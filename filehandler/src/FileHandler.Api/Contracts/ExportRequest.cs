using Microsoft.AspNetCore.Mvc;

namespace FileHandler.Api.Contracts;

/// <summary>
/// Multipart form data payload for translating and exporting a file.
/// </summary>
public sealed class ExportRequest
{

    /// <summary>
    /// Uploaded source file.
    /// </summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }

    /// <summary>
    /// JSON array of translated strings.
    /// </summary>
    [FromForm(Name = "translatedTexts")]
    public string? TranslatedTexts { get; init; }
}
