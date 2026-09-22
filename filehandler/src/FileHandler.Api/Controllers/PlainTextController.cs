using FileHandler.Api.Common;
using FileHandler.Api.Contracts;
using FileHandler.Api.Modules.PlainText;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FileHandler.Api.Controllers;

/// <summary>
/// REST API controller handling plain text document imports and exports.
/// </summary>
[ApiController]
[Route("api/plaintext")]
[EnableRateLimiting("file-processing")]
public sealed class PlainTextController : ControllerBase
{

    /// <summary>
    /// Plain text import and export service.
    /// </summary>
    private readonly PlainTextService _plainText;

    /// <summary>
    /// Request handling limits.
    /// </summary>
    private readonly FileHandlingOptions _options;

    /// <summary>
    /// Creates controller with PlainText service and configured limits.
    /// </summary>
    /// <param name="plainText">Plain text processing service.</param>
    /// <param name="options">Configured limits options.</param>
    public PlainTextController(PlainTextService plainText, IOptions<FileHandlingOptions>? options = null)
    {
        _plainText = plainText;
        _options = options?.Value ?? new FileHandlingOptions();
    }

    /// <summary>
    /// Imports uploaded plain text file as text array with metadata.
    /// </summary>
    /// <param name="request">Multipart form request containing plain text file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Import JSON, optional units.json multipart attachment, or fatal JSON envelope.</returns>
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(PlainTextImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Import([FromForm] PlainTextImportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new FileResponse(FileMetadata.Create("plaintext").ForExport(true), [new("missing_file", ProcessingMessages.MissingFile)]));

        if (!request.File.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new FileResponse(FileMetadata.Create("plaintext").ForExport(true), [new("unsupported_file_type", ProcessingMessages.UnsupportedExtension(".txt"))]));

        await using var stream = request.File.OpenReadStream();
        var result = await _plainText.ImportAsync(stream, request.Debug, cancellationToken);
        if (result.Errors.Count > 0)
            return result.Errors.ToActionResult(result.Metadata);

        var response = new PlainTextImportResponse(result.Texts, result.Metadata with { Units = null }, []);
        return request.Debug ? new MultipartUnitsResult(response, result.Metadata.Units ?? []) : Ok(response);
    }

    /// <summary>
    /// Exports uploaded plain text file with supplied translations and processing metadata.
    /// </summary>
    /// <param name="request">Multipart form request containing plain text file and translations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Translated plain text file content or error response.</returns>
    [HttpPost("export")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status200OK, "multipart/mixed")]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Export([FromForm] PlainTextExportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new FileResponse(FileMetadata.Create("plaintext").ForExport(true), [new("missing_file", ProcessingMessages.MissingFile)]));

        if (!request.File.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new FileResponse(FileMetadata.Create("plaintext").ForExport(true), [new("unsupported_file_type", ProcessingMessages.UnsupportedExtension(".txt"))]));

        var (success, translations, parseError) = await TranslationInputParser.TryParseAsync(
            Request?.HasFormContentType == true ? Request.Form.Files : null,
            request.Texts,
            _options,
            cancellationToken);

        if (!success)
            return parseError!.Code == "too_many_units" ? new[] { parseError! }.ToActionResult(FileMetadata.Create("plaintext")) : BadRequest(new FileResponse(FileMetadata.Create("plaintext").ForExport(true), [parseError!]));

        await using var stream = request.File.OpenReadStream();
        var result = await _plainText.ExportAsync(stream, translations!, cancellationToken);
        if (result.Errors.Count > 0)
            return result.Errors.ToActionResult(result.Metadata);

        return new MultipartFileResult(result, FileTypeDetector.GetTranslatedFileName(request.File.FileName, FileType.PlainText));
    }
}
