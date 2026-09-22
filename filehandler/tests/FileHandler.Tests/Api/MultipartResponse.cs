using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace FileHandler.Tests.Api;

/// <summary>
/// Binary-safe parser for exported metadata and file parts.
/// </summary>
/// <param name="Metadata">Parsed metadata JSON object.</param>
/// <param name="Bytes">Exact downloaded file bytes.</param>
/// <param name="ContentType">File media type including optional charset.</param>
/// <param name="FileName">Unicode download name.</param>
internal sealed record MultipartResponse(JsonElement Metadata, byte[] Bytes, string ContentType, string FileName)
{

    /// <summary>
    /// Decodes file bytes for text-format assertions.
    /// </summary>
    internal string Text => Encoding.UTF8.GetString(Bytes);

    /// <summary>
    /// Reads exactly two ordered parts from HTTP export response.
    /// </summary>
    /// <param name="response">Successful multipart response.</param>
    /// <returns>Independent JSON and binary file payloads.</returns>
    internal static async Task<MultipartResponse> ReadAsync(HttpResponseMessage response)
    {
        Assert.Equal("multipart/mixed", response.Content.Headers.ContentType?.MediaType);
        var boundary = response.Content.Headers.ContentType!.Parameters.Single(p => p.Name == "boundary").Value!.Trim('"');
        using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        var reader = new MultipartReader(boundary, stream);
        var jsonPart = await reader.ReadNextSectionAsync();
        Assert.NotNull(jsonPart);
        Assert.Equal("<metadata>", jsonPart.Headers!["Content-ID"].ToString());
        Assert.Equal("application/json; charset=utf-8", jsonPart.ContentType);
        using var json = await JsonDocument.ParseAsync(jsonPart.Body);
        Assert.Empty(json.RootElement.GetProperty("errors").EnumerateArray());
        var filePart = await reader.ReadNextSectionAsync();
        Assert.NotNull(filePart);
        Assert.Equal("<file>", filePart.Headers!["Content-ID"].ToString());
        using var bytes = new MemoryStream();
        await filePart.Body.CopyToAsync(bytes);
        var disposition = ContentDispositionHeaderValue.Parse(filePart.ContentDisposition!);
        Assert.Null(await reader.ReadNextSectionAsync());
        return new(json.RootElement.GetProperty("metadata").Clone(), bytes.ToArray(), filePart.ContentType!, disposition.FileNameStar.Value!);
    }
}
