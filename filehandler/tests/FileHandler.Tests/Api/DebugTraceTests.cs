using System.Net;
using System.Text;
using System.Text.Json;
using FileHandler.Api.Diagnostics;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileHandler.Tests.Api;

/// <summary>
/// Integration tests verifying end-to-end diagnostic tracing behavior.
/// </summary>
public sealed class DebugTraceTests
{

    /// <summary>
    /// Builds unique temporary path for trace test output.
    /// </summary>
    /// <returns>Unique temporary trace directory path.</returns>
    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "filehandler-trace-tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Creates test host with requested tracing settings.
    /// </summary>
    /// <param name="directory">Trace output directory.</param>
    /// <param name="enabled">Whether tracing is enabled.</param>
    /// <param name="captureContent">Whether trace values include captured content.</param>
    /// <returns>Test host configured for tracing.</returns>
    private static WebApplicationFactory<Program> Factory(string directory, bool enabled = true, bool captureContent = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<DebugTraceOptions>(options =>
            {
                options.Enabled = enabled;
                options.Directory = directory;
                options.CaptureContent = captureContent;
            })));

    /// <summary>
    /// Builds multipart request with source file and optional translations.
    /// </summary>
    /// <param name="source">Original source text.</param>
    /// <param name="translations">Optional JSON array of translated strings.</param>
    /// <param name="fileName">Source file name selecting document handler.</param>
    /// <returns>Multipart content containing source file and optional translations.</returns>
    private static MultipartFormDataContent Form(string source, string? translations = null, string fileName = "sample.md")
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(source)), "file", fileName } };
        if (translations is not null) form.Add(new StringContent(translations), "translatedTexts");
        return form;
    }

    /// <summary>
    /// Verifies isolated request traces with balanced calls, item scopes, and captured values in JSON.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task ImportAndExportHaveIsolatedBalancedCallsAndRealValues()
    {
        var directory = NewDirectory();
        using var factory = Factory(directory);
        using var client = factory.CreateClient();
        using var import = Form("# ImportOnly\n\nSecond paragraph");
        using var export = Form("# ExportOnly\n\nSecond paragraph", "[\"Xin chào\", \"Đoạn hai\"]");
        var responses = await Task.WhenAll(client.PostAsync("/import", import), client.PostAsync("/export", export));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal("# Xin chào\n\nĐoạn hai", await responses[1].Content.ReadAsStringAsync());
        var files = Directory.GetFiles(directory, "*.json");
        Assert.Equal(2, files.Length);
        var logs = files.Select(File.ReadAllText).ToArray();
        var importLog = Assert.Single(logs, log => log.Contains("ImportAsync"));
        var exportLog = Assert.Single(logs, log => log.Contains("ExportAsync"));
        Assert.Contains("ImportOnly", importLog);
        Assert.DoesNotContain("ExportOnly", importLog);
        Assert.Contains("Xin chào", exportLog);
        Assert.DoesNotContain("ImportOnly", exportLog);
        Assert.Contains("\"name\": \"unit\"", importLog);
        Assert.Contains("\"type\": \"item\"", importLog);
        Assert.Contains("\"index\": 1", importLog);
        Assert.Contains("\"type\": \"item\"", exportLog);
        Assert.Contains("DecodeTranslation", exportLog);
        Assert.Contains("ValidateStructure", exportLog);
        foreach (var log in logs)
        {
            using var doc = JsonDocument.Parse(log);
            Assert.Equal(200, doc.RootElement.GetProperty("status").GetInt32());
            Assert.True(doc.RootElement.GetProperty("calls").GetArrayLength() > 0);
            Assert.DoesNotContain("[Unavailable]", log);
        }
    }

    /// <summary>
    /// Verifies that disabled tracing skips files and value evaluation.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task DisabledDoesNotCreateDirectoryOrEvaluateSnapshots()
    {
        var directory = NewDirectory();
        using var factory = Factory(directory, enabled: false);
        using var client = factory.CreateClient();
        using var form = Form("# Hello");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/import", form)).StatusCode);
        Assert.False(Directory.Exists(directory));
        var evaluated = false;
        using var trace = DebugTrace.Enter("Test", "Disabled", () => { evaluated = true; return null; });
        trace.State("Ignored", () => { evaluated = true; return null; });
        trace.Return(1);
        Assert.False(evaluated);
    }

    /// <summary>
    /// Verifies that invalid UTF-8 is traced and returns validation error in JSON.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task InvalidUtf8RecordsErrorAndStillReturnsValidationResponse()
    {
        var directory = NewDirectory();
        using var factory = Factory(directory);
        using var client = factory.CreateClient();
        using var form = new MultipartFormDataContent { { new ByteArrayContent([0xff]), "file", "bad.md" } };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync("/import", form)).StatusCode);
        var log = File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "*.json")));
        using var doc = JsonDocument.Parse(log);
        Assert.Equal(422, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Contains("DecoderFallbackException", log);
        Assert.Contains("invalid_encoding", log);
    }

    /// <summary>
    /// Verifies that trace file failures do not interrupt requests.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task UnwritableDestinationDoesNotBreakRequest()
    {
        var directory = NewDirectory();
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(file, "preserve");
        using var factory = Factory(file);
        using var client = factory.CreateClient();
        using var form = Form("# Hello");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/import", form)).StatusCode);
        Assert.Equal("preserve", File.ReadAllText(file));
    }

    /// <summary>
    /// Verifies that hidden trace content excludes source and translated text.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task HiddenContentDoesNotLeakSourceOrTranslation()
    {
        var directory = NewDirectory();
        using var factory = Factory(directory, captureContent: false);
        using var client = factory.CreateClient();
        using var form = Form("# SecretSource", "[\"SecretTranslation\"]");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/export", form)).StatusCode);
        var log = File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "*.json")));
        Assert.Contains("[Hidden]", log);
        Assert.DoesNotContain("SecretSource", log);
        Assert.DoesNotContain("SecretTranslation", log);
    }

    /// <summary>
    /// Verifies TXT traces record its workflow and honor content redaction.
    /// </summary>
    /// <param name="captureContent">Whether source and translation snapshots are visible.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlainTextTraceRecordsWorkflowAndHonorsRedaction(bool captureContent)
    {
        var directory = NewDirectory();
        try
        {
            using var factory = Factory(directory, captureContent: captureContent);
            using var client = factory.CreateClient();
            using var import = Form("# PlainSource\n\nTail", fileName: "sample.txt");
            using var export = Form("# PlainSource\n\nTail", "[\"PlainTranslation\",\"NewTail\"]", "sample.txt");
            using var importResponse = await client.PostAsync("/import", import);
            using var exportResponse = await client.PostAsync("/export", export);
            Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
            var files = Directory.GetFiles(directory, "*.json");
            Assert.Equal(2, files.Length);
            foreach (var file in files)
            {
                var log = File.ReadAllText(file);
                using var document = JsonDocument.Parse(log);
                Assert.Equal(200, document.RootElement.GetProperty("status").GetInt32());
                Assert.Contains("PlainTextService", log);
                Assert.Contains("PlainTextSegmenter", log);
                Assert.Contains("Utf8TextReader", log);
                Assert.DoesNotContain("MarkdownExtractor", log);
                Assert.DoesNotContain("ValidateStructure", log);
                Assert.DoesNotContain("[Unavailable]", log);
                if (captureContent)
                    Assert.Contains("PlainSource", log);
                else
                {
                    Assert.Contains("[Hidden]", log);
                    Assert.DoesNotContain("PlainSource", log);
                    Assert.DoesNotContain("PlainTranslation", log);
                }
            }
            var exportLog = Assert.Single(files.Select(File.ReadAllText), log => log.Contains("ExportAsync"));
            Assert.Contains("FitsOutputLimit", exportLog);
            Assert.Contains("Compose", exportLog);
            Assert.Contains("ValidateTranslations", exportLog);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Verifies safe, closed JSON trace output under cancellation and limits.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void CancellationAndLimitsProduceClosedSafeJson()
    {
        var directory = NewDirectory();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "limited.json");
        using (var session = new TraceSession(path, new DebugTraceOptions { MaxEvents = 12, MaxValueLength = 64 }, NullLogger.Instance))
        {
            using var root = new TraceCall(session, null, "API", "Request");
            DebugTrace.Current = root;
            root.Input(() => new { Text = "```\nsequenceDiagram\n<script> & #59;" });
            using (var call = DebugTrace.Enter("Service", "Work", () => "start"))
                call.Error(new OperationCanceledException("cancel"));
            root.State("Long", () => new string('x', 10000));
            for (var i = 0; i < 30; i++) root.State("Loop", () => i);
            session.WriteResult(499, 10.0, new OperationCanceledException("cancel"), true);
        }
        Assert.Null(DebugTrace.Current);
        var log = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(log);
        Assert.Equal(499, doc.RootElement.GetProperty("status").GetInt32());
        Assert.True(doc.RootElement.GetProperty("cancelled").GetBoolean());
        Assert.Contains("cancel", log);
        Assert.Contains("Truncated", log);
        Assert.Contains("MaxEvents reached", log);
    }

    /// <summary>
    /// Verifies that hard event quota stops adding states and evaluating factories.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void EventLimit_StopsAddingStatesAndEvaluatingFactories()
    {
        var directory = NewDirectory();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "quota.json");
        var evaluatedCount = 0;
        using (var session = new TraceSession(path, new DebugTraceOptions { MaxEvents = 3, MaxValueLength = 64 }, NullLogger.Instance))
        {
            using var root = new TraceCall(session, null, "API", "Request");
            for (var i = 0; i < 20; i++)
            {
                root.State("TestState", () =>
                {
                    evaluatedCount++;
                    return "value";
                });
            }
            session.WriteResult(200, 5.0);
            Assert.True(root.Node.States.Count <= 2);
            Assert.True(evaluatedCount <= 2);
        }
        var log = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(log);
        Assert.Contains("MaxEvents reached", doc.RootElement.GetProperty("error").GetString());
    }

    /// <summary>
    /// Verifies that disabled content capture redacts exception messages in all branches.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void CaptureContent_WhenFalse_RedactsExceptionMessages()
    {
        var directory = NewDirectory();
        Directory.CreateDirectory(directory);

        var normalPath = Path.Combine(directory, "redacted-normal.json");
        using (var session = new TraceSession(normalPath, new DebugTraceOptions { CaptureContent = false, MaxEvents = 100 }, NullLogger.Instance))
        {
            using var root = new TraceCall(session, null, "API", "Request");
            session.WriteResult(500, 10.0, new InvalidOperationException("SUPER-SECRET-INFO-1"));
        }
        var normalLog = File.ReadAllText(normalPath);
        Assert.DoesNotContain("SUPER-SECRET-INFO-1", normalLog);
        Assert.Contains("[Redacted]", normalLog);

        var truncatedPath = Path.Combine(directory, "redacted-truncated.json");
        using (var session = new TraceSession(truncatedPath, new DebugTraceOptions { CaptureContent = false, MaxEvents = 2 }, NullLogger.Instance))
        {
            using var root = new TraceCall(session, null, "API", "Request");
            for (var i = 0; i < 5; i++) root.State("S", () => i);
            session.WriteResult(500, 10.0, new InvalidOperationException("SUPER-SECRET-INFO-2"));
        }
        var truncatedLog = File.ReadAllText(truncatedPath);
        Assert.DoesNotContain("SUPER-SECRET-INFO-2", truncatedLog);
        Assert.Contains("[Redacted]", truncatedLog);
        Assert.Contains("MaxEvents reached", truncatedLog);
    }

    /// <summary>
    /// Verifies trace logs are created from real import and export executions.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task RealExecutionCreatesValidTraceFiles()
    {
        var directory = NewDirectory();
        using var factory = Factory(directory);
        using var client = factory.CreateClient();
        var source = "# Hướng dẫn sử dụng\n\nChào mừng bạn đến với `FileHandler`. Đây là đoạn văn bản mẫu có liên kết [trang chủ](https://example.com).\n\n> Lưu ý: Giữ nguyên định dạng khi dịch tài liệu.";
        using var importForm = Form(source);
        var importResponse = await client.PostAsync("/import", importForm);
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var importedJson = await importResponse.Content.ReadAsStringAsync();
        var translations = JsonSerializer.Deserialize<string[]>(importedJson)!;
        Assert.Equal(3, translations.Length);
        translations[0] = "User Guide";
        translations[1] = translations[1].Replace("Chào mừng bạn đến với", "Welcome to").Replace("Đây là đoạn văn bản mẫu có liên kết", "This is sample text with link");
        translations[2] = "Note: Keep formatting when translating documents.";
        var exportJson = JsonSerializer.Serialize(translations);
        using var exportForm = Form(source, exportJson);
        var exportResponse = await client.PostAsync("/export", exportForm);
        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);

        var files = Directory.GetFiles(directory, "*.json").OrderBy(File.GetCreationTimeUtc).ToArray();
        Assert.Equal(2, files.Length);
        var importTrace = File.ReadAllText(files[0]);
        var exportTrace = File.ReadAllText(files[1]);

        using var importDoc = JsonDocument.Parse(importTrace);
        Assert.Equal("/import", importDoc.RootElement.GetProperty("path").GetString());

        using var exportDoc = JsonDocument.Parse(exportTrace);
        Assert.Equal("/export", exportDoc.RootElement.GetProperty("path").GetString());
    }
}
