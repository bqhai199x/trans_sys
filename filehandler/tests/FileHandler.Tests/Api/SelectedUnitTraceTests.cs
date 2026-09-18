using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FileHandler.Api.Diagnostics;
using FileHandler.Tests.Modules.Office;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileHandler.Tests.Api;

/// <summary>
/// Verifies complete selected-unit traces and runtime index settings.
/// </summary>
[Collection("Debug trace runtime toggle")]
public sealed class SelectedUnitTraceTests
{

    /// <summary>
    /// Checks index normalization, validation and metadata-only defaults through HTTP.
    /// </summary>
    /// <returns>Task completing after API assertions.</returns>
    [Fact]
    public async Task Settings_ValidateAndNormalizeSelection()
    {
        var previous = DebugTrace.UnitIndexesOverride;
        try
        {
            DebugTrace.UnitIndexesOverride = null;
            using var factory = new WebApplicationFactory<Program>();
            using var client = factory.CreateClient();
            Assert.Empty((await client.GetFromJsonAsync<DebugSettings>("/debug/settings"))!.UnitIndexes);
            var response = await client.PutAsJsonAsync("/debug/settings", new { unitIndexes = new[] { 3, 0, 3 } });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(new[] { 0, 3 }, (await response.Content.ReadFromJsonAsync<DebugSettings>())!.UnitIndexes);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/debug/settings", new { unitIndexes = new[] { -1 } })).StatusCode);
            Assert.Equal(new[] { 0, 3 }, (await client.GetFromJsonAsync<DebugSettings>("/debug/settings"))!.UnitIndexes);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/debug/settings", new { unitIndexes = (int[]?)null })).StatusCode);
        }
        finally { DebugTrace.UnitIndexesOverride = previous; }
    }

    /// <summary>
    /// Checks long late-selected sentences survive old limits without leaking other units.
    /// </summary>
    /// <param name="extension">Text-based input format.</param>
    /// <returns>Task completing after trace and response assertions.</returns>
    [Theory]
    [InlineData("txt")]
    [InlineData("md")]
    public async Task Export_OnlySelectedLateUnitIsCapturedCompletely(string extension)
    {
        var directory = NewDirectory();
        try
        {
            using var factory = Factory(directory, [150, 999]);
            using var client = factory.CreateClient();
            var sources = Enumerable.Range(0, 151).Select(i => $"UnselectedSource{i}").ToArray();
            sources[150] = "SelectedSource" + new string('源', 24000);
            var translations = Enumerable.Range(0, 151).Select(i => $"UnselectedTranslation{i}").ToArray();
            translations[150] = "SelectedTranslation" + new string('訳', 24000);
            using var form = Form(Encoding.UTF8.GetBytes(string.Join("\n\n", sources)), extension, translations);
            var response = await client.PostAsync("/export", form);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var log = File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "*.json")));
            Assert.Contains(sources[150], log);
            Assert.Contains(translations[150], log);
            Assert.DoesNotContain("UnselectedSource", log);
            Assert.DoesNotContain("UnselectedTranslation", log);
            Assert.DoesNotContain("[Truncated]", log);
            Assert.DoesNotContain("[Unavailable]", log);
            using var document = JsonDocument.Parse(log);
            Assert.Equal(2, document.RootElement.GetProperty("version").GetInt32());
            Assert.Equal(999, Assert.Single(document.RootElement.GetProperty("missingUnitIndexes").EnumerateArray()).GetInt32());
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>
    /// Checks Office extraction, style decisions, decoding and scalar patches share index zero.
    /// </summary>
    /// <param name="extension">Office input format.</param>
    /// <returns>Task completing after trace assertions.</returns>
    [Theory]
    [InlineData("docx")]
    [InlineData("pptx")]
    [InlineData("xlsx")]
    public async Task OfficeTrace_ContainsCompleteDateWorkflow(string extension)
    {
        var directory = NewDirectory();
        try
        {
            using var factory = Factory(directory, [0]);
            using var client = factory.CreateClient();
            using var form = Form(OfficeStyleCoalescingTests.CreateDate(extension), extension, ["ngày 31 tháng 3 năm 2026"]);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/export", form)).StatusCode);
            var log = File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "*.json")));
            Assert.Contains("2026年3月31日", log);
            Assert.Contains("ngày 31 tháng 3 năm 2026", log);
            Assert.Contains("merged", log);
            Assert.Contains("\"unitIndex\": 0", log);
            Assert.Contains("\"target\": \"decode\"", log);
            Assert.Contains("\"target\": \"apply\"", log);
            Assert.Contains("\"name\": \"scalar\"", log);
            Assert.DoesNotContain("[Unavailable]", log);
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>
    /// Verifies empty and missing selections never enable full-content capture.
    /// </summary>
    /// <param name="selectMissing">Whether to select an out-of-range index.</param>
    /// <returns>Task completing after trace assertions.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrEmptySelection_ContainsNoText(bool selectMissing)
    {
        var directory = NewDirectory();
        try
        {
            using var factory = Factory(directory, selectMissing ? [99] : []);
            using var client = factory.CreateClient();
            using var form = Form(Encoding.UTF8.GetBytes("PrivateSource"), "md", ["PrivateTranslation"]);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/export", form)).StatusCode);
            var log = File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "*.json")));
            Assert.DoesNotContain("PrivateSource", log);
            Assert.DoesNotContain("PrivateTranslation", log);
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>
    /// Ensures rejected unit factories are skipped and selected values exceed legacy quotas.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void SelectedUnit_SkipsUnselectedFactoriesAndRestoresContext()
    {
        var directory = NewDirectory();
        Directory.CreateDirectory(directory);
        var previous = DebugTrace.Current;
        try
        {
            using var session = new TraceSession(Path.Combine(directory, "trace.json"), new() { UnitIndexes = [1], MaxEvents = 1, MaxValueLength = 1, MaxTraceBytes = 1 }, NullLogger.Instance);
            using var root = new TraceCall(session, null, "Test", "Request");
            DebugTrace.Current = root;
            using (var unit = DebugTrace.Unit(0, "validate"))
            {
                unit.State("secret", () => throw new InvalidOperationException("Factory must not run"));
                using var nested = DebugTrace.Enter("Test", "Nested", () => throw new InvalidOperationException("Input must not run"));
                nested.State("secret", () => throw new InvalidOperationException("State must not run"));
            }
            Assert.Same(root, DebugTrace.Current);
            using (var unit = DebugTrace.Unit(1, "validate")) unit.State("values", () => Enumerable.Range(0, 200).ToArray());
            Assert.Single(root.Node.Children);
            Assert.Equal(200, ((JsonElement)root.Node.Children[0].States.Single().Value!).GetArrayLength());
        }
        finally
        {
            DebugTrace.Current = previous;
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Creates unique trace output directory name.
    /// </summary>
    /// <returns>Absolute temporary directory path.</returns>
    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "selected-trace-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies unit validation failures retain selected details without claiming apply ran.
    /// </summary>
    /// <param name="extension">Text-based input format.</param>
    /// <returns>Task completing after failure trace assertions.</returns>
    [Theory]
    [InlineData("txt")]
    [InlineData("md")]
    public async Task ValidationFailure_CapturesOnlySelectedError(string extension)
    {
        var directory = NewDirectory();
        try
        {
            using var factory = Factory(directory, [1]);
            using var client = factory.CreateClient();
            using var form = Form(Encoding.UTF8.GetBytes("UnselectedSource\n\nSelectedSource"), extension, ["UnselectedTranslation", ""]);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync("/export", form)).StatusCode);
            var log = File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "*.json")));
            Assert.Contains("empty_translation", log);
            Assert.Contains("SelectedSource", log);
            Assert.DoesNotContain("UnselectedSource", log);
            Assert.DoesNotContain("UnselectedTranslation", log);
            Assert.DoesNotContain("\"target\": \"apply\"", log);
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>
    /// Verifies selection is immutable after session construction.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void SelectionSnapshot_IgnoresLaterOptionChanges()
    {
        var directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DebugTraceOptions { UnitIndexes = [2, 0, 2] };
            using var session = new TraceSession(Path.Combine(directory, "snapshot.json"), options, NullLogger.Instance);
            options.UnitIndexes[0] = 1;
            options.UnitIndexes = [9];
            Assert.True(session.Selects(0));
            Assert.True(session.Selects(2));
            Assert.False(session.Selects(1));
            Assert.False(session.Selects(9));
            Assert.Equal(new[] { 0, 2 }, session.Document.UnitIndexes);
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>
    /// Configures a tracing host with deliberately small legacy limits.
    /// </summary>
    /// <param name="directory">Trace output directory.</param>
    /// <param name="indices">Selected zero-based translation indices.</param>
    /// <returns>Configured test host.</returns>
    private static WebApplicationFactory<Program> Factory(string directory, int[] indices) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<DebugTraceOptions>(options =>
            {
                options.Enabled = true;
                options.Directory = directory;
                options.UnitIndexes = indices;
                options.MaxValueLength = 64;
                options.MaxEvents = 100;
                options.MaxTraceBytes = 4096;
            })));

    /// <summary>
    /// Creates multipart source and translations.
    /// </summary>
    /// <param name="source">Source bytes.</param>
    /// <param name="extension">Source file extension.</param>
    /// <param name="translations">Ordered translated texts.</param>
    /// <returns>Multipart request content.</returns>
    private static MultipartFormDataContent Form(byte[] source, string extension, string[] translations) => new()
    {
        { new ByteArrayContent(source), "file", "sample." + extension },
        { new StringContent(JsonSerializer.Serialize(translations)), "translatedTexts" }
    };
}
