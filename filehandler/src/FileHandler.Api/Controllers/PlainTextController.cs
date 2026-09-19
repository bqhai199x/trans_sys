using System.Text.Json;
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
    /// <returns>Extracted texts and plain text metadata, or error response.</returns>
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(PlainTextImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Import([FromForm] PlainTextImportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new[] { new FileError("missing_file", "Field file là bắt buộc.") });

        if (!request.File.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new[] { new FileError("unsupported_file_type", "Chỉ hỗ trợ tệp .txt.") });

        await using var stream = request.File.OpenReadStream();
        var result = await _plainText.ImportAsync(stream, cancellationToken);
        if (result.Errors.Count > 0)
            return result.Errors.ToActionResult();

        var metadata = new PlainTextMetadata { ParagraphCount = result.Texts.Count };
        return Ok(new PlainTextImportResponse(result.Texts, metadata, []));
    }

    /// <summary>
    /// Exports uploaded plain text file with supplied translations and metadata header.
    /// </summary>
    /// <param name="request">Multipart form request containing plain text file and translations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Translated plain text file content or error response.</returns>
    [HttpPost("export")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "text/plain")]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Export([FromForm] PlainTextExportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new[] { new FileError("missing_file", "Field file là bắt buộc.") });

        if (!request.File.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new[] { new FileError("unsupported_file_type", "Chỉ hỗ trợ tệp .txt.") });

        var (success, translations, parseError) = await TranslationInputParser.TryParseAsync(
            Request?.HasFormContentType == true ? Request.Form.Files : null,
            request.Texts,
            _options,
            cancellationToken);

        if (!success)
            return parseError!.Code == "too_many_units" ? new[] { parseError! }.ToActionResult() : BadRequest(new[] { parseError! });

        await using var stream = request.File.OpenReadStream();
        var result = await _plainText.ExportAsync(stream, translations!, cancellationToken);
        if (result.Errors.Count > 0)
            return result.Errors.ToActionResult();

        var metadata = new PlainTextMetadata { ParagraphCount = translations!.Count };
        Response.Headers["X-File-Metadata"] = JsonSerializer.Serialize(metadata);

        return File(result.Content!, result.ContentType, FileTypeDetector.GetTranslatedFileName(request.File.FileName, FileType.PlainText));
    }
}
