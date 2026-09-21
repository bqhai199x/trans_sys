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
    /// <returns>Import JSON, optional units.json multipart attachment, or fatal JSON envelope.</returns>
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(MarkdownImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Import([FromForm] MarkdownImportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new FileResponse(FileMetadata.Create("markdown").ForExport(true), [new("missing_file", ProcessingMessages.MissingFile)]));

        if (!request.File.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new FileResponse(FileMetadata.Create("markdown").ForExport(true), [new("unsupported_file_type", ProcessingMessages.UnsupportedExtension(".md"))]));

        await using var stream = request.File.OpenReadStream();
        var result = await _markdown.ImportAsync(stream, request.Debug, cancellationToken);
        if (result.Errors.Count > 0)
            return result.Errors.ToActionResult(result.Metadata);

        var response = new MarkdownImportResponse(result.Texts, result.Metadata with { Units = null }, []);
        return request.Debug ? new MultipartUnitsResult(response, result.Metadata.Units ?? []) : Ok(response);
    }

    /// <summary>
    /// Exports uploaded Markdown file with supplied translations and processing metadata.
    /// </summary>
    /// <param name="request">Multipart form request containing Markdown file and translations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Translated Markdown file content or error response.</returns>
    [HttpPost("export")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status200OK, "multipart/mixed")]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Export([FromForm] MarkdownExportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
            return BadRequest(new FileResponse(FileMetadata.Create("markdown").ForExport(true), [new("missing_file", ProcessingMessages.MissingFile)]));

        if (!request.File.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new FileResponse(FileMetadata.Create("markdown").ForExport(true), [new("unsupported_file_type", ProcessingMessages.UnsupportedExtension(".md"))]));

        var (success, translations, parseError) = await TranslationInputParser.TryParseAsync(
            Request?.HasFormContentType == true ? Request.Form.Files : null,
            request.Texts,
            _options,
            cancellationToken);

        if (!success)
            return parseError!.Code == "too_many_units" ? new[] { parseError! }.ToActionResult(FileMetadata.Create("markdown")) : BadRequest(new FileResponse(FileMetadata.Create("markdown").ForExport(true), [parseError!]));

        await using var stream = request.File.OpenReadStream();
        var result = await _markdown.ExportAsync(stream, translations!, cancellationToken);
        if (result.Errors.Count > 0)
            return result.Errors.ToActionResult(result.Metadata);

        return new MultipartFileResult(result, FileTypeDetector.GetTranslatedFileName(request.File.FileName, FileType.Markdown));
    }
}
