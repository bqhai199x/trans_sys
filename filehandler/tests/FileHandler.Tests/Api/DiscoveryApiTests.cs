using System.Net;
using System.Text.Json;
using FileHandler.Tests.Modules.Office;
using FileHandler.Api.Modules.Excel;
using FileHandler.Api.Modules.Office;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FileHandler.Tests.Api;

/// <summary>
/// Verifies discovery guards, native inventory responses and selection form validation.
/// </summary>
public sealed class DiscoveryApiTests : IClassFixture<WebApplicationFactory<Program>>
{

    /// <summary>
    /// Application client used by HTTP assertions.
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// Creates discovery API test client.
    /// </summary>
    /// <param name="factory">Hosted test application.</param>
    public DiscoveryApiTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    /// <summary>
    /// Checks discovery never calls translation extractor, while unexpected extraction errors remain fatal.
    /// </summary>
    /// <returns>Task completing after discovery and exception-envelope assertions.</returns>
    [Fact]
    public async Task Discovery_DoesNotInvokeTranslationExtractor()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.ClearProviders()).ConfigureServices(services =>
            services.AddSingleton<IExcelExtractor, FailingExtractor>()));
        using var client = factory.CreateClient();
        foreach (var operation in new[] { "sheets", "import" })
        {
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(OfficeFixtureFactory.CreateSelectionWorkbook()), "file", "source.xlsx");
            using var response = await client.PostAsync("/api/excel/" + operation, form);
            Assert.Equal(operation == "sheets" ? HttpStatusCode.OK : HttpStatusCode.InternalServerError, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("private failure", body);
            using var json = JsonDocument.Parse(body);
            Assert.Equal(operation == "sheets" ? "success" : "failed", json.RootElement.GetProperty("metadata").GetProperty("status").GetString());
        }
    }

    /// <summary>
    /// Fails when translation extraction is invoked.
    /// </summary>
    private sealed class FailingExtractor : IExcelExtractor
    {

        /// <summary>
        /// Raises unexpected failure for testing exception isolation.
        /// </summary>
        /// <param name="source">Source snapshot.</param>
        /// <param name="inventory">Source inventory.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>No result; always throws.</returns>
        public ExcelPlan Analyze(OfficeSource source, OfficeInventory inventory, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("private failure");
    }

    /// <summary>
    /// Checks discovery includes native inventory without any translation counts or units.
    /// </summary>
    /// <param name="format">Office format route.</param>
    /// <param name="collection">Inventory response collection.</param>
    /// <param name="extension">Source extension.</param>
    /// <returns>Task completing after JSON contract assertions.</returns>
    [Theory]
    [InlineData("excel", "sheets", "xlsx")]
    [InlineData("powerpoint", "slides", "pptx")]
    public async Task Discovery_ReturnsInventoryWithoutMapping(string format, string collection, string extension)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(format == "excel" ? OfficeFixtureFactory.CreateSelectionWorkbook() : OfficeFixtureFactory.CreateSelectionPresentation()), "file", "source." + extension);
        using var response = await _client.PostAsync($"/api/{format}/{collection}", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var metadata = json.RootElement.GetProperty("metadata");
        Assert.Equal(format, metadata.GetProperty("format").GetString());
        Assert.False(metadata.TryGetProperty("units", out _));
        Assert.False(metadata.TryGetProperty("unitCount", out _));
        Assert.False(metadata.TryGetProperty(collection, out _));
        Assert.Equal(format == "excel" ? 5 : 2, json.RootElement.GetProperty(collection).GetArrayLength());
        Assert.Empty(json.RootElement.GetProperty("errors").EnumerateArray());
    }

    /// <summary>
    /// Checks invalid content types and missing files use discovery error envelopes.
    /// </summary>
    /// <param name="path">Discovery route.</param>
    /// <returns>Task completing after guarded failure assertions.</returns>
    [Theory]
    [InlineData("/api/excel/sheets")]
    [InlineData("/api/powerpoint/slides")]
    public async Task Discovery_ErrorsOmitMappingAndFile(string path)
    {
        using var request = new StringContent("{}");
        using var response = await _client.PostAsync(path, request);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("failed", json.RootElement.GetProperty("metadata").GetProperty("status").GetString());
        Assert.False(json.RootElement.GetProperty("metadata").TryGetProperty("unitCount", out _));
        Assert.Equal("unsupported_media_type", json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        using var empty = new MultipartFormDataContent();
        empty.Add(new StringContent(""), "unused");
        using var missing = await _client.PostAsync(path, empty);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using var missingJson = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
        Assert.Equal("missing_file", missingJson.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    /// <summary>
    /// Checks selection preserves distinction between omitted, blank and explicit empty arrays.
    /// </summary>
    /// <param name="selection">Form selection JSON.</param>
    /// <param name="status">Expected HTTP status.</param>
    /// <param name="count">Expected mapping size for successful imports.</param>
    /// <returns>Task completing after selection contract assertions.</returns>
    [Theory]
    [InlineData(null, 200, 3)]
    [InlineData(" ", 200, 3)]
    [InlineData("[]", 200, 0)]
    [InlineData("[\"42\",\"7\",\"42\"]", 200, 4)]
    [InlineData("[\"404\"]", 422, 0)]
    [InlineData("[null]", 400, 0)]
    [InlineData("[7]", 400, 0)]
    [InlineData("{}", 400, 0)]
    [InlineData("[", 400, 0)]
    public async Task ExcelSelection_ValidatesRequestShape(string? selection, int status, int count)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(OfficeFixtureFactory.CreateSelectionWorkbook()), "file", "source.xlsx");
        if (selection is not null) form.Add(new StringContent(selection), "sheetIds");
        using var response = await _client.PostAsync("/api/excel/import", form);
        Assert.Equal(status, (int)response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (status == 200)
        {
            Assert.Equal(count, json.RootElement.GetProperty("texts").GetArrayLength());
            Assert.Equal(count, json.RootElement.GetProperty("metadata").GetProperty("unitCount").GetInt32());
        }
        else Assert.Equal(status == 400 ? "invalid_selection" : "unknown_selection_id", json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
    }
}
