using System.Text;
using FileHandler.Api.Common;
using FileHandler.Api.Contracts;
using FileHandler.Api.Controllers;
using FileHandler.Api.Modules.Markdown;
using FileHandler.Api.Modules.PlainText;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FileHandler.Tests.Api;

/// <summary>
/// Unit tests for Markdown and PlainText format controllers actions and validations.
/// </summary>
public sealed class FilesControllerTests
{

    /// <summary>
    /// Creates MarkdownController with configured processing limits.
    /// </summary>
    /// <param name="options">File processing limits.</param>
    /// <returns>Controller configured for testing.</returns>
    private static MarkdownController CreateMarkdown(FileHandlingOptions? options = null)
    {
        var configured = Options.Create(options ?? new());
        return new MarkdownController(MarkdownService.Create(configured), configured)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    /// <summary>
    /// Creates PlainTextController with configured processing limits.
    /// </summary>
    /// <param name="options">File processing limits.</param>
    /// <returns>Controller configured for testing.</returns>
    private static PlainTextController CreatePlainText(FileHandlingOptions? options = null)
    {
        var configured = Options.Create(options ?? new());
        return new PlainTextController(new PlainTextService(configured), configured)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    /// <summary>
    /// Creates uploaded file for controller tests.
    /// </summary>
    /// <param name="name">Client-supplied file name.</param>
    /// <param name="bytes">Original source bytes.</param>
    /// <returns>Uploaded file backed by in-memory stream.</returns>
    private static FormFile FileUpload(string name = "guide.md", byte[]? bytes = null)
    {
        var payload = bytes ?? Encoding.UTF8.GetBytes("Hello");
        return new FormFile(new MemoryStream(payload), 0, payload.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/markdown"
        };
    }

    /// <summary>
    /// Verifies response status and error code.
    /// </summary>
    /// <param name="result">Controller response to inspect.</param>
    /// <param name="status">Expected HTTP status code.</param>
    /// <param name="code">Machine-readable error code.</param>
    /// <returns>No return value.</returns>
    private static void AssertError(IActionResult result, int status, string code)
    {
        var response = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, response.StatusCode);
        var errors = Assert.IsType<FileResponse>(response.Value).Errors;
        Assert.Equal(code, Assert.Single(errors).Code);
    }

    /// <summary>
    /// Verifies import returns extracted texts.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Import_ReturnsTexts()
    {
        var response = Assert.IsType<OkObjectResult>(await CreateMarkdown().Import(new MarkdownImportRequest { File = FileUpload() }, default));
        var importResponse = Assert.IsAssignableFrom<MarkdownImportResponse>(response.Value);
        Assert.Equal(new[] { "Hello" }, importResponse.Texts);
    }

    /// <summary>
    /// Verifies rejection of missing, unsupported, and invalid source files.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Import_RejectsMissingUnsupportedAndInvalidSource()
    {
        var controller = CreateMarkdown();
        AssertError(await controller.Import(new MarkdownImportRequest { File = null }, default), 400, "missing_file");
        AssertError(await controller.Import(new MarkdownImportRequest { File = FileUpload("file.pdf") }, default), 415, "unsupported_file_type");
        AssertError(await controller.Import(new MarkdownImportRequest { File = FileUpload(bytes: [0xff]) }, default), 422, "invalid_encoding");
        AssertError(await CreateMarkdown(new() { MaxFileBytes = 1 }).Import(new MarkdownImportRequest { File = FileUpload() }, default), 413, "file_too_large");
    }

    /// <summary>
    /// Verifies translated downloads use safe file names.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Export_ReturnsDownloadWithSafeName()
    {
        var controller = CreateMarkdown();
        var result = Assert.IsType<MultipartFileResult>(await controller.Export(new MarkdownExportRequest { File = FileUpload("../../guide.md"), Texts = "[\"Bonjour\"]" }, default));
        Assert.Equal("Bonjour", Encoding.UTF8.GetString(result.Result.Content!));
        Assert.Equal(MarkdownService.ContentType, result.Result.ContentType);
        Assert.Equal("guide.md", result.FileName);
        Assert.False(controller.Response.Headers.ContainsKey("X-File-Metadata"));
    }

    /// <summary>
    /// Verifies export accepts JSON with trailing commas and unescaped newlines inside strings.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Export_AcceptsLenientJsonWithNewlinesAndTrailingCommas()
    {
        var result = Assert.IsType<MultipartFileResult>(await CreateMarkdown().Export(new MarkdownExportRequest { File = FileUpload(), Texts = "[\n  \"Hello\nWorld\",\n]" }, default));
        Assert.Equal("Hello\nWorld", Encoding.UTF8.GetString(result.Result.Content!));
    }

    /// <summary>
    /// Verifies invalid translation JSON is rejected.
    /// </summary>
    /// <param name="json">Translation JSON submitted to export.</param>
    /// <param name="code">Machine-readable error code.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData(null, "missing_texts")]
    [InlineData("[", "invalid_json")]
    [InlineData("{}", "invalid_texts")]
    [InlineData("null", "invalid_texts")]
    [InlineData("[null]", "invalid_texts")]
    [InlineData("[1]", "invalid_texts")]
    [InlineData("[true]", "invalid_texts")]
    public async Task Export_RejectsInvalidJsonContract(string? json, string code) =>
        AssertError(await CreateMarkdown().Export(new MarkdownExportRequest { File = FileUpload(), Texts = json }, default), 400, code);

    /// <summary>
    /// Verifies source file and translation count validation.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Export_ValidatesFileAndTranslationCount()
    {
        AssertError(await CreateMarkdown().Export(new MarkdownExportRequest(), default), 400, "missing_file");
        AssertError(await CreateMarkdown().Export(new MarkdownExportRequest { File = FileUpload("a.pdf"), Texts = "[]" }, default), 415, "unsupported_file_type");
        AssertError(await CreateMarkdown().Export(new MarkdownExportRequest { File = FileUpload(), Texts = "[]" }, default), 422, "translation_count_mismatch");
    }

    /// <summary>
    /// Verifies resource limit failures return HTTP 413.
    /// </summary>
    /// <param name="limit">Resource limit scenario to test.</param>
    /// <param name="code">Machine-readable error code.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData("units", "too_many_units")]
    [InlineData("translation", "translation_too_long")]
    [InlineData("output", "output_too_large")]
    public async Task Export_MapsResourceLimitsTo413(string limit, string code)
    {
        var options = new FileHandlingOptions();
        if (limit == "units") options.MaxUnits = 0;
        if (limit == "translation") options.MaxTranslationChars = 1;
        if (limit == "output") options.MaxOutputBytes = 1;
        var result = Assert.IsType<ObjectResult>(await CreateMarkdown(options).Export(new MarkdownExportRequest { File = FileUpload(), Texts = "[\"Bonjour\"]" }, default));
        Assert.Equal(413, result.StatusCode);
        Assert.Contains(Assert.IsType<FileResponse>(result.Value).Errors, x => x.Code == code);
    }

    /// <summary>
    /// Verifies controller actions propagate cancellation.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Actions_PropagateCancellation()
    {
        var token = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateMarkdown().Import(new MarkdownImportRequest { File = FileUpload() }, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateMarkdown().Export(new MarkdownExportRequest { File = FileUpload(), Texts = "[\"Hi\"]" }, token));
    }

    /// <summary>
    /// Verifies that invalid Unicode surrogate pairs in translation JSON return HTTP 400 instead of HTTP 500.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Export_ReturnsBadRequest_WhenUnicodeSurrogateIsInvalid()
    {
        var result = Assert.IsType<BadRequestObjectResult>(await CreateMarkdown().Export(new MarkdownExportRequest { File = FileUpload(), Texts = "[\"\\uD800\"]" }, default));
        Assert.Equal(400, result.StatusCode);
        Assert.Contains(Assert.IsType<FileResponse>(result.Value).Errors, x => x.Code == "invalid_json");
    }

    /// <summary>
    /// Verifies that JSON translations with block comments and unescaped newlines parse successfully.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Export_AcceptsJsonWithCommentsAndRawNewlines()
    {
        var jsonWithComments = "[/* \"comment\" */ \"Line 1\nLine 2\"]";
        var result = Assert.IsType<MultipartFileResult>(await CreatePlainText().Export(new PlainTextExportRequest { File = FileUpload("guide.txt", Encoding.UTF8.GetBytes("Line 1\nLine 2")), Texts = jsonWithComments }, default));
        Assert.NotNull(result.Result.Content!);
    }
}

