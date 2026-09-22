using Microsoft.AspNetCore.Mvc;

namespace FileHandler.Api.Common;

/// <summary>
/// Streams import envelope followed by explicitly requested unit mapping attachment.
/// </summary>
/// <param name="response">Import envelope without inline unit details.</param>
/// <param name="units">Ordered mapping to serialize as units.json.</param>
public sealed class MultipartUnitsResult(object response, IReadOnlyList<UnitMetadata> units) : IActionResult
{

    /// <summary>
    /// Writes import JSON and unit attachment without buffering combined response.
    /// </summary>
    /// <param name="context">Current MVC execution context.</param>
    /// <returns>Task completing after both MIME parts have been written.</returns>
    public async Task ExecuteResultAsync(ActionContext context)
    {
        var token = context.HttpContext.RequestAborted;
        token.ThrowIfCancellationRequested();
        var output = context.HttpContext.Response;
        var boundary = MultipartResponseWriter.Start(output);
        await MultipartResponseWriter.WriteJsonAsync(output, boundary, "metadata", response, null, token);
        await MultipartResponseWriter.WriteJsonAsync(output, boundary, "units", new UnitsFileResponse(units), "units.json", token);
        await output.WriteAsync($"--{boundary}--\r\n", token);
    }
}

/// <summary>
/// Downloadable source mapping requested during import.
/// </summary>
/// <param name="Units">Ordered source unit indices, kinds and locations.</param>
public sealed record UnitsFileResponse(IReadOnlyList<UnitMetadata> Units);
