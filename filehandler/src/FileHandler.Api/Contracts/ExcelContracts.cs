using FileHandler.Api.Common;
using Microsoft.AspNetCore.Mvc;

namespace FileHandler.Api.Contracts;

/// <summary>
/// Multipart form data payload for importing an Excel spreadsheet.
/// </summary>
public sealed class ExcelImportRequest
{

    /// <summary>
    /// Uploaded Excel source file (.xlsx).
    /// </summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }
}

/// <summary>
/// Structural and formatting metadata for an Excel spreadsheet.
/// </summary>
public sealed class ExcelMetadata
{

    /// <summary>
    /// Total number of extracted translation units.
    /// </summary>
    public int UnitCount { get; init; }
}

/// <summary>
/// Response payload for imported Excel spreadsheet.
/// </summary>
/// <param name="Texts">Extracted translation units in source order.</param>
/// <param name="Metadata">Format-specific Excel metadata.</param>
/// <param name="Errors">Validation errors encountered during import.</param>
public sealed record ExcelImportResponse(
    IReadOnlyList<string> Texts,
    ExcelMetadata? Metadata,
    IReadOnlyList<FileError> Errors);

/// <summary>
/// Multipart form data payload for translating and exporting an Excel spreadsheet.
/// </summary>
public sealed class ExcelExportRequest
{

    /// <summary>
    /// Uploaded Excel source file (.xlsx).
    /// </summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }

    /// <summary>
    /// Translated texts as JSON array string or uploaded file.
    /// </summary>
    [FromForm(Name = "texts")]
    public string? Texts { get; init; }
}
