using Microsoft.AspNetCore.Mvc;

namespace FileHandler.Api.Contracts;

/// <summary>
/// Multipart upload for native sheet or slide inventory without selection.
/// </summary>
public sealed class DiscoveryRequest
{

    /// <summary>
    /// Source Office file whose native inventory is requested.
    /// </summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }
}
