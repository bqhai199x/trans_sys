using System.Text;
using FileHandler.Api.Common;
using FileHandler.Api.Contracts;
using FileHandler.Api.Controllers;
using FileHandler.Api.Modules.Markdown;
using FileHandler.Api.Modules.PlainText;
using FileHandler.Api.Modules.Word;
using FileHandler.Api.Modules.Excel;
using FileHandler.Api.Modules.PowerPoint;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FileHandler.Tests.Api;

/// <summary>
/// Unit tests for files controller actions and validations.
/// </summary>
public sealed class FilesControllerTests
{

    /// <summary>
    /// Creates controller with configured processing limits.
    /// </summary>
    /// <param name="options">File processing limits.</param>
    /// <returns>Controller configured for testing.</returns>
    private static FilesController Create(FileHandlingOptions? options = null)
    {
        var configured = Options.Create(options ?? new());
        return new(
            MarkdownService.Create(configured),
            new PlainTextService(configured),
            WordService.Create(configured),
            ExcelService.Create(configured),
            PowerPointService.Create(configured));
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
        var errors = Assert.IsAssignableFrom<IReadOnlyList<FileError>>(response.Value);
        Assert.Equal(code, Assert.Single(errors).Code);
    }

    /// <summary>
    /// Verifies import returns extracted texts.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Import_ReturnsTexts()
    {
        var response = Assert.IsType<OkObjectResult>(await Create().Import(new() { File = FileUpload() }, default));
        Assert.Equal(new[] { "Hello" }, Assert.IsAssignableFrom<IReadOnlyList<string>>(response.Value));
    }

    /// <summary>
    /// Verifies rejection of missing, unsupported, and invalid source files.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Import_RejectsMissingUnsupportedAndInvalidSource()
    {
        var controller = Create();
        AssertError(await controller.Import(new(), default), 400, "missing_file");
        AssertError(await controller.Import(new() { File = FileUpload("file.pdf") }, default), 415, "unsupported_file_type");
        AssertError(await controller.Import(new() { File = FileUpload(bytes: [0xff]) }, default), 422, "invalid_encoding");
        AssertError(await Create(new() { MaxFileBytes = 1 }).Import(new() { File = FileUpload() }, default), 413, "file_too_large");
    }

    /// <summary>
    /// Verifies translated downloads use safe file names.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Export_ReturnsDownloadWithSafeName()
    {
        var result = Assert.IsType<FileContentResult>(await Create().Export(new()
        {
            File = FileUpload("../../guide.md"),
            TranslatedTexts = "[\"Bonjour\"]"
        }, default));
        Assert.Equal("Bonjour", Encoding.UTF8.GetString(result.FileContents));
        Assert.Equal(MarkdownService.ContentType, result.ContentType);
        Assert.Equal("guide.md", result.FileDownloadName);
    }

    /// <summary>
    /// Verifies export accepts JSON with trailing commas and unescaped newlines inside strings.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Export_AcceptsLenientJsonWithNewlinesAndTrailingCommas()
    {
        var result = Assert.IsType<FileContentResult>(await Create().Export(new()
        {
            File = FileUpload(),
            TranslatedTexts = "[\n  \"Hello\nWorld\",\n]"
        }, default));
        Assert.Equal("Hello\nWorld", Encoding.UTF8.GetString(result.FileContents));
    }

    /// <summary>
    /// Verifies invalid translation JSON is rejected.
    /// </summary>
    /// <param name="json">Translation JSON submitted to export.</param>
    /// <param name="code">Machine-readable error code.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData(null, "missing_translated_texts")]
    [InlineData("[", "invalid_json")]
    [InlineData("{}", "invalid_translated_texts")]
    [InlineData("null", "invalid_translated_texts")]
    [InlineData("[null]", "invalid_translated_texts")]
    [InlineData("[1]", "invalid_translated_texts")]
    [InlineData("[true]", "invalid_translated_texts")]
    public async Task Export_RejectsInvalidJsonContract(string? json, string code) =>
        AssertError(await Create().Export(new() { File = FileUpload(), TranslatedTexts = json }, default), 400, code);

    /// <summary>
    /// Verifies source file and translation count validation.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Export_ValidatesFileAndTranslationCount()
    {
        AssertError(await Create().Export(new(), default), 400, "missing_file");
        AssertError(await Create().Export(new() { File = FileUpload("a.pdf"), TranslatedTexts = "[]" }, default), 415, "unsupported_file_type");
        AssertError(await Create().Export(new() { File = FileUpload(), TranslatedTexts = "[]" }, default), 422, "translation_count_mismatch");
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
        var result = Assert.IsType<ObjectResult>(await Create(options).Export(new()
        {
            File = FileUpload(),
            TranslatedTexts = "[\"Bonjour\"]"
        }, default));
        Assert.Equal(413, result.StatusCode);
        Assert.Contains(Assert.IsAssignableFrom<IReadOnlyList<FileError>>(result.Value), x => x.Code == code);
    }

    /// <summary>
    /// Verifies controller actions propagate cancellation.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Actions_PropagateCancellation()
    {
        var token = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().Import(new() { File = FileUpload() }, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().Export(new() { File = FileUpload(), TranslatedTexts = "[\"Hi\"]" }, token));
    }

    /// <summary>
    /// Verifies that invalid Unicode surrogate pairs in translation JSON return HTTP 400 instead of HTTP 500.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Export_ReturnsBadRequest_WhenUnicodeSurrogateIsInvalid()
    {
        var result = Assert.IsType<BadRequestObjectResult>(await Create().Export(new()
        {
            File = FileUpload(),
            TranslatedTexts = "[\"\\uD800\"]"
        }, default));
        Assert.Equal(400, result.StatusCode);
        Assert.Contains(Assert.IsAssignableFrom<IReadOnlyList<FileError>>(result.Value), x => x.Code == "invalid_json");
    }

    /// <summary>
    /// Verifies that JSON translations with block comments and unescaped newlines parse successfully.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Export_AcceptsJsonWithCommentsAndRawNewlines()
    {
        var jsonWithComments = "[/* \"comment\" */ \"Line 1\nLine 2\"]";
        var result = Assert.IsType<FileContentResult>(await Create().Export(new()
        {
            File = FileUpload("guide.txt", Encoding.UTF8.GetBytes("Line 1\nLine 2")),
            TranslatedTexts = jsonWithComments
        }, default));
        Assert.NotNull(result.FileContents);
    }
}
