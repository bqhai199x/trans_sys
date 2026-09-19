using FileHandler.Api.Common;
using Microsoft.AspNetCore.Mvc;

namespace FileHandler.Api.Contracts;

/// <summary>
/// Multipart form data payload for importing a Markdown document.
/// </summary>
public sealed class MarkdownImportRequest
{

    /// <summary>
    /// Uploaded Markdown source file.
    /// </summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }
}

/// <summary>
/// Structural and formatting metadata for a Markdown document.
/// </summary>
public sealed class MarkdownMetadata
{

    /// <summary>
    /// Line ending convention detected in document.
    /// </summary>
    public string? NewlinePolicy { get; init; }

    /// <summary>
    /// Whether document has YAML frontmatter.
    /// </summary>
    public bool HasFrontmatter { get; init; }

    /// <summary>
    /// Total number of extracted units.
    /// </summary>
    public int UnitCount { get; init; }
}

/// <summary>
/// Response payload for imported Markdown document.
/// </summary>
/// <param name="Texts">Extracted translation units in source order.</param>
/// <param name="Metadata">Format-specific Markdown metadata.</param>
/// <param name="Errors">Validation errors encountered during import.</param>
public sealed record MarkdownImportResponse(
    IReadOnlyList<string> Texts,
    MarkdownMetadata? Metadata,
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
