using System.Text.Json;
using Microsoft.Net.Http.Headers;

namespace FileHandler.Api.Common;

/// <summary>
/// Streams ordered multipart response parts without combining payload buffers.
/// </summary>
internal static class MultipartResponseWriter
{

    /// <summary>
    /// Shared web JSON serialization settings.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Initializes successful multipart response after processing has completed.
    /// </summary>
    /// <param name="response">Caller-owned HTTP response.</param>
    /// <returns>Fresh MIME boundary for this response.</returns>
    internal static string Start(HttpResponse response)
    {
        response.StatusCode = StatusCodes.Status200OK;
        var boundary = "filehandler_" + Guid.NewGuid().ToString("N");
        response.ContentType = "multipart/mixed; boundary=" + boundary;
        return boundary;
    }

    /// <summary>
    /// Writes JSON envelope or downloadable JSON attachment directly to response.
    /// </summary>
    /// <param name="response">Caller-owned HTTP response.</param>
    /// <param name="boundary">Current MIME boundary.</param>
    /// <param name="contentId">Part identifier without angle brackets.</param>
    /// <param name="value">JSON payload.</param>
    /// <param name="fileName">Attachment basename, or null for envelope.</param>
    /// <param name="token">Request cancellation token.</param>
    /// <returns>Task completing after JSON and trailing CRLF are written.</returns>
    internal static async Task WriteJsonAsync(HttpResponse response, string boundary, string contentId, object value, string? fileName, CancellationToken token)
    {
        await WriteHeadersAsync(response, boundary, contentId, "application/json; charset=utf-8", fileName, token);
        await JsonSerializer.SerializeAsync(response.Body, value, JsonOptions, token);
        await response.WriteAsync("\r\n", token);
    }

    /// <summary>
    /// Writes MIME headers with safe Unicode attachment filenames.
    /// </summary>
    /// <param name="response">Caller-owned HTTP response.</param>
    /// <param name="boundary">Current MIME boundary.</param>
    /// <param name="contentId">Part identifier without angle brackets.</param>
    /// <param name="contentType">Payload media type.</param>
    /// <param name="fileName">Attachment basename, or null for envelope.</param>
    /// <param name="token">Request cancellation token.</param>
    /// <returns>Task completing after MIME headers are written.</returns>
    internal static async Task WriteHeadersAsync(HttpResponse response, string boundary, string contentId, string contentType, string? fileName, CancellationToken token)
    {
        var attachment = "";
        if (fileName is not null)
        {
            var disposition = new ContentDispositionHeaderValue("attachment");
            disposition.SetHttpFileName(fileName);
            attachment = $"Content-Disposition: {disposition}\r\n";
        }
        await response.WriteAsync($"--{boundary}\r\nContent-Type: {contentType}\r\nContent-ID: <{contentId}>\r\n{attachment}\r\n", token);
    }
}
