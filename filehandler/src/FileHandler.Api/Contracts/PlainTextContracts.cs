using FileHandler.Api.Common;
using Microsoft.AspNetCore.Mvc;

namespace FileHandler.Api.Contracts;

/// <summary>
/// Multipart form data payload for importing a plain text document.
/// </summary>
public sealed class PlainTextImportRequest
{

    /// <summary>
    /// Uploaded plain text source file.
    /// </summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }
}

/// <summary>
/// Structural and formatting metadata for a plain text document.
/// </summary>
public sealed class PlainTextMetadata
{

    /// <summary>
    /// Detected encoding format description.
    /// </summary>
    public string Encoding { get; init; } = "utf-8";

    /// <summary>
    /// Whether file begins with UTF-8 byte order mark.
    /// </summary>
    public bool HasBom { get; init; }

    /// <summary>
    /// Total number of extracted paragraphs.
    /// </summary>
    public int ParagraphCount { get; init; }
}

/// <summary>
/// Response payload for imported plain text document.
/// </summary>
/// <param name="Texts">Extracted translation units in source order.</param>
/// <param name="Metadata">Format-specific plain text metadata.</param>
/// <param name="Errors">Validation errors encountered during import.</param>
public sealed record PlainTextImportResponse(
    IReadOnlyList<string> Texts,
    PlainTextMetadata? Metadata,
    IReadOnlyList<FileError> Errors);

/// <summary>
/// Multipart form data payload for translating and exporting a plain text document.
/// </summary>
public sealed class PlainTextExportRequest
{

    /// <summary>
    /// Uploaded plain text source file.
    /// </summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }

    /// <summary>
    /// Translated texts as JSON array string or uploaded file.
    /// </summary>
    [FromForm(Name = "texts")]
    public string? Texts { get; init; }
}
