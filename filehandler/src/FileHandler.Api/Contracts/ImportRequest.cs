using Microsoft.AspNetCore.Mvc;

namespace FileHandler.Api.Contracts;

public sealed class ImportRequest
{
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }
}
