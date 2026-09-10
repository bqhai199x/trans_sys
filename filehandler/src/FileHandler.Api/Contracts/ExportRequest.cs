using Microsoft.AspNetCore.Mvc;

namespace FileHandler.Api.Contracts;

public sealed class ExportRequest
{
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }

    [FromForm(Name = "translatedTexts")]
    public string? TranslatedTexts { get; init; }
}
