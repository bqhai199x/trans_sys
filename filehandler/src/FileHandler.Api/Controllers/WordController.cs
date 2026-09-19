using System.Text.Json;
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
    /// <returns>Extracted texts and Word metadata, or error response.</returns>
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(WordImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Import([FromForm] WordImportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new[] { new FileError("missing_file", "Field file là bắt buộc.") });

        if (!request.File.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new[] { new FileError("unsupported_file_type", "Chỉ hỗ trợ tệp .docx.") });

        await using var stream = request.File.OpenReadStream();
        var result = await _word.ImportAsync(stream, cancellationToken);
        if (result.Errors.Count > 0)
            return result.Errors.ToActionResult();

        var metadata = new WordMetadata { UnitCount = result.Texts.Count };
        return Ok(new WordImportResponse(result.Texts, metadata, []));
    }

    /// <summary>
    /// Exports uploaded Word document with supplied translations and metadata header.
    /// </summary>
    /// <param name="request">Multipart form request containing Word file and translations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Translated Word file content or error response.</returns>
    [HttpPost("export")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, WordService.ContentType)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Export([FromForm] WordExportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new[] { new FileError("missing_file", "Field file là bắt buộc.") });

        if (!request.File.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new[] { new FileError("unsupported_file_type", "Chỉ hỗ trợ tệp .docx.") });

        var (success, translations, parseError) = await TranslationInputParser.TryParseAsync(
            Request?.HasFormContentType == true ? Request.Form.Files : null,
            request.Texts,
            _options,
            cancellationToken);

        if (!success)
            return parseError!.Code == "too_many_units" ? new[] { parseError! }.ToActionResult() : BadRequest(new[] { parseError! });

        await using var stream = request.File.OpenReadStream();
        var result = await _word.ExportAsync(stream, translations!, cancellationToken);
        if (result.Errors.Count > 0)
            return result.Errors.ToActionResult();

        var metadata = new WordMetadata { UnitCount = translations!.Count };
        Response.Headers["X-File-Metadata"] = JsonSerializer.Serialize(metadata);

        return File(result.Content!, result.ContentType, FileTypeDetector.GetTranslatedFileName(request.File.FileName, FileType.Word));
    }
}
