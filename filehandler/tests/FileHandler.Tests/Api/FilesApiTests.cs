using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FileHandler.Api.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FileHandler.Tests.Api;

/// <summary>
/// Integration tests verifying files import and export endpoints.
/// </summary>
public sealed class FilesApiTests : IClassFixture<WebApplicationFactory<Program>>
{

    /// <summary>
    /// Verifies actual multipart bytes are bounded without a Content-Length header.
    /// </summary>
    /// <returns>Task completing after response assertions.</returns>
    [Fact]
    public async Task ChunkedMultipart_EnforcesAggregateByteLimit()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<FileHandlingOptions>(options => options.MaxMultipartBytes = 4096)));
        using var client = factory.CreateClient();
        using var form = Form("source.txt", "Hello");
        form.Add(new StringContent(new string('x', 2600)), "extra1");
        form.Add(new StringContent(new string('x', 2600)), "extra2");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/import") { Content = form };
        request.Headers.TransferEncodingChunked = true;
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
    }

    /// <summary>
    /// HTTP client for API tests.
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// Creates HTTP client for API tests.
    /// </summary>
    /// <param name="factory">Application factory for API tests.</param>
    public FilesApiTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    /// <summary>
    /// Verifies that import returns bare JSON text array.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task ImportReturnsBareArray()
    {
        using var form = Form("guide.md", "# Hello");
        var response = await _client.PostAsync("/import", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "Hello" }, JsonSerializer.Deserialize<string[]>(await response.Content.ReadAsStringAsync()));
    }

    /// <summary>
    /// Verifies that export returns translated Markdown attachment.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task ExportReturnsAttachment()
    {
        using var form = Form("guide.md", "# Hello", "[\"Xin chào\"]");
        var response = await _client.PostAsync("/export", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("guide.md", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("# Xin chào", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Verifies array error responses for invalid JSON and file extensions.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task RejectsBadJsonAndExtensionWithArrayErrors()
    {
        using var badJson = Form("guide.md", "Hi", "not json");
        var first = await _client.PostAsync("/export", badJson);
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement.ValueKind);

        using var wrongType = Form("guide.md.exe", "Hi");
        var second = await _client.PostAsync("/import", wrongType);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, second.StatusCode);
    }

    /// <summary>
    /// Verifies that non-multipart request returns HTTP 415 with JSON array error contract.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Import_Returns415_WhenNotMultipart()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/import", content);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal("unsupported_media_type", doc.RootElement[0].GetProperty("code").GetString());
    }

    /// <summary>
    /// Verifies that invalid Unicode surrogate pairs in JSON array return HTTP 400 instead of HTTP 500.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Export_Returns400_WhenUnicodeIsInvalid()
    {
        using var form = Form("guide.md", "Hello", "[\"\\uD800\"]");
        var response = await _client.PostAsync("/export", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal("invalid_json", doc.RootElement[0].GetProperty("code").GetString());
    }

    /// <summary>
    /// Verifies that Swagger UI and API document are available with custom export schema.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task SwaggerUiAndDocumentAreAvailable()
    {
        var ui = await _client.GetAsync("/swagger/index.html", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
        var uiHtml = await ui.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("FileHandler API", uiHtml, StringComparison.Ordinal);
        Assert.Contains("swagger-custom.css", uiHtml, StringComparison.Ordinal);
        Assert.Contains("swagger-custom.js", uiHtml, StringComparison.Ordinal);

        var cssResponse = await _client.GetAsync("/swagger-custom.css", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, cssResponse.StatusCode);

        var jsResponse = await _client.GetAsync("/swagger-custom.js", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, jsResponse.StatusCode);

        var document = await _client.GetAsync("/swagger/v1/swagger.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        var json = JsonDocument.Parse(await document.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(json.RootElement.GetProperty("paths").TryGetProperty("/import", out _));

        var exportProp = json.RootElement
            .GetProperty("paths")
            .GetProperty("/export")
            .GetProperty("post")
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("multipart/form-data")
            .GetProperty("schema")
            .GetProperty("properties")
            .GetProperty("translatedTexts");

        Assert.Equal("textarea", exportProp.GetProperty("format").GetString());
    }

    /// <summary>
    /// Verifies that export accepts formatted multiline JSON in translatedTexts form field.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task ExportAcceptsMultilineJsonTranslations()
    {
        const string multilineJson = "[\n  \"Tiêu đề dịch\",\n  \"Đoạn văn dịch\"\n]";
        using var form = Form("guide.md", "# Heading\n\nBody paragraph", multilineJson);
        var response = await _client.PostAsync("/export", form, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("# Tiêu đề dịch\n\nĐoạn văn dịch", content);
    }

    /// <summary>
    /// Verifies that export accepts uploaded JSON file as translatedTexts.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task ExportAcceptsUploadedJsonFileForTranslations()
    {
        using var form = new MultipartFormDataContent();
        var mdFile = new ByteArrayContent(Encoding.UTF8.GetBytes("# Source\n\nText"));
        mdFile.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
        form.Add(mdFile, "file", "guide.md");

        var jsonFile = new ByteArrayContent(Encoding.UTF8.GetBytes("[\"Nguồn\", \"Văn bản\"]"));
        jsonFile.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(jsonFile, "translatedTexts", "translations.json");

        var response = await _client.PostAsync("/export", form, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("# Nguồn\n\nVăn bản", content);
    }

    /// <summary>
    /// Verifies same syntax-like text selects different handlers by extension.
    /// </summary>
    /// <param name="name">Source file name.</param>
    /// <param name="expected">Expected imported unit.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData("guide.txt", "# Hello")]
    [InlineData("guide.TXT", "# Hello")]
    [InlineData("guide.md", "Hello")]
    public async Task SelectsHandlerByExtension(string name, string expected)
    {
        using var form = Form(name, "# Hello");
        using var response = await _client.PostAsync("/import", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { expected }, JsonSerializer.Deserialize<string[]>(await response.Content.ReadAsStringAsync()));
    }

    /// <summary>
    /// Verifies TXT downloads preserve BOM and separators for pasted and uploaded JSON.
    /// </summary>
    /// <param name="uploadJson">Whether translations are uploaded as a JSON file.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlainTextExportReturnsExactAttachment(bool uploadJson)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Utf8TextReader.Encode("# Hello\r\n\r\nWorld\r\n", true)), "file", "C:\\fake\\guide.TXT");
        var json = JsonSerializer.Serialize(new[] { "# Xin chào\nDòng mới", "<keepme01> **Thế giới**" });
        if (uploadJson)
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(json)), "translatedTexts", "translations.json");
        else
            form.Add(new StringContent(json), "translatedTexts");
        using var response = await _client.PostAsync("/export", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.Equal("guide.TXT", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal(Utf8TextReader.Encode("# Xin chào\nDòng mới\r\n\r\n<keepme01> **Thế giới**\r\n", true), await response.Content.ReadAsByteArrayAsync());
    }

    /// <summary>
    /// Verifies TXT uses existing JSON validation and lenient parsing contracts.
    /// </summary>
    /// <param name="json">Translation form field.</param>
    /// <param name="status">Expected HTTP status.</param>
    /// <param name="expected">Output content or expected error code.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData("[/* note */\"Xin\nchào\",]", 200, "Xin\nchào")]
    [InlineData("[", 400, "invalid_json")]
    [InlineData("[null]", 400, "invalid_translated_texts")]
    [InlineData("[\"\\uD800\"]", 400, "invalid_json")]
    [InlineData("[]", 422, "translation_count_mismatch")]
    [InlineData("[\" \t\"]", 400, "invalid_json")]
    [InlineData("[\"\"]", 422, "empty_translation")]
    public async Task PlainTextUsesExistingJsonAndErrorContracts(string json, int status, string expected)
    {
        using var form = Form("guide.txt", "Source", json);
        using var response = await _client.PostAsync("/export", form);
        Assert.Equal(status, (int)response.StatusCode);
        if (status == 200)
            Assert.Equal(expected, await response.Content.ReadAsStringAsync());
        else
        {
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(expected, body.RootElement[0].GetProperty("code").GetString());
        }
    }

    /// <summary>
    /// Verifies invalid TXT source encoding returns shared error array contract.
    /// </summary>
    /// <param name="path">Import or export route.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData("/import")]
    [InlineData("/export")]
    public async Task PlainTextRejectsInvalidEncoding(string path)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([0xff]), "file", "bad.txt");
        form.Add(new StringContent("[]"), "translatedTexts");
        using var response = await _client.PostAsync(path, form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var errors = JsonSerializer.Deserialize<FileError[]>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("invalid_encoding", Assert.Single(errors!).Code);
    }

    /// <summary>
    /// Verifies resource limits map to HTTP 413 for TXT import and export.
    /// </summary>
    /// <param name="limit">Resource limit to exercise.</param>
    /// <param name="expected">Expected error code.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData("file", "file_too_large")]
    [InlineData("multipart", "file_too_large")]
    [InlineData("units", "too_many_units")]
    [InlineData("translation", "translation_too_long")]
    [InlineData("output", "output_too_large")]
    public async Task PlainTextResourceLimitsReturn413(string limit, string expected)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<FileHandlingOptions>(options =>
            {
                if (limit == "file") options.MaxFileBytes = 1;
                if (limit == "multipart") options.MaxMultipartBytes = 1;
                if (limit == "units") options.MaxUnits = 0;
                if (limit == "translation") options.MaxTranslationChars = 1;
                if (limit == "output") options.MaxOutputBytes = 1;
            })));
        using var client = factory.CreateClient();
        using var form = Form("guide.txt", "Source", "[\"Translated\"]");
        using var response = await client.PostAsync("/export", form);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, body.RootElement[0].GetProperty("code").GetString());
        if (limit is "file" or "multipart" or "units")
        {
            using var import = Form("guide.txt", "Source");
            using var importResponse = await client.PostAsync("/import", import);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, importResponse.StatusCode);
        }
    }

    /// <summary>
    /// Verifies OpenAPI advertises both formats, upload descriptions and error responses.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task OpenApiDescribesPlainTextSupport()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var content = paths.GetProperty("/export").GetProperty("post").GetProperty("responses").GetProperty("200").GetProperty("content");
        Assert.True(content.TryGetProperty("text/plain", out _));
        Assert.True(content.TryGetProperty("text/markdown", out _));
        Assert.False(content.TryGetProperty("application/json", out _));
        Assert.Equal("binary", content.GetProperty("text/plain").GetProperty("schema").GetProperty("format").GetString());
        var errorContent = paths.GetProperty("/export").GetProperty("post").GetProperty("responses").GetProperty("422").GetProperty("content");
        Assert.Equal("application/json", Assert.Single(errorContent.EnumerateObject()).Name);
        foreach (var path in new[] { "/import", "/export" })
        {
            var operation = paths.GetProperty(path).GetProperty("post");
            Assert.True(operation.GetProperty("responses").TryGetProperty("413", out _));
            Assert.True(operation.GetProperty("responses").TryGetProperty("415", out _));
            var description = operation.GetProperty("requestBody").GetProperty("content").GetProperty("multipart/form-data")
                .GetProperty("schema").GetProperty("properties").GetProperty("file").GetProperty("description").GetString();
            Assert.Contains(".txt", description);
            Assert.Contains(".md", description);
        }
    }

    /// <summary>
    /// Builds multipart request with source file and optional translations.
    /// </summary>
    /// <param name="fileName">Uploaded source file name.</param>
    /// <param name="source">Original source text.</param>
    /// <param name="translations">Optional JSON array of translated strings.</param>
    /// <returns>Multipart content containing source file and optional translations.</returns>
    private static MultipartFormDataContent Form(string fileName, string source, string? translations = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(source));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
        form.Add(file, "file", fileName);
        if (translations is not null) form.Add(new StringContent(translations), "translatedTexts");
        return form;
    }

    /// <summary>
    /// Verifies shared Markdown wire tokens survive HTTP JSON and preserve download name.
    /// </summary>
    /// <returns>Task representing completed HTTP assertions.</returns>
    [Fact]
    public async Task MarkdownWireTokensRoundTripThroughHttp()
    {
        const string source = "Before **red** after `code`";
        using var importForm = Form("Guide.MD", source);
        using var importedResponse = await _client.PostAsync("/import", importForm);
        Assert.Equal(HttpStatusCode.OK, importedResponse.StatusCode);
        var texts = JsonSerializer.Deserialize<string[]>(await importedResponse.Content.ReadAsStringAsync())!;
        Assert.Equal("<ox:r0>Before </ox:r0><ox:r1>red</ox:r1><ox:r2> after </ox:r2><ox:k0/>", Assert.Single(texts));
        using var exportForm = Form("Guide.MD", source, JsonSerializer.Serialize(new[] { texts[0].Replace(">red<", ">đỏ<") }));
        using var exported = await _client.PostAsync("/export", exportForm);
        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        Assert.Equal("Guide.MD", exported.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("Before **đỏ** after `code`", await exported.Content.ReadAsStringAsync());
    }
}
