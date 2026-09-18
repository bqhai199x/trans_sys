using System.Text;
using System.Text.Json;
using FileHandler.Api.Common;
using FileHandler.Api.Contracts;
using FileHandler.Api.Controllers;
using FileHandler.Api.Diagnostics;
using FileHandler.Api.Modules.Excel;
using FileHandler.Api.Modules.Markdown;
using FileHandler.Api.Modules.PlainText;
using FileHandler.Api.Modules.PowerPoint;
using FileHandler.Api.Modules.Word;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FileHandler.Tests.Modules.Office;

/// <summary>
/// Tests covering Office trace state, isolation, and FilesController API endpoints (TR01-TR14, API01-API10).
/// </summary>
public sealed class OfficeTraceAndApiTests
{

    /// <summary>
    /// Creates configured FilesController instance for API tests.
    /// </summary>
    /// <param name="fileOptions">File handling limits.</param>
    /// <returns>Configured FilesController instance.</returns>
    private static FilesController CreateController(FileHandlingOptions? fileOptions = null)
    {
        var configured = Options.Create(fileOptions ?? new());
        return new FilesController(
            MarkdownService.Create(configured),
            new PlainTextService(configured),
            WordService.Create(configured),
            ExcelService.Create(configured),
            PowerPointService.Create(configured));
    }

    /// <summary>
    /// Creates FormFile from raw bytes and filename.
    /// </summary>
    /// <param name="name">File name.</param>
    /// <param name="bytes">File payload bytes.</param>
    /// <param name="contentType">MIME content type.</param>
    /// <returns>FormFile instance.</returns>
    private static FormFile MakeFormFile(string name, byte[] bytes, string contentType) =>
        new(new MemoryStream(bytes), 0, bytes.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    /// <summary>
    /// Verifies POST /import for Word document extracts texts through API (API01).
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Import_WordDocument_ReturnsOkWithTexts()
    {
        var controller = CreateController();
        var docx = OfficeFixtureFactory.CreateWordDocument("First paragraph", "Second paragraph");
        var file = MakeFormFile("test.docx", docx, "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        var result = await controller.Import(new ImportRequest { File = file }, default);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var texts = Assert.IsAssignableFrom<IReadOnlyList<string>>(okResult.Value);
        Assert.Equal(2, texts.Count);
        Assert.Equal("First paragraph", texts[0]);
        Assert.Equal("Second paragraph", texts[1]);
    }

    /// <summary>
    /// Verifies POST /export for Word document returns translated docx file (API02).
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Export_WordDocument_ReturnsTranslatedFile()
    {
        var controller = CreateController();
        var docx = OfficeFixtureFactory.CreateWordDocument("First paragraph");
        var file = MakeFormFile("test.docx", docx, "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        var result = await controller.Export(new ExportRequest
        {
            File = file,
            TranslatedTexts = "[\"Doan van thu nhat\"]"
        }, default);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", fileResult.ContentType);
        Assert.Equal("test.docx", fileResult.FileDownloadName);
        Assert.NotNull(fileResult.FileContents);
        Assert.True(fileResult.FileContents.Length > 0);
    }

    /// <summary>
    /// Verifies POST /import for Excel document extracts texts through API (API03).
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Import_ExcelDocument_ReturnsOkWithTexts()
    {
        var controller = CreateController();
        var xlsx = OfficeFixtureFactory.CreateExcelWithInlineStrings(new[] { new[] { "Cell1", "Cell2" } });
        var file = MakeFormFile("test.xlsx", xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var result = await controller.Import(new ImportRequest { File = file }, default);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var texts = Assert.IsAssignableFrom<IReadOnlyList<string>>(okResult.Value);
        Assert.Equal(2, texts.Count);
    }

    /// <summary>
    /// Verifies POST /export for Excel document returns translated xlsx file (API04).
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Export_ExcelDocument_ReturnsTranslatedFile()
    {
        var controller = CreateController();
        var xlsx = OfficeFixtureFactory.CreateExcelWithInlineStrings(new[] { new[] { "Hello" } });
        var file = MakeFormFile("test.xlsx", xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var result = await controller.Export(new ExportRequest
        {
            File = file,
            TranslatedTexts = "[\"Xin chao\"]"
        }, default);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileResult.ContentType);
        Assert.Equal("test.xlsx", fileResult.FileDownloadName);
    }

    /// <summary>
    /// Verifies POST /import for PowerPoint document extracts texts through API (API05).
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Import_PowerPointDocument_ReturnsOkWithTexts()
    {
        var controller = CreateController();
        var pptx = OfficeFixtureFactory.CreatePowerPointPresentation("Slide 1");
        var file = MakeFormFile("test.pptx", pptx, "application/vnd.openxmlformats-officedocument.presentationml.presentation");

        var result = await controller.Import(new ImportRequest { File = file }, default);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var texts = Assert.IsAssignableFrom<IReadOnlyList<string>>(okResult.Value);
        Assert.Single(texts);
        Assert.Equal("Slide 1", texts[0]);
    }

    /// <summary>
    /// Verifies POST /export for PowerPoint document returns translated pptx file (API06).
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Export_PowerPointDocument_ReturnsTranslatedFile()
    {
        var controller = CreateController();
        var pptx = OfficeFixtureFactory.CreatePowerPointPresentation("Slide 1");
        var file = MakeFormFile("test.pptx", pptx, "application/vnd.openxmlformats-officedocument.presentationml.presentation");

        var result = await controller.Export(new ExportRequest
        {
            File = file,
            TranslatedTexts = "[\"Trang 1\"]"
        }, default);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/vnd.openxmlformats-officedocument.presentationml.presentation", fileResult.ContentType);
        Assert.Equal("test.pptx", fileResult.FileDownloadName);
    }

    /// <summary>
    /// Verifies quota limit exceeded returns 413 Payload Too Large (API07).
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Import_FileTooLarge_Returns413PayloadTooLarge()
    {
        var controller = CreateController(new FileHandlingOptions { MaxFileBytes = 50 });
        var docx = OfficeFixtureFactory.CreateWordDocument("Exceeds fifty bytes");
        var file = MakeFormFile("test.docx", docx, "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        var result = await controller.Import(new ImportRequest { File = file }, default);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, objectResult.StatusCode);
    }
}
