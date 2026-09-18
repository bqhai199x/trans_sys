using System.Text;
using System.Text.Json;
using FileHandler.Api.Common;
using FileHandler.Api.Contracts;
using FileHandler.Api.Controllers;
using FileHandler.Api.Diagnostics;
using FileHandler.Api.Modules.Markdown;
using FileHandler.Api.Modules.PlainText;
using FileHandler.Api.Modules.Word;
using FileHandler.Api.Modules.Excel;
using FileHandler.Api.Modules.PowerPoint;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FileHandler.Tests.Diagnostics;

/// <summary>
/// Regression tests for state values, mutation timing and business item ownership.
/// </summary>
public sealed class TraceStatePlacementTests
{

    /// <summary>
    /// Verifies unsupported names never appear as successfully detected Markdown.
    /// </summary>
    /// <param name="name">Client file name to detect.</param>
    /// <param name="expected">Supported type name, or null when unsupported.</param>
    /// <returns>Task representing trace assertions.</returns>
    [Theory]
    [InlineData("guide.md", "Markdown")]
    [InlineData("guide.TXT", "PlainText")]
    [InlineData("guide.pdf", null)]
    [InlineData(null, null)]
    public async Task Detection_RecordsOnlySuccessfulType(string? name, string? expected)
    {
        var log = await CaptureAsync(() =>
        {
            Assert.Equal(expected is not null, FileTypeDetector.TryDetect(name, out _));
            return Task.CompletedTask;
        });
        var states = States(Call(log, "FileTypeDetector.TryDetect"), "fileType");
        if (expected is null)
            Assert.Empty(states);
        else
            Assert.Equal(expected, Assert.Single(states).GetString());
    }

    /// <summary>
    /// Verifies extraction work belongs to its block and buffers preserve before/after snapshots.
    /// </summary>
    /// <returns>Task representing trace assertions.</returns>
    [Fact]
    public async Task MarkdownExtraction_GroupsWorkBeforeProcessingAndSnapshotsBuffers()
    {
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var log = await CaptureAsync(async () =>
        {
            using var source = Source("# Hello *world*\n\n`code`\n\nTail");
            var result = await service.ImportAsync(source);
            Assert.Empty(result.Errors);
            Assert.Equal(2, result.Texts.Count);
        });
        var extract = Call(log, "MarkdownExtractor.Extract");
        var items = extract.GetProperty("children").EnumerateArray().Where(x => x.GetProperty("type").GetString() == "item").ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, items.Select(x => x.GetProperty("index").GetInt32()));
        Assert.All(items, item => Assert.Contains(Nodes(item), x => Target(x) == "MarkdownExtractor.EncodeContainer"));
        Assert.Equal("noTranslatableText", Assert.Single(States(items[1], "outcome")).GetString());
        Assert.Empty(States(items[1], "unitIndex"));
        Assert.Equal(1, Assert.Single(States(items[2], "unitIndex")).GetInt32());
        Assert.Equal(2, Assert.Single(States(extract, "unitCount")).GetInt32());
        var literal = Nodes(items[0]).First(x => Target(x) == "MarkdownExtractor.EncodeInline");
        var buffers = States(literal, "buffer");
        Assert.Equal(2, buffers.Length);
        Assert.Equal("", buffers[0].GetProperty("text").GetString());
        Assert.Equal("Hello ", buffers[1].GetProperty("text").GetString());
        Assert.All(Nodes(extract).Where(x => Target(x) != "MarkdownExtractor.EncodeInline"), node => Assert.Empty(States(node, "buffer")));
        var marker = Assert.Single(States(Call(log, "MarkdownExtractor.AddFormatting"), "marker"));
        Assert.Equal("*", marker.GetProperty("openSource").GetString());
    }

    /// <summary>
    /// Verifies unchanged units are distinguished from queued replacements and applied patch order.
    /// </summary>
    /// <returns>Task representing trace assertions.</returns>
    [Fact]
    public async Task MarkdownExport_RecordsDecisionsAndReversePatchOrder()
    {
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var log = await CaptureAsync(async () =>
        {
            using var source = Source("# Title\n\nOne\n\nTwo");
            var result = await service.ExportAsync(source, ["Title", "Một", "Hai"]);
            Assert.Empty(result.Errors);
            Assert.Equal("# Title\n\nMột\n\nHai", Encoding.UTF8.GetString(result.Content!));
        });
        var apply = Call(log, "MarkdownTranslationApplier.Apply");
        var items = apply.GetProperty("children").EnumerateArray().Where(x => x.GetProperty("type").GetString() == "item").ToArray();
        Assert.Equal(new[] { "unchanged", "replacementQueued", "replacementQueued" }, items.Select(x => Assert.Single(States(x, "outcome")).GetString()));
        var patches = States(apply, "patch");
        Assert.Equal(2, patches.Length);
        Assert.True(patches[0].GetProperty("start").GetInt32() > patches[1].GetProperty("start").GetInt32());
        Assert.Equal("Hai", patches[0].GetProperty("value").GetString());
        Assert.Equal("Một", patches[1].GetProperty("value").GetString());
        var stages = States(Call(log, "MarkdownService.ExportAsync"), "stage");
        Assert.Equal(new[] { "readSource", "extractUnits", "applyTranslations", "validateStructure", "encodeOutput" }, stages.Select(x => x.GetString()));
    }

    /// <summary>
    /// Verifies formatting normalization records distinct original and adjusted token snapshots.
    /// </summary>
    /// <returns>Task representing trace assertions.</returns>
    [Fact]
    public async Task MarkdownFormatting_RecordsTokensBeforeAndAfterNormalization()
    {
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var log = await CaptureAsync(async () =>
        {
            using var source = Source("*Hello*");
            var result = await service.ExportAsync(source, [" Bonjour "]);
            Assert.Empty(result.Errors);
            Assert.Equal(" *Bonjour* ", Encoding.UTF8.GetString(result.Content!));
        });
        var tokens = States(Call(log, "MarkdownTranslationApplier.DecodeTranslation"), "tokens");
        Assert.Equal(2, tokens.Length);
        Assert.True(tokens[0][0].GetProperty("isMarker").GetBoolean());
        Assert.Equal(" Bonjour ", tokens[0][1].GetProperty("value").GetString());
        Assert.False(tokens[1][0].GetProperty("isMarker").GetBoolean());
        Assert.Equal("Bonjour", tokens[1][2].GetProperty("value").GetString());
        var signatures = States(Call(log, "MarkdownExtractor.ValidateStructure"), "signature");
        Assert.Equal(2, signatures.Length);
        Assert.Equal(signatures[0].GetRawText(), signatures[1].GetRawText());
    }

    /// <summary>
    /// Verifies token validation fails before restoration without claiming later stages ran.
    /// </summary>
    /// <returns>Task representing trace assertions.</returns>
    [Fact]
    public async Task MarkdownMarkerFailure_RecordsValidationAndStopsStages()
    {
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var log = await CaptureAsync(async () =>
        {
            using var source = Source("**Hello**!");
            var result = await service.ExportAsync(source, ["Goodbye"]);
            Assert.Contains(result.Errors, x => x.Code == "invalid_marker_syntax");
            Assert.Null(result.Content);
        });
        Assert.Equal("invalid_marker_syntax", Assert.Single(States(Call(log, "MarkdownTokenCodec.Decode"), "validation")).GetProperty("code").GetString());
        Assert.Equal("applyTranslations", States(Call(log, "MarkdownService.ExportAsync"), "stage")[^1].GetString());
        Assert.DoesNotContain(AllNodes(log), x => Target(x) == "MarkdownExtractor.ValidateStructure");
    }

    /// <summary>
    /// Verifies byte progression includes BOM, multibyte translations and trailing separators on failure.
    /// </summary>
    /// <param name="limit">Output byte limit triggering failure inside loop or at trailing separator.</param>
    /// <param name="expectedBytes">Last measured output byte count.</param>
    /// <returns>Task representing trace assertions.</returns>
    [Theory]
    [InlineData(9, 10)]
    [InlineData(10, 11)]
    public async Task PlainTextBudget_RecordsBytesBeforeEveryRejection(long limit, long expectedBytes)
    {
        var service = new PlainTextService(Options.Create(new FileHandlingOptions { MaxOutputBytes = limit }));
        var log = await CaptureAsync(async () =>
        {
            using var source = Source("\uFEFFOne\n\nTwo\n");
            var result = await service.ExportAsync(source, ["é", "界"]);
            Assert.Equal("output_too_large", Assert.Single(result.Errors).Code);
        });
        var bytes = States(Call(log, "PlainTextService.FitsOutputLimit"), "outputBytes");
        Assert.Equal(3, bytes[0].GetInt64());
        Assert.Equal(5, bytes[1].GetInt64());
        Assert.Equal(expectedBytes, bytes[^1].GetInt64());
        Assert.Equal("checkOutputLimit", States(Call(log, "PlainTextService.ExportAsync"), "stage")[^1].GetString());
        Assert.DoesNotContain(AllNodes(log), x => Target(x) == "PlainTextService.Compose");
    }

    /// <summary>
    /// Verifies TXT composition records identity or replacement decisions for each paragraph.
    /// </summary>
    /// <param name="identity">Whether every translation matches source text.</param>
    /// <returns>Task representing trace assertions.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PlainTextComposition_RecordsUnitOutcomes(bool identity)
    {
        var service = new PlainTextService(Options.Create(new FileHandlingOptions()));
        var log = await CaptureAsync(async () =>
        {
            using var source = Source("One\r\n\r\nTwo");
            var result = await service.ExportAsync(source, ["One", identity ? "Two" : "Hai"]);
            Assert.Empty(result.Errors);
            Assert.Equal(identity ? "One\r\n\r\nTwo" : "One\r\n\r\nHai", Encoding.UTF8.GetString(result.Content!));
        });
        var compose = Call(log, "PlainTextService.Compose");
        Assert.Equal(identity, Assert.Single(States(compose, "identity")).GetBoolean());
        var items = compose.GetProperty("children").EnumerateArray().Where(x => x.GetProperty("type").GetString() == "item").ToArray();
        Assert.Equal("unchanged", Assert.Single(States(items[0], "outcome")).GetString());
        Assert.Equal(identity ? "unchanged" : "replaced", Assert.Single(States(items[1], "outcome")).GetString());
        Assert.Equal(3, Assert.Single(States(items[1], "unit")).GetProperty("line").GetProperty("start").GetInt32());
    }

    /// <summary>
    /// Verifies segmentation identifies attempted paragraph and physical line on quota failure.
    /// </summary>
    /// <returns>Task representing trace assertions.</returns>
    [Fact]
    public async Task PlainTextSegmentation_RecordsQuotaLocation()
    {
        var log = await CaptureAsync(() =>
        {
            var result = PlainTextSegmenter.Segment("One\n\nTwo", 1, default);
            Assert.Equal("too_many_units", result.Error!.Code);
            return Task.CompletedTask;
        });
        var segment = Call(log, "PlainTextSegmenter.Segment");
        Assert.Equal(2, Assert.Single(States(segment, "attemptedUnitCount")).GetInt32());
        Assert.Equal(3, Assert.Single(States(segment, "line")).GetInt32());
    }

    /// <summary>
    /// Verifies read failure records bytes or BOM before returning its validation error.
    /// </summary>
    /// <param name="limit">Source byte limit selecting size or encoding failure.</param>
    /// <param name="code">Expected source error code.</param>
    /// <returns>Task representing trace assertions.</returns>
    [Theory]
    [InlineData(3, "file_too_large")]
    [InlineData(100, "invalid_encoding")]
    public async Task ReaderFailure_RecordsMeasuredBytesAndBom(long limit, string code)
    {
        var log = await CaptureAsync(async () =>
        {
            using var source = new MemoryStream([0xef, 0xbb, 0xbf, 0xff]);
            var result = await Utf8TextReader.ReadAsync(source, limit, default);
            Assert.Equal(code, result.Error!.Code);
        });
        Assert.Equal(4, Assert.Single(States(Call(log, "Utf8TextReader.ReadAsync"), "bytesRead")).GetInt64());
        if (code == "invalid_encoding")
            Assert.True(Assert.Single(States(Call(log, "Utf8TextReader.DecodeUtf8"), "hasBom")).GetBoolean());
        else
            Assert.DoesNotContain(AllNodes(log), x => Target(x) == "Utf8TextReader.DecodeUtf8");
    }

    /// <summary>
    /// Verifies normalized JSON is recorded within parsing scope and optional content remains hidden.
    /// </summary>
    /// <param name="captureContent">Whether snapshots contain source and translation content.</param>
    /// <returns>Task representing trace assertions.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ControllerNormalization_RecordsParseProgressAndHonorsRedaction(bool captureContent)
    {
        var options = Options.Create(new FileHandlingOptions());
        var controller = new FilesController(
            MarkdownService.Create(options),
            new PlainTextService(options),
            WordService.Create(options),
            ExcelService.Create(options),
            PowerPointService.Create(options));
        var log = await CaptureAsync(async () =>
        {
            using var source = Source("SecretSource");
            var file = new FormFile(source, 0, source.Length, "file", "sample.txt") { Headers = new HeaderDictionary(), ContentType = "text/plain" };
            var result = await controller.Export(new ExportRequest { File = file, TranslatedTexts = "[\"SecretTranslation\nTail\"]" }, default);
            Assert.IsType<FileContentResult>(result);
        }, captureContent);
        var parse = Call(log, "FilesController.TryParseTranslations");
        if (captureContent)
        {
            Assert.Equal(new[] { "original", "normalizedNewlines" }, States(parse, "parseMode").Select(x => x.GetString()));
            Assert.Equal("SecretTranslation\nTail", Assert.Single(States(parse, "jsonText"))[0].GetString());
            Assert.Equal(1, Assert.Single(States(Call(log, "FilesController.Export"), "translationCount")).GetInt32());
        }
        else
        {
            Assert.DoesNotContain("SecretSource", log.GetRawText());
            Assert.DoesNotContain("SecretTranslation", log.GetRawText());
            Assert.All(AllNodes(log).SelectMany(x => x.GetProperty("states").EnumerateArray()), state => Assert.Equal("[Hidden]", state.GetProperty("value").GetString()));
        }
    }

    /// <summary>
    /// Verifies cancellation leaves last started stage without manufacturing later progress.
    /// </summary>
    /// <returns>Task representing trace assertions.</returns>
    [Fact]
    public async Task Cancellation_LeavesLastStartedStage()
    {
        var service = new PlainTextService(Options.Create(new FileHandlingOptions()));
        var log = await CaptureAsync(async () =>
        {
            using var source = Source("One");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExportAsync(source, ["Two"], new CancellationToken(true)));
        });
        Assert.Equal("readSource", Assert.Single(States(Call(log, "PlainTextService.ExportAsync"), "stage")).GetString());
        Assert.DoesNotContain(AllNodes(log), x => Target(x) == "PlainTextSegmenter.Segment");
    }

    /// <summary>
    /// Creates UTF-8 source stream for a test workflow.
    /// </summary>
    /// <param name="text">Source text including optional BOM.</param>
    /// <returns>Readable in-memory source stream.</returns>
    private static MemoryStream Source(string text) => new(Encoding.UTF8.GetBytes(text));

    /// <summary>
    /// Executes workflow within isolated trace session and reads persisted snapshots.
    /// </summary>
    /// <param name="action">Workflow and result assertions to execute.</param>
    /// <param name="captureContent">Whether snapshots include captured values.</param>
    /// <returns>Detached JSON trace document.</returns>
    private static async Task<JsonElement> CaptureAsync(Func<Task> action, bool captureContent = true)
    {
        var previous = DebugTrace.Current;
        var path = Path.Combine(Path.GetTempPath(), $"trace-state-{Guid.NewGuid():N}.json");
        try
        {
            using (var session = new TraceSession(path, new DebugTraceOptions { CaptureContent = captureContent }, NullLogger.Instance))
            using (var root = new TraceCall(session, null, "Test", "Workflow"))
            {
                DebugTrace.Current = root;
                await action();
                session.WriteResult(200, 0);
            }
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.DoesNotContain("[Unavailable]", document.RootElement.GetRawText());
            Assert.DoesNotContain("MaxEvents reached", document.RootElement.GetRawText());
            return document.RootElement.Clone();
        }
        finally
        {
            DebugTrace.Current = previous;
            File.Delete(path);
        }
    }

    /// <summary>
    /// Finds unique call by target within complete trace document.
    /// </summary>
    /// <param name="log">Root JSON trace document.</param>
    /// <param name="target">Fully qualified trace target.</param>
    /// <returns>Single matching call node.</returns>
    private static JsonElement Call(JsonElement log, string target) => Assert.Single(AllNodes(log), x => Target(x) == target);

    /// <summary>
    /// Enumerates all call and item nodes in trace document.
    /// </summary>
    /// <param name="log">Root JSON trace document.</param>
    /// <returns>Depth-first sequence of trace nodes.</returns>
    private static IEnumerable<JsonElement> AllNodes(JsonElement log) => log.GetProperty("calls").EnumerateArray().SelectMany(Nodes);

    /// <summary>
    /// Enumerates node and descendants while retaining parent scope boundaries.
    /// </summary>
    /// <param name="node">Call or item node to traverse.</param>
    /// <returns>Node followed by depth-first descendants.</returns>
    private static IEnumerable<JsonElement> Nodes(JsonElement node)
    {
        yield return node;
        foreach (var child in node.GetProperty("children").EnumerateArray())
            foreach (var descendant in Nodes(child))
                yield return descendant;
    }

    /// <summary>
    /// Reads optional target name from call or item node.
    /// </summary>
    /// <param name="node">Trace node to inspect.</param>
    /// <returns>Call target, or null for an item.</returns>
    private static string? Target(JsonElement node) => node.TryGetProperty("target", out var target) ? target.GetString() : null;

    /// <summary>
    /// Reads named snapshots in their original capture order within a single scope.
    /// </summary>
    /// <param name="node">Trace node owning snapshots.</param>
    /// <param name="name">State name to select.</param>
    /// <returns>Snapshot values in capture order.</returns>
    private static JsonElement[] States(JsonElement node, string name) => node.GetProperty("states").EnumerateArray()
        .Where(x => x.GetProperty("name").GetString() == name).Select(x => x.GetProperty("value")).ToArray();
}
