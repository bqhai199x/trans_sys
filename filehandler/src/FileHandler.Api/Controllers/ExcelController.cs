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
    /// <returns>Import JSON, optional units.json multipart attachment, or fatal JSON envelope.</returns>
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ExcelImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Import([FromForm] ExcelImportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new FileResponse(FileMetadata.Create("excel").ForExport(true), [new("missing_file", ProcessingMessages.MissingFile)]));

        if (!request.File.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new FileResponse(FileMetadata.Create("excel").ForExport(true), [new("unsupported_file_type", ProcessingMessages.UnsupportedExtension(".xlsx"))]));

        var selectionError = SelectionInput.Parse(request.SheetIds, out var ids);
        if (selectionError is not null)
            return BadRequest(new FileResponse(FileMetadata.Create("excel").ForExport(true), [selectionError]));
        await using var stream = request.File.OpenReadStream();
        var result = await _excel.ImportAsync(stream, new ExcelSelection(ids), request.Debug, cancellationToken);
        if (result.Errors.Count > 0)
            return result.Errors.ToActionResult(result.Metadata);

        var response = new ExcelImportResponse(result.Texts, result.Metadata with { Units = null }, []);
        return request.Debug ? new MultipartUnitsResult(response, result.Metadata.Units ?? []) : Ok(response);
    }

    /// <summary>
    /// Exports uploaded Excel spreadsheet with supplied translations and processing metadata.
    /// </summary>
    /// <param name="request">Multipart form request containing Excel file and translations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Translated Excel spreadsheet file content or error response.</returns>
    [HttpPost("export")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status200OK, "multipart/mixed")]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Export([FromForm] ExcelExportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new FileResponse(FileMetadata.Create("excel").ForExport(true), [new("missing_file", ProcessingMessages.MissingFile)]));

        if (!request.File.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new FileResponse(FileMetadata.Create("excel").ForExport(true), [new("unsupported_file_type", ProcessingMessages.UnsupportedExtension(".xlsx"))]));

        var (success, translations, parseError) = await TranslationInputParser.TryParseAsync(
            Request?.HasFormContentType == true ? Request.Form.Files : null,
            request.Texts,
            _options,
            cancellationToken);

        if (!success)
            return parseError!.Code == "too_many_units" ? new[] { parseError! }.ToActionResult(FileMetadata.Create("excel")) : BadRequest(new FileResponse(FileMetadata.Create("excel").ForExport(true), [parseError!]));

        var selectionError = SelectionInput.Parse(request.SheetIds, out var ids);
        if (selectionError is not null)
            return BadRequest(new FileResponse(FileMetadata.Create("excel").ForExport(true), [selectionError]));
        await using var stream = request.File.OpenReadStream();
        var result = await _excel.ExportAsync(stream, translations!, new ExcelSelection(ids), cancellationToken);
        if (result.Errors.Count > 0)
            return result.Errors.ToActionResult(result.Metadata);

        return new MultipartFileResult(result, FileTypeDetector.GetTranslatedFileName(request.File.FileName, FileType.Excel));
    }

    /// <summary>
    /// Lists source sheets without translation extraction.
    /// </summary>
    /// <param name="request">Uploaded source file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Native inventory and discovery metadata.</returns>
    [HttpPost("sheets")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(SheetsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DiscoveryFailureResponse), 400)]
    [ProducesResponseType(typeof(DiscoveryFailureResponse), 413)]
    [ProducesResponseType(typeof(DiscoveryFailureResponse), 415)]
    [ProducesResponseType(typeof(DiscoveryFailureResponse), 422)]
    [ProducesResponseType(typeof(DiscoveryFailureResponse), 429)]
    [ProducesResponseType(typeof(DiscoveryFailureResponse), 500)]
    public async Task<IActionResult> Sheets([FromForm] DiscoveryRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new DiscoveryFailureResponse(new("excel", ProcessingStatus.Failed, []), [new("missing_file", ProcessingMessages.MissingFile)]));
        if (!request.File.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return StatusCode(415, new DiscoveryFailureResponse(new("excel", ProcessingStatus.Failed, []), [new("unsupported_file_type", ProcessingMessages.UnsupportedExtension(".xlsx"))]));
        await using var stream = request.File.OpenReadStream();
        var result = await _excel.GetSheetsAsync(stream, cancellationToken);
        if (result.Errors.Count > 0)
        {
            var mapped = (ObjectResult)result.Errors.ToActionResult();
            return StatusCode(mapped.StatusCode!.Value, new DiscoveryFailureResponse(result.Metadata, result.Errors));
        }
        return Ok(result);
    }
}
