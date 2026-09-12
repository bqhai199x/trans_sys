using Microsoft.AspNetCore.Mvc;

namespace FileHandler.Api.Contracts;

/// <summary>
/// Multipart form data payload for extracting translatable units from a file.
/// </summary>
public sealed class ImportRequest
{

    /// <summary>
    /// Uploaded source file.
    /// </summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }
}
