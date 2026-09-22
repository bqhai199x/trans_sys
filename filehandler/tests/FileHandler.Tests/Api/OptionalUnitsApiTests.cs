using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FileHandler.Tests.Modules.Office;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace FileHandler.Tests.Api;

/// <summary>
/// Verifies opt-in mapping attachments while ordinary metadata and skips remain available.
/// </summary>
public sealed class OptionalUnitsApiTests : IClassFixture<WebApplicationFactory<Program>>
{

    /// <summary>
    /// Hosted API client for request and wire-format assertions.
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// Creates API client with shared hosted application.
    /// </summary>
    /// <param name="factory">Application factory.</param>
    public OptionalUnitsApiTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    /// <summary>
    /// Checks debug adds mapping and informational skips while warnings and ordinary metadata remain identical.
    /// </summary>
    /// <param name="format">Endpoint format.</param>
    /// <param name="extension">Source extension.</param>
    /// <returns>Task completing after independent multipart and JSON assertions.</returns>
    [Theory]
    [InlineData("markdown", "md")]
    [InlineData("plaintext", "txt")]
    [InlineData("word", "docx")]
    [InlineData("excel", "xlsx")]
    [InlineData("powerpoint", "pptx")]
    public async Task Import_UnitsAreOptInJsonAttachment(string format, string extension)
    {
        var bytes = Source(format);
        using var defaultForm = Form(bytes, extension, null);
        using var defaultResponse = await _client.PostAsync($"/api/{format}/import", defaultForm);
        Assert.Equal(HttpStatusCode.OK, defaultResponse.StatusCode);
        Assert.Equal("application/json", defaultResponse.Content.Headers.ContentType!.MediaType);
        using var baseline = JsonDocument.Parse(await defaultResponse.Content.ReadAsStringAsync());
        Assert.False(baseline.RootElement.GetProperty("metadata").TryGetProperty("units", out _));
        var totals = baseline.RootElement.GetProperty("metadata").GetProperty("skipCount");
        Assert.Equal(format == "excel" ? 1 : 0, totals.GetProperty("warning").GetInt32());
        Assert.Equal(format == "excel" ? 2 : format == "plaintext" ? 0 : 1, totals.GetProperty("info").GetInt32());
        using var falseForm = Form(bytes, extension, "false");
        using var falseResponse = await _client.PostAsync($"/api/{format}/import", falseForm);
        Assert.Equal(await defaultResponse.Content.ReadAsStringAsync(), await falseResponse.Content.ReadAsStringAsync());

        using var trueForm = Form(bytes, extension, "true");
        using var response = await _client.PostAsync($"/api/{format}/import", trueForm);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("multipart/mixed", response.Content.Headers.ContentType!.MediaType);
        var boundary = response.Content.Headers.ContentType.Parameters.Single(p => p.Name == "boundary").Value!.Trim('"');
        await using var body = await response.Content.ReadAsStreamAsync();
        var reader = new MultipartReader(boundary, body);
        var envelopePart = await reader.ReadNextSectionAsync();
        Assert.Equal("<metadata>", envelopePart!.Headers!["Content-ID"].ToString());
        Assert.Equal("application/json; charset=utf-8", envelopePart.ContentType);
        using var envelope = await JsonDocument.ParseAsync(envelopePart.Body);
        var ordinaryEnvelope = JsonNode.Parse(envelope.RootElement.GetRawText())!;
        var diagnosticSkips = ordinaryEnvelope["metadata"]!["skipped"]!.AsArray();
        var infoIndexes = diagnosticSkips.Select((skip, index) => (skip, index)).Where(item => item.skip!["severity"]!.GetValue<string>() == "info").Select(item => item.index).ToArray();
        if (format is "excel" or "powerpoint" or "markdown" or "word") Assert.NotEmpty(infoIndexes);
        foreach (var index in infoIndexes.Reverse()) diagnosticSkips.RemoveAt(index);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(baseline.RootElement.GetRawText()), ordinaryEnvelope));
        Assert.DoesNotContain(baseline.RootElement.GetProperty("metadata").GetProperty("skipped").EnumerateArray(), skip => skip.GetProperty("severity").GetString() == "info");
        var file = await reader.ReadNextSectionAsync();
        Assert.Equal("<units>", file!.Headers!["Content-ID"].ToString());
        Assert.Equal("application/json; charset=utf-8", file.ContentType);
        Assert.Equal("units.json", ContentDispositionHeaderValue.Parse(file.ContentDisposition!).FileNameStar.Value);
        using var mapping = await JsonDocument.ParseAsync(file.Body);
        var units = mapping.RootElement.GetProperty("units");
        Assert.Equal("units", Assert.Single(mapping.RootElement.EnumerateObject()).Name);
        Assert.Equal(envelope.RootElement.GetProperty("texts").GetArrayLength(), units.GetArrayLength());
        Assert.Equal(units.GetArrayLength(), envelope.RootElement.GetProperty("metadata").GetProperty("unitCount").GetInt32());
        Assert.Equal(Enumerable.Range(0, units.GetArrayLength()), units.EnumerateArray().Select(u => u.GetProperty("index").GetInt32()));
        Assert.All(units.EnumerateArray(), u => Assert.Equal(JsonValueKind.Object, u.GetProperty("location").ValueKind));
        Assert.Equal(format == "excel" ? "sheetName" : "paragraph", units[0].GetProperty("kind").GetString());
        if (format is "excel" or "powerpoint") Assert.NotEmpty(envelope.RootElement.GetProperty("metadata").GetProperty("skipped").EnumerateArray());
        Assert.Null(await reader.ReadNextSectionAsync());
    }

    /// <summary>
    /// Checks invalid options and failed imports never publish a mapping attachment.
    /// </summary>
    /// <param name="option">Boolean form value.</param>
    /// <param name="status">Expected failure status.</param>
    /// <returns>Task completing after failure envelope assertions.</returns>
    [Theory]
    [InlineData("invalid", HttpStatusCode.BadRequest)]
    [InlineData("true", HttpStatusCode.UnprocessableEntity)]
    public async Task Import_FailuresRemainJson(string option, HttpStatusCode status)
    {
        using var form = Form([0xff], "txt", option);
        using var response = await _client.PostAsync("/api/plaintext/import", form);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("failed", json.RootElement.GetProperty("metadata").GetProperty("status").GetString());
        Assert.False(json.RootElement.GetProperty("metadata").TryGetProperty("units", out _));
        Assert.NotEmpty(json.RootElement.GetProperty("errors").EnumerateArray());
    }

    /// <summary>
    /// Checks fatal errors after extraction retain counts and skips but never inline unit details.
    /// </summary>
    /// <returns>Task completing after located source failure assertions.</returns>
    [Fact]
    public async Task Import_PostExtractionFailureOmitsUnits()
    {
        var bytes = OfficeFixtureFactory.CreateWordDocumentWithElements(
            OfficeFixtureFactory.WordParagraph("Visible"),
            new DocumentFormat.OpenXml.Wordprocessing.Paragraph(new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Ruby())),
            new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text("Invalid body child")));
        using var form = Form(bytes, "docx", "true");
        using var response = await _client.PostAsync("/api/word/import", form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var metadata = json.RootElement.GetProperty("metadata");
        Assert.Equal(1, metadata.GetProperty("unitCount").GetInt32());
        Assert.False(metadata.TryGetProperty("units", out _));
        Assert.NotEmpty(metadata.GetProperty("skipped").EnumerateArray());
    }

    /// <summary>
    /// Checks an explicitly empty selection returns empty requested mapping and complete selection skips.
    /// </summary>
    /// <returns>Task completing after empty mapping and skip assertions.</returns>
    [Fact]
    public async Task Import_EmptySelectionStillReturnsRequestedJsonFile()
    {
        using var form = Form(Source("excel"), "xlsx", "true");
        form.Add(new StringContent("[]"), "sheetIds");
        using var response = await _client.PostAsync("/api/excel/import", form);
        response.EnsureSuccessStatusCode();
        var boundary = response.Content.Headers.ContentType!.Parameters.Single(p => p.Name == "boundary").Value!.Trim('"');
        await using var stream = await response.Content.ReadAsStreamAsync();
        var reader = new MultipartReader(boundary, stream);
        using var metadata = await JsonDocument.ParseAsync((await reader.ReadNextSectionAsync())!.Body);
        Assert.Equal(0, metadata.RootElement.GetProperty("metadata").GetProperty("unitCount").GetInt32());
        Assert.Equal(5, metadata.RootElement.GetProperty("metadata").GetProperty("skipped").GetArrayLength());
        Assert.Equal(5, metadata.RootElement.GetProperty("metadata").GetProperty("skipCount").GetProperty("info").GetInt32());
        Assert.Equal(0, metadata.RootElement.GetProperty("metadata").GetProperty("skipCount").GetProperty("warning").GetInt32());
        using var units = await JsonDocument.ParseAsync((await reader.ReadNextSectionAsync())!.Body);
        Assert.Empty(units.RootElement.GetProperty("units").EnumerateArray());
        Assert.Null(await reader.ReadNextSectionAsync());
    }

    /// <summary>
    /// Checks exports hide informational skips while preserving warnings and exact fallback bytes.
    /// </summary>
    /// <param name="format">Endpoint format.</param>
    /// <param name="extension">Source extension.</param>
    /// <returns>Task completing after multipart warning and preservation assertions.</returns>
    [Theory]
    [InlineData("markdown", "md")]
    [InlineData("plaintext", "txt")]
    [InlineData("word", "docx")]
    [InlineData("excel", "xlsx")]
    [InlineData("powerpoint", "pptx")]
    public async Task Export_ReturnsWarningsWithoutInfo(string format, string extension)
    {
        var bytes = Source(format);
        using var importForm = Form(bytes, extension, null);
        using var imported = await _client.PostAsync($"/api/{format}/import", importForm);
        imported.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await imported.Content.ReadAsStringAsync());
        var texts = json.RootElement.GetProperty("texts").EnumerateArray().Select(text => text.GetString()!).ToArray();
        var index = format == "excel" ? 1 : 0;
        texts[index] = "";
        using var exportForm = Form(bytes, extension, null);
        exportForm.Add(new StringContent(JsonSerializer.Serialize(texts)), "texts");
        using var exported = await _client.PostAsync($"/api/{format}/export", exportForm);
        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        var output = await MultipartResponse.ReadAsync(exported);
        Assert.Equal(bytes, output.Bytes);
        Assert.Equal("partial", output.Metadata.GetProperty("status").GetString());
        Assert.Equal(format == "excel" ? 2 : 1, output.Metadata.GetProperty("skipCount").GetProperty("warning").GetInt32());
        Assert.Equal(format == "excel" ? 2 : format == "plaintext" ? 0 : 1, output.Metadata.GetProperty("skipCount").GetProperty("info").GetInt32());
        var skips = output.Metadata.GetProperty("skipped").EnumerateArray().ToArray();
        Assert.All(skips, skip => Assert.Equal("warning", skip.GetProperty("severity").GetString()));
        var warning = Assert.Single(skips, skip => skip.GetProperty("code").GetString() == "empty_translation");
        Assert.Equal(index, warning.GetProperty("unitIndex").GetInt32());
        Assert.Equal("Empty translation; source retained.", warning.GetProperty("message").GetString());
    }

    /// <summary>
    /// Checks failed imports respect debug visibility without removing warnings or known source facts.
    /// </summary>
    /// <param name="debug">Whether informational skips are requested.</param>
    /// <returns>Task completing after failed JSON metadata assertions.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Import_FatalResultRespectsInfoVisibility(bool debug)
    {
        var bytes = OfficeFixtureFactory.CreateWordDocumentWithElements(
            OfficeFixtureFactory.WordParagraph("Visible"),
            new DocumentFormat.OpenXml.Wordprocessing.Paragraph(new DocumentFormat.OpenXml.Wordprocessing.SimpleField(
                new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text("Date"))) { Instruction = "DATE" }),
            new DocumentFormat.OpenXml.Wordprocessing.Paragraph(new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Ruby())),
            new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text("Invalid body child")));
        using var form = Form(bytes, "docx", debug ? "true" : "false");
        using var response = await _client.PostAsync("/api/word/import", form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var metadata = json.RootElement.GetProperty("metadata");
        Assert.Equal("failed", metadata.GetProperty("status").GetString());
        Assert.Equal(1, metadata.GetProperty("skipCount").GetProperty("info").GetInt32());
        Assert.False(metadata.TryGetProperty("units", out _));
        var skips = metadata.GetProperty("skipped").EnumerateArray().ToArray();
        Assert.Equal(debug, skips.Any(skip => skip.GetProperty("severity").GetString() == "info"));
        Assert.Contains(skips, skip => skip.GetProperty("code").GetString() == "unsupported_ruby" && skip.GetProperty("severity").GetString() == "warning");
        Assert.NotEmpty(json.RootElement.GetProperty("errors").EnumerateArray());
    }

    /// <summary>
    /// Checks OpenAPI describes actual JSON and MIME choices with Vietnamese guidance.
    /// </summary>
    /// <returns>Task completing after schema and example assertions.</returns>
    [Fact]
    public async Task Swagger_DescribesOptionalUnitsAndMultipartInVietnamese()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = json.RootElement.GetProperty("paths");
        Assert.False(json.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("FileMetadata").GetProperty("properties").TryGetProperty("units", out _));
        Assert.True(json.RootElement.GetProperty("components").GetProperty("schemas").TryGetProperty("UnitsFileResponse", out _));
        foreach (var format in new[] { "markdown", "plaintext", "word", "excel", "powerpoint" })
        {
            var import = paths.GetProperty($"/api/{format}/import").GetProperty("post");
            Assert.Contains("Mặc định trả JSON", import.GetProperty("description").GetString());
            Assert.Equal("Invalid multipart request.", import.GetProperty("responses").GetProperty("400")
                .GetProperty("content").GetProperty("application/json").GetProperty("example")
                .GetProperty("errors")[0].GetProperty("message").GetString());
            var input = import.GetProperty("requestBody").GetProperty("content").GetProperty("multipart/form-data").GetProperty("schema");
            Assert.False(input.GetProperty("properties").GetProperty("debug").GetProperty("default").GetBoolean());
            Assert.Equal("boolean", input.GetProperty("properties").GetProperty("debug").GetProperty("type").GetString());
            Assert.False(input.GetProperty("properties").TryGetProperty("includeUnits", out _));
            Assert.Contains("\n", import.GetProperty("description").GetString());
            if (format is "excel" or "powerpoint")
            {
                var selection = input.GetProperty("properties").GetProperty(format == "excel" ? "sheetIds" : "slideIds");
                Assert.Equal("", selection.GetProperty("default").GetString());
                Assert.Equal("", selection.GetProperty("example").GetString());
            }
            var content = import.GetProperty("responses").GetProperty("200").GetProperty("content");
            var example = content.GetProperty("application/json").GetProperty("example");
            Assert.False(example.GetProperty("metadata").TryGetProperty("units", out _));
            Assert.Equal(example.GetProperty("texts").GetArrayLength(), example.GetProperty("metadata").GetProperty("unitCount").GetInt32());
            Assert.Equal(0, example.GetProperty("metadata").GetProperty("skipCount").GetProperty("info").GetInt32());
            var wire = content.GetProperty("multipart/mixed").GetProperty("example").GetString()!;
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(wire));
            var reader = new MultipartReader("example-boundary", stream);
            var metadataPart = await reader.ReadNextSectionAsync();
            using var metadata = await JsonDocument.ParseAsync(metadataPart!.Body);
            Assert.Equal(example.GetProperty("metadata").GetProperty("format").GetString(), metadata.RootElement.GetProperty("metadata").GetProperty("format").GetString());
            var unitsPart = await reader.ReadNextSectionAsync();
            Assert.Equal("<units>", unitsPart!.Headers!["Content-ID"].ToString());
            using var units = await JsonDocument.ParseAsync(unitsPart.Body);
            Assert.Equal(2, units.RootElement.GetProperty("units").GetArrayLength());
            Assert.Null(await reader.ReadNextSectionAsync());
            var export = paths.GetProperty($"/api/{format}/export").GetProperty("post");
            Assert.False(export.GetProperty("requestBody").GetProperty("content").GetProperty("multipart/form-data").GetProperty("schema").GetProperty("properties").TryGetProperty("debug", out _));
            Assert.Equal("multipart/mixed", Assert.Single(export.GetProperty("responses").GetProperty("200").GetProperty("content").EnumerateObject()).Name);
            Assert.Contains("Nguồn không hợp lệ", export.GetProperty("responses").GetProperty("422").GetProperty("description").GetString());
        }
        var ui = await _client.GetStringAsync("/swagger/index.html");
        Assert.True(ui.IndexOf("swagger-multipart.js", StringComparison.Ordinal) < ui.IndexOf("swagger-custom.js", StringComparison.Ordinal));
        Assert.True(ui.IndexOf("swagger-response.js", StringComparison.Ordinal) < ui.IndexOf("swagger-custom.js", StringComparison.Ordinal));
    }

    /// <summary>
    /// Checks discovery always includes zero skip totals without running extraction.
    /// </summary>
    /// <param name="format">Discovery format.</param>
    /// <param name="extension">Source extension.</param>
    /// <param name="action">Discovery route suffix.</param>
    /// <returns>Task completing after discovery count assertions.</returns>
    [Theory]
    [InlineData("excel", "xlsx", "sheets")]
    [InlineData("powerpoint", "pptx", "slides")]
    public async Task Discovery_ReturnsSkipCount(string format, string extension, string action)
    {
        using var form = Form(Source(format), extension, null);
        using var response = await _client.PostAsync($"/api/{format}/{action}", form);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var count = json.RootElement.GetProperty("metadata").GetProperty("skipCount");
        Assert.Equal(0, count.GetProperty("warning").GetInt32());
        Assert.Equal(0, count.GetProperty("info").GetInt32());
    }

    /// <summary>
    /// Creates deterministic fixtures with selection skips in Office formats.
    /// </summary>
    /// <param name="format">Endpoint format.</param>
    /// <returns>Valid source bytes.</returns>
    private static byte[] Source(string format) => format switch
    {
        "excel" => OfficeFixtureFactory.CreateSelectionWorkbook(),
        "powerpoint" => OfficeFixtureFactory.CreateSelectionPresentation(),
        "word" => OfficeFixtureFactory.CreateWordDocumentWithElements(
            OfficeFixtureFactory.WordParagraph("One"),
            new DocumentFormat.OpenXml.Wordprocessing.Paragraph(new DocumentFormat.OpenXml.Wordprocessing.SimpleField(
                new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text("Date"))) { Instruction = "DATE" }),
            OfficeFixtureFactory.WordParagraph("Two")),
        "markdown" => Encoding.UTF8.GetBytes("One\n\n```text\nProtected\n```\n\nTwo"),
        _ => Encoding.UTF8.GetBytes("One\n\nTwo")
    };

    /// <summary>
    /// Builds multipart request with optional mapping flag.
    /// </summary>
    /// <param name="bytes">Source bytes.</param>
    /// <param name="extension">Source extension without period.</param>
    /// <param name="debug">Raw option, or null when omitted.</param>
    /// <returns>Disposable form request.</returns>
    private static MultipartFormDataContent Form(byte[] bytes, string extension, string? debug)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", "source." + extension);
        if (debug is not null) form.Add(new StringContent(debug), "debug");
        return form;
    }
}
