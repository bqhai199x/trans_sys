using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FileHandler.Api.Diagnostics;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FileHandler.Tests.Api;

/// <summary>
/// Unit and integration tests for debug trace management controller.
/// </summary>
[Collection("Debug trace runtime toggle")]
public sealed class DebugControllerTests : IDisposable
{

    /// <summary>
    /// Temporary directory for debug trace storage in tests.
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "debug-controller-tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Cleans up temporary test files.
    /// </summary>
    /// <returns>No return value.</returns>
    public void Dispose()
    {
        DebugTrace.EnabledOverride = null;
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Creates test host configured with test trace directory.
    /// </summary>
    /// <returns>Configured web application factory.</returns>
    private WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<DebugTraceOptions>(options =>
            {
                options.Enabled = true;
                options.Directory = _directory;
                options.CaptureContent = true;
            })));

    /// <summary>
    /// Verifies that HTML viewer page is served at root debug endpoint.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Viewer_ReturnsHtmlContent()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/debug");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("FileHandler Debug Trace", html);
    }

    /// <summary>
    /// Verifies status endpoint returns current options and log counts.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Status_ReturnsCurrentConfigurationAndLogCount()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/debug/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<DebugStatusResponse>();
        Assert.NotNull(status);
        Assert.True(status.Enabled);
        Assert.Equal(0, status.TotalLogs);
    }

    /// <summary>
    /// Verifies toggle endpoint modifies active debug state.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Toggle_UpdatesRuntimeDebugEnablement()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        var offResponse = await client.PostAsJsonAsync("/debug/toggle", new ToggleDebugRequest { Enabled = false });
        Assert.Equal(HttpStatusCode.OK, offResponse.StatusCode);
        Assert.False(DebugTrace.EnabledOverride);

        var onResponse = await client.PostAsJsonAsync("/debug/toggle", new ToggleDebugRequest { Enabled = true });
        Assert.Equal(HttpStatusCode.OK, onResponse.StatusCode);
        Assert.True(DebugTrace.EnabledOverride);
    }

    /// <summary>
    /// Verifies logs listing, filtering, detail retrieval, and deletion lifecycle.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Logs_SupportsListingFilteringGetAndDelete()
    {
        Directory.CreateDirectory(_directory);
        var log1Path = Path.Combine(_directory, "log-1.json");
        var log2Path = Path.Combine(_directory, "log-2.json");

        var doc1 = new TraceDocument
        {
            Id = "log-1",
            Timestamp = DateTime.UtcNow.AddMinutes(-5),
            Method = "POST",
            Path = "/import",
            Status = 200,
            DurationMs = 8.5,
            Calls = [new TraceNode { Type = "call", Target = "MarkdownService.ImportAsync", In = new { Text = "Hello extraction" } }]
        };
        var doc2 = new TraceDocument
        {
            Id = "log-2",
            Timestamp = DateTime.UtcNow,
            Method = "POST",
            Path = "/export",
            Status = 422,
            DurationMs = 15.2,
            Error = "invalid_structure",
            Calls = [new TraceNode { Type = "call", Target = "MarkdownTranslationApplier.Apply" }]
        };

        File.WriteAllText(log1Path, JsonSerializer.Serialize(doc1));
        File.WriteAllText(log2Path, JsonSerializer.Serialize(doc2));

        using var factory = Factory();
        using var client = factory.CreateClient();

        // 1. List all logs
        var listResponse = await client.GetAsync("/debug/logs");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<DebugLogSummary[]>();
        Assert.NotNull(list);
        Assert.Equal(2, list.Length);

        // 2. Filter by search term (extracted text)
        var searchResponse = await client.GetAsync("/debug/logs?search=extraction");
        var searchList = await searchResponse.Content.ReadFromJsonAsync<DebugLogSummary[]>();
        Assert.NotNull(searchList);
        Assert.Single(searchList);
        Assert.Equal("log-1", searchList[0].Id);

        // 3. Filter by function name
        var fnResponse = await client.GetAsync("/debug/logs?function=MarkdownTranslationApplier");
        var fnList = await fnResponse.Content.ReadFromJsonAsync<DebugLogSummary[]>();
        Assert.NotNull(fnList);
        Assert.Single(fnList);
        Assert.Equal("log-2", fnList[0].Id);

        // 4. Filter by status code
        var statusResponse = await client.GetAsync("/debug/logs?status=422");
        var statusList = await statusResponse.Content.ReadFromJsonAsync<DebugLogSummary[]>();
        Assert.NotNull(statusList);
        Assert.Single(statusList);
        Assert.Equal("log-2", statusList[0].Id);

        // 5. Get detail by ID
        var detailResponse = await client.GetAsync("/debug/logs/log-1");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detailDoc = await detailResponse.Content.ReadFromJsonAsync<TraceDocument>();
        Assert.NotNull(detailDoc);
        Assert.Equal("log-1", detailDoc.Id);

        // 6. Delete single log
        var deleteSingle = await client.DeleteAsync("/debug/logs/log-1");
        Assert.Equal(HttpStatusCode.OK, deleteSingle.StatusCode);
        Assert.False(File.Exists(log1Path));
        Assert.True(File.Exists(log2Path));

        // 7. Delete non-existent returns 404
        var notFoundDelete = await client.DeleteAsync("/debug/logs/nonexistent");
        Assert.Equal(HttpStatusCode.NotFound, notFoundDelete.StatusCode);

        // 8. Delete all logs
        var deleteAll = await client.DeleteAsync("/debug/logs");
        Assert.Equal(HttpStatusCode.OK, deleteAll.StatusCode);
        Assert.False(File.Exists(log2Path));
    }
}
