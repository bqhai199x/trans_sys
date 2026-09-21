namespace FileHandler.Api.Common;

/// <summary>
/// Creates consistent envelopes for failures before service execution.
/// </summary>
internal static class FileResponses
{

    /// <summary>
    /// Resolves format from routed request without reading upload body.
    /// </summary>
    /// <param name="context">Current request context.</param>
    /// <param name="errors">Fatal request errors.</param>
    /// <returns>Failed metadata with unknown unit count.</returns>
    internal static object Failure(HttpContext context, IReadOnlyList<FileError> errors)
    {
        var segments = context.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var format = segments is { Length: >= 2 } ? segments[1].ToLowerInvariant() : "unknown";
        if (segments is { Length: >= 3 } && segments[2].ToLowerInvariant() is "sheets" or "slides")
            return new DiscoveryFailureResponse(new(format, ProcessingStatus.Failed, []), errors);
        return new FileResponse(FileMetadata.Create(format).ForExport(true), errors);
    }
}
