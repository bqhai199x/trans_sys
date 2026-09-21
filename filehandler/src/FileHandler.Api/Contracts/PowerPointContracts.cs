using FileHandler.Api.Common;
using Microsoft.AspNetCore.Mvc;

namespace FileHandler.Api.Contracts;

/// <summary>
/// Multipart form data payload for importing a PowerPoint presentation.
/// </summary>
public sealed class PowerPointImportRequest
{

    /// <summary>
    /// Whether to return informational skips and units.json; false by default.
    /// </summary>
    [FromForm(Name = "debug")]
    public bool Debug { get; init; }

    /// <summary>
    /// Uploaded PowerPoint source file (.pptx).
    /// </summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }

    /// <summary>
    /// Native IDs as JSON string array; omitted or blank selects visible content.
    /// </summary>
    [FromForm(Name = "slideIds")]
    public string? SlideIds { get; init; }
}

/// <summary>
/// Response payload for imported PowerPoint presentation.
/// </summary>
/// <param name="Texts">Extracted translation units in source order.</param>
/// <param name="Metadata">Format-specific PowerPoint metadata.</param>
/// <param name="Errors">Validation errors encountered during import.</param>
public sealed record PowerPointImportResponse(
    IReadOnlyList<string> Texts,
    FileMetadata Metadata,
    IReadOnlyList<FileError> Errors);

/// <summary>
/// Multipart form data payload for translating and exporting a PowerPoint presentation.
/// </summary>
public sealed class PowerPointExportRequest
{

    /// <summary>
    /// Uploaded PowerPoint source file (.pptx).
    /// </summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }

    /// <summary>
    /// Native IDs as JSON string array; omitted or blank selects visible content.
    /// </summary>
    [FromForm(Name = "slideIds")]
    public string? SlideIds { get; init; }

    /// <summary>
    /// Translated texts as JSON array string or uploaded file.
    /// </summary>
    [FromForm(Name = "texts")]
    public string? Texts { get; init; }
}
