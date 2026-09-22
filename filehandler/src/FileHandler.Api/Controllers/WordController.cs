using FileHandler.Api.Common;
using FileHandler.Api.Contracts;
using FileHandler.Api.Modules.Word;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FileHandler.Api.Controllers;

/// <summary>
/// REST API controller handling Word document imports and exports.
/// </summary>
[ApiController]
[Route("api/word")]
[EnableRateLimiting("file-processing")]
public sealed class WordController : ControllerBase
{

    /// <summary>
    /// Word document import and export service.
    /// </summary>
    private readonly WordService _word;

    /// <summary>
    /// Request handling limits.
    /// </summary>
    private readonly FileHandlingOptions _options;

    /// <summary>
    /// Creates controller with Word service and configured limits.
    /// </summary>
    /// <param name="word">Word document processing service.</param>
    /// <param name="options">Configured limits options.</param>
    public WordController(WordService word, IOptions<FileHandlingOptions>? options = null)
    {
        _word = word;
        _options = options?.Value ?? new FileHandlingOptions();
    }

    /// <summary>
    /// Imports uploaded Word document as text array with metadata.
    /// </summary>
    /// <param name="request">Multipart form request containing Word file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Import JSON, optional units.json multipart attachment, or fatal JSON envelope.</returns>
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(WordImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Import([FromForm] WordImportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new FileResponse(FileMetadata.Create("word").ForExport(true), [new("missing_file", ProcessingMessages.MissingFile)]));

        if (!request.File.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new FileResponse(FileMetadata.Create("word").ForExport(true), [new("unsupported_file_type", ProcessingMessages.UnsupportedExtension(".docx"))]));

        await using var stream = request.File.OpenReadStream();
        var result = await _word.ImportAsync(stream, request.Debug, cancellationToken);
        if (result.Errors.Count > 0)
            return result.Errors.ToActionResult(result.Metadata);

        var response = new WordImportResponse(result.Texts, result.Metadata with { Units = null }, []);
        return request.Debug ? new MultipartUnitsResult(response, result.Metadata.Units ?? []) : Ok(response);
    }

    /// <summary>
    /// Exports uploaded Word document with supplied translations and processing metadata.
    /// </summary>
    /// <param name="request">Multipart form request containing Word file and translations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Translated Word file content or error response.</returns>
    [HttpPost("export")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status200OK, "multipart/mixed")]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Export([FromForm] WordExportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new FileResponse(FileMetadata.Create("word").ForExport(true), [new("missing_file", ProcessingMessages.MissingFile)]));

        if (!request.File.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new FileResponse(FileMetadata.Create("word").ForExport(true), [new("unsupported_file_type", ProcessingMessages.UnsupportedExtension(".docx"))]));

        var (success, translations, parseError) = await TranslationInputParser.TryParseAsync(
            Request?.HasFormContentType == true ? Request.Form.Files : null,
            request.Texts,
            _options,
            cancellationToken);

        if (!success)
            return parseError!.Code == "too_many_units" ? new[] { parseError! }.ToActionResult(FileMetadata.Create("word")) : BadRequest(new FileResponse(FileMetadata.Create("word").ForExport(true), [parseError!]));

        await using var stream = request.File.OpenReadStream();
        var result = await _word.ExportAsync(stream, translations!, cancellationToken);
        if (result.Errors.Count > 0)
            return result.Errors.ToActionResult(result.Metadata);

        return new MultipartFileResult(result, FileTypeDetector.GetTranslatedFileName(request.File.FileName, FileType.Word));
    }
}
