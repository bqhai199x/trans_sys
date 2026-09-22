using FileHandler.Api.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace FileHandler.Tests.Api;

/// <summary>
/// Verifies binary-safe multipart writing and response cancellation.
/// </summary>
public sealed class MultipartFileResultTests
{

    /// <summary>
    /// Checks Unicode filenames and arbitrary binary bytes survive actual result writing.
    /// </summary>
    /// <returns>Task completing after independent multipart parsing.</returns>
    [Fact]
    public async Task Writer_PreservesBinaryAndUnicodeFilename()
    {
        var bytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var export = new ExportResult(bytes, "application/octet-stream", []) { Metadata = FileMetadata.Create("excel") with { UnitCount = 0, Units = [] } };
        var result = new MultipartFileResult(export, "C:\\fake\\日本語.XLSX");
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;
        await result.ExecuteResultAsync(new ActionContext(context, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));
        using var response = new HttpResponseMessage { Content = new ByteArrayContent(body.ToArray()) };
        response.Content.Headers.TryAddWithoutValidation("Content-Type", context.Response.ContentType);
        var parsed = await MultipartResponse.ReadAsync(response);
        Assert.Equal(bytes, parsed.Bytes);
        Assert.Equal("日本語.XLSX", parsed.FileName);
        Assert.Equal("application/octet-stream", parsed.ContentType);
        Assert.False(parsed.Metadata.TryGetProperty("units", out _));
        Assert.False(context.Response.Headers.ContainsKey("X-File-Metadata"));
    }

    /// <summary>
    /// Checks cancellation before response writing publishes no partial bytes.
    /// </summary>
    /// <param name="units">Whether response contains a mapping attachment.</param>
    /// <returns>Task completing after cancellation assertions.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Writer_PropagatesCancellationBeforeStartingResponse(bool units)
    {
        IActionResult result = units ? new MultipartUnitsResult(new FileResponse(FileMetadata.Create("word"), []), []) : new MultipartFileResult(new([1, 2], "application/octet-stream", []), "file.xlsx");
        var context = new DefaultHttpContext { RequestAborted = new CancellationToken(true) };
        using var body = new MemoryStream();
        context.Response.Body = body;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result.ExecuteResultAsync(new ActionContext(context, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor())));
        Assert.Equal(0, body.Length);
        Assert.True(body.CanWrite);
    }

    /// <summary>
    /// Checks cancellation during multipart writing propagates without closing caller-owned response stream.
    /// </summary>
    /// <param name="units">Whether response contains a mapping attachment.</param>
    /// <returns>Task completing after interrupted-write assertions.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Writer_PropagatesCancellationDuringResponse(bool units)
    {
        using var cancellation = new CancellationTokenSource();
        using var body = new CancellingStream(cancellation);
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        context.Response.Body = body;
        IActionResult result = units ? new MultipartUnitsResult(new FileResponse(FileMetadata.Create("word"), []), []) : new MultipartFileResult(new([1, 2], "application/octet-stream", []), "file.xlsx");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result.ExecuteResultAsync(new ActionContext(context, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor())));
        Assert.True(body.Length > 0);
        Assert.True(body.CanWrite);
    }

    /// <summary>
    /// Cancels response after first asynchronous write.
    /// </summary>
    /// <param name="cancellation">Request cancellation source.</param>
    private sealed class CancellingStream(CancellationTokenSource cancellation) : MemoryStream
    {

        /// <summary>
        /// Writes first part boundary then requests cancellation.
        /// </summary>
        /// <param name="buffer">Response bytes.</param>
        /// <param name="cancellationToken">Active request token.</param>
        /// <returns>Completed write task before subsequent writes observe cancellation.</returns>
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            base.Write(buffer.Span);
            cancellation.Cancel();
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Routes legacy writes through deterministic cancellation boundary.
        /// </summary>
        /// <param name="buffer">Response buffer.</param>
        /// <param name="offset">First source byte.</param>
        /// <param name="count">Number of bytes.</param>
        /// <param name="cancellationToken">Active request token.</param>
        /// <returns>Write task.</returns>
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }
}
