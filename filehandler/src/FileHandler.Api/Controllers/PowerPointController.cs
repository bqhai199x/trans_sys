using System.Text.Json;
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
    /// <returns>Extracted texts and PowerPoint metadata, or error response.</returns>
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(PowerPointImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Import([FromForm] PowerPointImportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new[] { new FileError("missing_file", "Field file là bắt buộc.") });

        if (!request.File.FileName.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new[] { new FileError("unsupported_file_type", "Chỉ hỗ trợ tệp .pptx.") });

        await using var stream = request.File.OpenReadStream();
        var result = await _powerPoint.ImportAsync(stream, cancellationToken);
        if (result.Errors.Count > 0)
            return result.Errors.ToActionResult();

        var metadata = new PowerPointMetadata { UnitCount = result.Texts.Count };
        return Ok(new PowerPointImportResponse(result.Texts, metadata, []));
    }

    /// <summary>
    /// Exports uploaded PowerPoint presentation with supplied translations and metadata header.
    /// </summary>
    /// <param name="request">Multipart form request containing PowerPoint file and translations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Translated PowerPoint presentation file content or error response.</returns>
    [HttpPost("export")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, PowerPointService.ContentType)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Export([FromForm] PowerPointExportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new[] { new FileError("missing_file", "Field file là bắt buộc.") });

        if (!request.File.FileName.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new[] { new FileError("unsupported_file_type", "Chỉ hỗ trợ tệp .pptx.") });

        var (success, translations, parseError) = await TranslationInputParser.TryParseAsync(
            Request?.HasFormContentType == true ? Request.Form.Files : null,
            request.Texts,
            _options,
            cancellationToken);

        if (!success)
            return parseError!.Code == "too_many_units" ? new[] { parseError! }.ToActionResult() : BadRequest(new[] { parseError! });

        await using var stream = request.File.OpenReadStream();
        var result = await _powerPoint.ExportAsync(stream, translations!, cancellationToken);
        if (result.Errors.Count > 0)
            return result.Errors.ToActionResult();

        var metadata = new PowerPointMetadata { UnitCount = translations!.Count };
        Response.Headers["X-File-Metadata"] = JsonSerializer.Serialize(metadata);

        return File(result.Content!, result.ContentType, FileTypeDetector.GetTranslatedFileName(request.File.FileName, FileType.PowerPoint));
    }
}
