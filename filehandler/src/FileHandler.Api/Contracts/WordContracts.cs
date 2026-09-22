using FileHandler.Api.Common;
using Microsoft.AspNetCore.Mvc;

namespace FileHandler.Api.Contracts;

/// <summary>
/// Multipart form data payload for importing a Word document.
/// </summary>
public sealed class WordImportRequest
{

    /// <summary>
    /// Whether to return informational skips and units.json; false by default.
    /// </summary>
    [FromForm(Name = "debug")]
    public bool Debug { get; init; }

    /// <summary>
    /// Uploaded Word source file (.docx).
    /// </summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }
}

/// <summary>
/// Response payload for imported Word document.
/// </summary>
/// <param name="Texts">Extracted translation units in source order.</param>
/// <param name="Metadata">Format-specific Word metadata.</param>
/// <param name="Errors">Validation errors encountered during import.</param>
public sealed record WordImportResponse(
    IReadOnlyList<string> Texts,
    FileMetadata Metadata,
    IReadOnlyList<FileError> Errors);

/// <summary>
/// Multipart form data payload for translating and exporting a Word document.
/// </summary>
public sealed class WordExportRequest
{

    /// <summary>
    /// Uploaded Word source file (.docx).
    /// </summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }

    /// <summary>
    /// Translated texts as JSON array string or uploaded file.
    /// </summary>
    [FromForm(Name = "texts")]
    public string? Texts { get; init; }
}
