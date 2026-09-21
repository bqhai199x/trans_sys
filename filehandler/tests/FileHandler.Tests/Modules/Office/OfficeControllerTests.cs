using System.Text;
using System.Text.Json;
using FileHandler.Api.Common;
using FileHandler.Api.Contracts;
using FileHandler.Api.Controllers;
using FileHandler.Api.Modules.Excel;
using FileHandler.Api.Modules.PowerPoint;
using FileHandler.Api.Modules.Word;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FileHandler.Tests.Modules.Office;

/// <summary>
/// Tests covering Word, Excel, and PowerPoint format controller endpoints.
/// </summary>
public sealed class OfficeControllerTests
{

    /// <summary>
    /// Creates configured WordController instance for API tests.
    /// </summary>
    /// <param name="fileOptions">File handling limits.</param>
    /// <returns>Configured WordController instance.</returns>
    private static WordController CreateWordController(FileHandlingOptions? fileOptions = null)
    {
        var configured = Options.Create(fileOptions ?? new());
        return new WordController(WordService.Create(configured), configured)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    /// <summary>
    /// Creates configured ExcelController instance for API tests.
    /// </summary>
    /// <param name="fileOptions">File handling limits.</param>
    /// <returns>Configured ExcelController instance.</returns>
    private static ExcelController CreateExcelController(FileHandlingOptions? fileOptions = null)
    {
        var configured = Options.Create(fileOptions ?? new());
        return new ExcelController(ExcelService.Create(configured), configured)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    /// <summary>
    /// Creates configured PowerPointController instance for API tests.
    /// </summary>
    /// <param name="fileOptions">File handling limits.</param>
    /// <returns>Configured PowerPointController instance.</returns>
    private static PowerPointController CreatePowerPointController(FileHandlingOptions? fileOptions = null)
    {
        var configured = Options.Create(fileOptions ?? new());
        return new PowerPointController(PowerPointService.Create(configured), configured)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
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
    /// Verifies POST /api/word/import for Word document extracts texts through API.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Import_WordDocument_ReturnsOkWithTexts()
    {
        var controller = CreateWordController();
        var docx = OfficeFixtureFactory.CreateWordDocument("First paragraph", "Second paragraph");
        var file = MakeFormFile("test.docx", docx, "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        var result = await controller.Import(new WordImportRequest { File = file }, default);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsAssignableFrom<WordImportResponse>(okResult.Value);
        Assert.Equal(2, response.Texts.Count);
        Assert.Equal("First paragraph", response.Texts[0]);
        Assert.Equal("Second paragraph", response.Texts[1]);
    }

    /// <summary>
    /// Verifies POST /api/word/export for Word document returns translated docx file.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Export_WordDocument_ReturnsTranslatedFile()
    {
        var controller = CreateWordController();
        var docx = OfficeFixtureFactory.CreateWordDocument("First paragraph");
        var file = MakeFormFile("test.docx", docx, "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        var result = await controller.Export(new WordExportRequest { File = file, Texts = "[\"Doan van thu nhat\"]" }, default);

        var fileResult = Assert.IsType<MultipartFileResult>(result);
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", fileResult.Result.ContentType);
        Assert.Equal("test.docx", fileResult.FileName);
        Assert.NotNull(fileResult.Result.Content!);
        Assert.True(fileResult.Result.Content!.Length > 0);
        Assert.False(controller.Response.Headers.ContainsKey("X-File-Metadata"));
    }

    /// <summary>
    /// Verifies POST /api/excel/import for Excel document extracts texts through API.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Import_ExcelDocument_ReturnsOkWithTexts()
    {
        var controller = CreateExcelController();
        var xlsx = OfficeFixtureFactory.CreateExcelWithInlineStrings(new[] { new[] { "Cell1", "Cell2" } });
        var file = MakeFormFile("test.xlsx", xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var result = await controller.Import(new ExcelImportRequest { File = file }, default);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsAssignableFrom<ExcelImportResponse>(okResult.Value);
        Assert.Equal(new[] { "Sheet1", "Cell1", "Cell2" }, response.Texts);
    }

    /// <summary>
    /// Verifies POST /api/excel/export for Excel document returns translated xlsx file.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Export_ExcelDocument_ReturnsTranslatedFile()
    {
        var controller = CreateExcelController();
        var xlsx = OfficeFixtureFactory.CreateExcelWithInlineStrings(new[] { new[] { "Hello" } });
        var file = MakeFormFile("test.xlsx", xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var result = await controller.Export(new ExcelExportRequest { File = file, Texts = "[\"Sheet1\",\"Xin chao\"]" }, default);

        var fileResult = Assert.IsType<MultipartFileResult>(result);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileResult.Result.ContentType);
        Assert.Equal("test.xlsx", fileResult.FileName);
        Assert.False(controller.Response.Headers.ContainsKey("X-File-Metadata"));
    }

    /// <summary>
    /// Verifies POST /api/powerpoint/import for PowerPoint document extracts texts through API.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Import_PowerPointDocument_ReturnsOkWithTexts()
    {
        var controller = CreatePowerPointController();
        var pptx = OfficeFixtureFactory.CreatePowerPointPresentation("Slide 1");
        var file = MakeFormFile("test.pptx", pptx, "application/vnd.openxmlformats-officedocument.presentationml.presentation");

        var result = await controller.Import(new PowerPointImportRequest { File = file }, default);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsAssignableFrom<PowerPointImportResponse>(okResult.Value);
        Assert.Single(response.Texts);
        Assert.Equal("Slide 1", response.Texts[0]);
    }

    /// <summary>
    /// Verifies POST /api/powerpoint/export for PowerPoint document returns translated pptx file.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Export_PowerPointDocument_ReturnsTranslatedFile()
    {
        var controller = CreatePowerPointController();
        var pptx = OfficeFixtureFactory.CreatePowerPointPresentation("Slide 1");
        var file = MakeFormFile("test.pptx", pptx, "application/vnd.openxmlformats-officedocument.presentationml.presentation");

        var result = await controller.Export(new PowerPointExportRequest { File = file, Texts = "[\"Trang 1\"]" }, default);

        var fileResult = Assert.IsType<MultipartFileResult>(result);
        Assert.Equal("application/vnd.openxmlformats-officedocument.presentationml.presentation", fileResult.Result.ContentType);
        Assert.Equal("test.pptx", fileResult.FileName);
        Assert.False(controller.Response.Headers.ContainsKey("X-File-Metadata"));
    }

    /// <summary>
    /// Verifies quota limit exceeded returns 413 Payload Too Large.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Import_FileTooLarge_Returns413PayloadTooLarge()
    {
        var controller = CreateWordController(new FileHandlingOptions { MaxFileBytes = 50 });
        var docx = OfficeFixtureFactory.CreateWordDocument("Exceeds fifty bytes");
        var file = MakeFormFile("test.docx", docx, "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        var result = await controller.Import(new WordImportRequest { File = file }, default);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, objectResult.StatusCode);
    }
}

