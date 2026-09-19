using System.Text.Json;
using FileHandler.Api.Common;
using FileHandler.Api.Contracts;
using FileHandler.Api.Modules.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FileHandler.Api.Controllers;

/// <summary>
/// REST API controller handling Excel spreadsheet imports and exports.
/// </summary>
[ApiController]
[Route("api/excel")]
[EnableRateLimiting("file-processing")]
public sealed class ExcelController : ControllerBase
{

    /// <summary>
    /// Excel spreadsheet import and export service.
    /// </summary>
    private readonly ExcelService _excel;

    /// <summary>
    /// Request handling limits.
    /// </summary>
    private readonly FileHandlingOptions _options;

    /// <summary>
    /// Creates controller with Excel service and configured limits.
    /// </summary>
    /// <param name="excel">Excel spreadsheet processing service.</param>
    /// <param name="options">Configured limits options.</param>
    public ExcelController(ExcelService excel, IOptions<FileHandlingOptions>? options = null)
    {
        _excel = excel;
        _options = options?.Value ?? new FileHandlingOptions();
    }

    /// <summary>
    /// Imports uploaded Excel spreadsheet as text array with metadata.
    /// </summary>
    /// <param name="request">Multipart form request containing Excel file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted texts and Excel metadata, or error response.</returns>
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ExcelImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Import([FromForm] ExcelImportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new[] { new FileError("missing_file", "Field file là bắt buộc.") });

        if (!request.File.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new[] { new FileError("unsupported_file_type", "Chỉ hỗ trợ tệp .xlsx.") });

        await using var stream = request.File.OpenReadStream();
        var result = await _excel.ImportAsync(stream, cancellationToken);
        if (result.Errors.Count > 0)
            return result.Errors.ToActionResult();

        var metadata = new ExcelMetadata { UnitCount = result.Texts.Count };
        return Ok(new ExcelImportResponse(result.Texts, metadata, []));
    }

    /// <summary>
    /// Exports uploaded Excel spreadsheet with supplied translations and metadata header.
    /// </summary>
    /// <param name="request">Multipart form request containing Excel file and translations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Translated Excel spreadsheet file content or error response.</returns>
    [HttpPost("export")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, ExcelService.ContentType)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Export([FromForm] ExcelExportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new[] { new FileError("missing_file", "Field file là bắt buộc.") });

        if (!request.File.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new[] { new FileError("unsupported_file_type", "Chỉ hỗ trợ tệp .xlsx.") });

        var (success, translations, parseError) = await TranslationInputParser.TryParseAsync(
            Request?.HasFormContentType == true ? Request.Form.Files : null,
            request.Texts,
            _options,
            cancellationToken);

        if (!success)
            return parseError!.Code == "too_many_units" ? new[] { parseError! }.ToActionResult() : BadRequest(new[] { parseError! });

        await using var stream = request.File.OpenReadStream();
        var result = await _excel.ExportAsync(stream, translations!, cancellationToken);
        if (result.Errors.Count > 0)
            return result.Errors.ToActionResult();

        var metadata = new ExcelMetadata { UnitCount = translations!.Count };
        Response.Headers["X-File-Metadata"] = JsonSerializer.Serialize(metadata);

        return File(result.Content!, result.ContentType, FileTypeDetector.GetTranslatedFileName(request.File.FileName, FileType.Excel));
    }
}
