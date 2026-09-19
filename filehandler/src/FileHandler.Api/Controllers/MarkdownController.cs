using System.Text.Json;
using FileHandler.Api.Common;
using FileHandler.Api.Contracts;
using FileHandler.Api.Modules.Markdown;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FileHandler.Api.Controllers;

/// <summary>
/// REST API controller handling Markdown document imports and exports.
/// </summary>
[ApiController]
[Route("api/markdown")]
[EnableRateLimiting("file-processing")]
public sealed class MarkdownController : ControllerBase
{

    /// <summary>
    /// Markdown import and export service.
    /// </summary>
    private readonly MarkdownService _markdown;

    /// <summary>
    /// Request handling limits.
    /// </summary>
    private readonly FileHandlingOptions _options;

    /// <summary>
    /// Creates controller with Markdown service and configured limits.
    /// </summary>
    /// <param name="markdown">Markdown processing service.</param>
    /// <param name="options">Configured limits options.</param>
    public MarkdownController(MarkdownService markdown, IOptions<FileHandlingOptions>? options = null)
    {
        _markdown = markdown;
        _options = options?.Value ?? new FileHandlingOptions();
    }

    /// <summary>
    /// Imports uploaded Markdown file as text array with metadata.
    /// </summary>
    /// <param name="request">Multipart form request containing Markdown file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted texts and Markdown metadata, or error response.</returns>
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(MarkdownImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Import([FromForm] MarkdownImportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new[] { new FileError("missing_file", "Field file là bắt buộc.") });

        if (!request.File.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new[] { new FileError("unsupported_file_type", "Chỉ hỗ trợ tệp .md.") });

        await using var stream = request.File.OpenReadStream();
        var result = await _markdown.ImportAsync(stream, cancellationToken);
        if (result.Errors.Count > 0)
            return result.Errors.ToActionResult();

        var metadata = new MarkdownMetadata { UnitCount = result.Texts.Count };
        return Ok(new MarkdownImportResponse(result.Texts, metadata, []));
    }

    /// <summary>
    /// Exports uploaded Markdown file with supplied translations and metadata header.
    /// </summary>
    /// <param name="request">Multipart form request containing Markdown file and translations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Translated Markdown file content or error response.</returns>
    [HttpPost("export")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "text/markdown")]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Export([FromForm] MarkdownExportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new[] { new FileError("missing_file", "Field file là bắt buộc.") });

        if (!request.File.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new[] { new FileError("unsupported_file_type", "Chỉ hỗ trợ tệp .md.") });

        var (success, translations, parseError) = await TranslationInputParser.TryParseAsync(
            Request?.HasFormContentType == true ? Request.Form.Files : null,
            request.Texts,
            _options,
            cancellationToken);

        if (!success)
            return parseError!.Code == "too_many_units" ? new[] { parseError! }.ToActionResult() : BadRequest(new[] { parseError! });

        await using var stream = request.File.OpenReadStream();
        var result = await _markdown.ExportAsync(stream, translations!, cancellationToken);
        if (result.Errors.Count > 0)
            return result.Errors.ToActionResult();

        var metadata = new MarkdownMetadata { UnitCount = translations!.Count };
        Response.Headers["X-File-Metadata"] = JsonSerializer.Serialize(metadata);

        return File(result.Content!, result.ContentType, FileTypeDetector.GetTranslatedFileName(request.File.FileName, FileType.Markdown));
    }
}
