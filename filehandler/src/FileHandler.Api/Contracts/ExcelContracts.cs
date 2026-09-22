using FileHandler.Api.Common;
using Microsoft.AspNetCore.Mvc;

namespace FileHandler.Api.Contracts;

/// <summary>
/// Multipart form data payload for importing an Excel spreadsheet.
/// </summary>
public sealed class ExcelImportRequest
{

    /// <summary>
    /// Whether to return informational skips and units.json; false by default.
    /// </summary>
    [FromForm(Name = "debug")]
    public bool Debug { get; init; }

    /// <summary>
    /// Uploaded Excel source file (.xlsx).
    /// </summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }

    /// <summary>
    /// Native IDs as JSON string array; omitted or blank selects visible content.
    /// </summary>
    [FromForm(Name = "sheetIds")]
    public string? SheetIds { get; init; }
}

/// <summary>
/// Response payload for imported Excel spreadsheet.
/// </summary>
/// <param name="Texts">Extracted translation units in source order.</param>
/// <param name="Metadata">Format-specific Excel metadata.</param>
/// <param name="Errors">Validation errors encountered during import.</param>
public sealed record ExcelImportResponse(
    IReadOnlyList<string> Texts,
    FileMetadata Metadata,
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
    /// Native IDs as JSON string array; omitted or blank selects visible content.
    /// </summary>
    [FromForm(Name = "sheetIds")]
    public string? SheetIds { get; init; }

    /// <summary>
    /// Translated texts as JSON array string or uploaded file.
    /// </summary>
    [FromForm(Name = "texts")]
    public string? Texts { get; init; }
}
