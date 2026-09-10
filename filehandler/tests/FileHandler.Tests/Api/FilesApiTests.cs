using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FileHandler.Tests.Api;

public sealed class FilesApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public FilesApiTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    [Fact]
    public async Task ImportReturnsBareArray()
    {
        using var form = Form("guide.md", "# Hello");
        var response = await _client.PostAsync("/import", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "Hello" }, JsonSerializer.Deserialize<string[]>(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task ExportReturnsAttachment()
    {
        using var form = Form("guide.md", "# Hello", "[\"Xin chào\"]");
        var response = await _client.PostAsync("/export", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("guide.translated.md", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("# Xin chào", await response.Content.ReadAsStringAsync());
    }

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

    [Fact]
    public async Task SwaggerUiAndDocumentAreAvailable()
    {
        var ui = await _client.GetAsync("/swagger/index.html", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
        Assert.Contains("FileHandler API", await ui.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);

        var document = await _client.GetAsync("/swagger/v1/swagger.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        var json = JsonDocument.Parse(await document.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(json.RootElement.GetProperty("paths").TryGetProperty("/import", out _));
        Assert.True(json.RootElement.GetProperty("paths").TryGetProperty("/export", out _));
    }

    private static MultipartFormDataContent Form(string fileName, string source, string? translations = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(source));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
        form.Add(file, "file", fileName);
        if (translations is not null) form.Add(new StringContent(translations), "translatedTexts");
        return form;
    }
}
