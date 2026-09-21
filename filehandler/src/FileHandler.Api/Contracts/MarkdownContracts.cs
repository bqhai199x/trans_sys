using FileHandler.Api.Common;
using Microsoft.AspNetCore.Mvc;

namespace FileHandler.Api.Contracts;

/// <summary>
/// Multipart form data payload for importing a Markdown document.
/// </summary>
public sealed class MarkdownImportRequest
{

    /// <summary>
    /// Whether to return informational skips and units.json; false by default.
    /// </summary>
    [FromForm(Name = "debug")]
    public bool Debug { get; init; }

    /// <summary>
    /// Uploaded Markdown source file.
    /// </summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }
}

/// <summary>
/// Response payload for imported Markdown document.
/// </summary>
/// <param name="Texts">Extracted translation units in source order.</param>
/// <param name="Metadata">Format-specific Markdown metadata.</param>
/// <param name="Errors">Validation errors encountered during import.</param>
public sealed record MarkdownImportResponse(
    IReadOnlyList<string> Texts,
    FileMetadata Metadata,
    IReadOnlyList<FileError> Errors);

/// <summary>
/// Multipart form data payload for translating and exporting a Markdown document.
/// </summary>
public sealed class MarkdownExportRequest
{

    /// <summary>
    /// Uploaded Markdown source file.
    /// </summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }

    /// <summary>
    /// Translated texts as JSON array string or uploaded file.
    /// </summary>
    [FromForm(Name = "texts")]
    public string? Texts { get; init; }
}
