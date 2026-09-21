using FileHandler.Api.Common;
using FileHandler.Api.Contracts;
using FileHandler.Api.Modules.PowerPoint;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FileHandler.Api.Controllers;

/// <summary>
/// REST API controller handling PowerPoint presentation imports and exports.
/// </summary>
[ApiController]
[Route("api/powerpoint")]
[EnableRateLimiting("file-processing")]
public sealed class PowerPointController : ControllerBase
{

    /// <summary>
    /// PowerPoint presentation import and export service.
    /// </summary>
    private readonly PowerPointService _powerPoint;

    /// <summary>
    /// Request handling limits.
    /// </summary>
    private readonly FileHandlingOptions _options;

    /// <summary>
    /// Creates controller with PowerPoint service and configured limits.
    /// </summary>
    /// <param name="powerPoint">PowerPoint presentation processing service.</param>
    /// <param name="options">Configured limits options.</param>
    public PowerPointController(PowerPointService powerPoint, IOptions<FileHandlingOptions>? options = null)
    {
        _powerPoint = powerPoint;
        _options = options?.Value ?? new FileHandlingOptions();
    }

    /// <summary>
    /// Imports uploaded PowerPoint presentation as text array with metadata.
    /// </summary>
    /// <param name="request">Multipart form request containing PowerPoint file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Import JSON, optional units.json multipart attachment, or fatal JSON envelope.</returns>
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(PowerPointImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Import([FromForm] PowerPointImportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new FileResponse(FileMetadata.Create("powerpoint").ForExport(true), [new("missing_file", ProcessingMessages.MissingFile)]));

        if (!request.File.FileName.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new FileResponse(FileMetadata.Create("powerpoint").ForExport(true), [new("unsupported_file_type", ProcessingMessages.UnsupportedExtension(".pptx"))]));

        var selectionError = SelectionInput.Parse(request.SlideIds, out var ids);
        if (selectionError is not null)
            return BadRequest(new FileResponse(FileMetadata.Create("powerpoint").ForExport(true), [selectionError]));
        await using var stream = request.File.OpenReadStream();
        var result = await _powerPoint.ImportAsync(stream, new PowerPointSelection(ids), request.Debug, cancellationToken);
        if (result.Errors.Count > 0)
            return result.Errors.ToActionResult(result.Metadata);

        var response = new PowerPointImportResponse(result.Texts, result.Metadata with { Units = null }, []);
        return request.Debug ? new MultipartUnitsResult(response, result.Metadata.Units ?? []) : Ok(response);
    }

    /// <summary>
    /// Exports uploaded PowerPoint presentation with supplied translations and processing metadata.
    /// </summary>
    /// <param name="request">Multipart form request containing PowerPoint file and translations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Translated PowerPoint presentation file content or error response.</returns>
    [HttpPost("export")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status200OK, "multipart/mixed")]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Export([FromForm] PowerPointExportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new FileResponse(FileMetadata.Create("powerpoint").ForExport(true), [new("missing_file", ProcessingMessages.MissingFile)]));

        if (!request.File.FileName.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new FileResponse(FileMetadata.Create("powerpoint").ForExport(true), [new("unsupported_file_type", ProcessingMessages.UnsupportedExtension(".pptx"))]));

        var (success, translations, parseError) = await TranslationInputParser.TryParseAsync(
            Request?.HasFormContentType == true ? Request.Form.Files : null,
            request.Texts,
            _options,
            cancellationToken);

        if (!success)
            return parseError!.Code == "too_many_units" ? new[] { parseError! }.ToActionResult(FileMetadata.Create("powerpoint")) : BadRequest(new FileResponse(FileMetadata.Create("powerpoint").ForExport(true), [parseError!]));

        var selectionError = SelectionInput.Parse(request.SlideIds, out var ids);
        if (selectionError is not null)
            return BadRequest(new FileResponse(FileMetadata.Create("powerpoint").ForExport(true), [selectionError]));
        await using var stream = request.File.OpenReadStream();
        var result = await _powerPoint.ExportAsync(stream, translations!, new PowerPointSelection(ids), cancellationToken);
        if (result.Errors.Count > 0)
            return result.Errors.ToActionResult(result.Metadata);

        return new MultipartFileResult(result, FileTypeDetector.GetTranslatedFileName(request.File.FileName, FileType.PowerPoint));
    }

    /// <summary>
    /// Lists source slides without translation extraction.
    /// </summary>
    /// <param name="request">Uploaded source file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Native inventory and discovery metadata.</returns>
    [HttpPost("slides")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(SlidesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DiscoveryFailureResponse), 400)]
    [ProducesResponseType(typeof(DiscoveryFailureResponse), 413)]
    [ProducesResponseType(typeof(DiscoveryFailureResponse), 415)]
    [ProducesResponseType(typeof(DiscoveryFailureResponse), 422)]
    [ProducesResponseType(typeof(DiscoveryFailureResponse), 429)]
    [ProducesResponseType(typeof(DiscoveryFailureResponse), 500)]
    public async Task<IActionResult> Slides([FromForm] DiscoveryRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new DiscoveryFailureResponse(new("powerpoint", ProcessingStatus.Failed, []), [new("missing_file", ProcessingMessages.MissingFile)]));
        if (!request.File.FileName.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase))
            return StatusCode(415, new DiscoveryFailureResponse(new("powerpoint", ProcessingStatus.Failed, []), [new("unsupported_file_type", ProcessingMessages.UnsupportedExtension(".pptx"))]));
        await using var stream = request.File.OpenReadStream();
        var result = await _powerPoint.GetSlidesAsync(stream, cancellationToken);
        if (result.Errors.Count > 0)
        {
            var mapped = (ObjectResult)result.Errors.ToActionResult();
            return StatusCode(mapped.StatusCode!.Value, new DiscoveryFailureResponse(result.Metadata, result.Errors));
        }
        return Ok(result);
    }
}
