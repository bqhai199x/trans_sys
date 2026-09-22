using FileHandler.Api.Common;
using Microsoft.AspNetCore.Mvc;

namespace FileHandler.Api.Contracts;

/// <summary>
/// Multipart form data payload for importing a plain text document.
/// </summary>
public sealed class PlainTextImportRequest
{

    /// <summary>
    /// Whether to return informational skips and units.json; false by default.
    /// </summary>
    [FromForm(Name = "debug")]
    public bool Debug { get; init; }

    /// <summary>
    /// Uploaded plain text source file.
    /// </summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }
}

/// <summary>
/// Response payload for imported plain text document.
/// </summary>
/// <param name="Texts">Extracted translation units in source order.</param>
/// <param name="Metadata">Format-specific plain text metadata.</param>
/// <param name="Errors">Validation errors encountered during import.</param>
public sealed record PlainTextImportResponse(
    IReadOnlyList<string> Texts,
    FileMetadata Metadata,
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
