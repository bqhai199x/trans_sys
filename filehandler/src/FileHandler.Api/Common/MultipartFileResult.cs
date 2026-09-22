using Microsoft.AspNetCore.Mvc;

namespace FileHandler.Api.Common;

/// <summary>
/// Streams metadata followed by validated file bytes as multipart/mixed.
/// </summary>
/// <param name="result">Completed export with validated file content.</param>
/// <param name="fileName">Suggested download filename.</param>
public sealed class MultipartFileResult(ExportResult result, string fileName) : IActionResult
{

    /// <summary>
    /// Completed export retained until response writing finishes.
    /// </summary>
    public ExportResult Result { get; } = result;

    /// <summary>
    /// Basename stripped of path components and header control characters.
    /// </summary>
    public string FileName { get; } = new(Path.GetFileName(fileName.Replace('\\', '/')).Where(c => !char.IsControl(c)).ToArray());

    /// <summary>
    /// Writes two parts without buffering combined response.
    /// </summary>
    /// <param name="context">Current MVC execution context.</param>
    /// <returns>Task completing when response bytes have been written.</returns>
    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        var token = context.HttpContext.RequestAborted;
        token.ThrowIfCancellationRequested();
        var content = Result.Content ?? throw new InvalidOperationException("Export has no file content.");
        var boundary = MultipartResponseWriter.Start(response);
        await MultipartResponseWriter.WriteJsonAsync(response, boundary, "metadata", new FileResponse(Result.Metadata.ForExport(), Result.Errors), null, token);
        await MultipartResponseWriter.WriteHeadersAsync(response, boundary, "file", Result.ContentType, FileName, token);
        await response.Body.WriteAsync(content, token);
        await response.WriteAsync($"\r\n--{boundary}--\r\n", token);
    }
}
