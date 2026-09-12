using System.Buffers;
using System.Text;
using FileHandler.Api.Diagnostics;

namespace FileHandler.Api.Common;

/// <summary>
/// Reads size-limited UTF-8 streams and preserves optional BOM.
/// </summary>
internal static class Utf8TextReader
{

    /// <summary>
    /// Buffer size in bytes for source reads.
    /// </summary>
    private const int RentBufferSize = 81920;

    /// <summary>
    /// UTF-8 codec rejecting malformed Unicode.
    /// </summary>
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// Reads actual stream bytes within configured limit and decodes strict UTF-8.
    /// </summary>
    /// <param name="stream">Source stream, left open after reading.</param>
    /// <param name="maxBytes">Maximum source bytes including BOM.</param>
    /// <param name="cancellationToken">Token for cancelling reads.</param>
    /// <returns>Decoded source, or size or encoding error without partial content.</returns>
    internal static Task<(Utf8TextSource? Source, FileError? Error)> ReadAsync(Stream stream, long maxBytes, CancellationToken cancellationToken) =>
        DebugTrace.TraceAsync<(Utf8TextSource? Source, FileError? Error)>("Utf8TextReader", "ReadAsync", () => new { stream, maxBytes, cancellationToken }, async trace =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var buffer = ArrayPool<byte>.Shared.Rent(RentBufferSize);
            try
            {
                using var output = new MemoryStream();
                while (true)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read == 0)
                        break;
                    if (output.Length + read > maxBytes)
                    {
                        trace.State("bytesRead", () => output.Length + read);
                        return (null, new FileError("file_too_large", $"Tệp vượt giới hạn {maxBytes} byte."));
                    }
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                trace.State("bytesRead", () => output.Length);
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!output.TryGetBuffer(out var segment))
                        segment = new ArraySegment<byte>(output.ToArray());
                    var bytes = segment.Count == segment.Array!.Length ? segment.Array : segment.AsSpan().ToArray();
                    return (DecodeUtf8(bytes), null);
                }
                catch (DecoderFallbackException)
                {
                    return (null, new FileError("invalid_encoding", "Tệp phải dùng UTF-8 hợp lệ."));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        });

    /// <summary>
    /// Decodes strict UTF-8 without changing newlines or Unicode normalization.
    /// </summary>
    /// <param name="bytes">Original source bytes.</param>
    /// <returns>Original bytes, decoded text and BOM flag.</returns>
    /// <exception cref="DecoderFallbackException">Source bytes are not valid UTF-8.</exception>
    internal static Utf8TextSource DecodeUtf8(byte[] bytes) =>
        DebugTrace.Trace("Utf8TextReader", "DecodeUtf8", () => new { bytes }, trace =>
        {
            var hasBom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
            var offset = hasBom ? Encoding.UTF8.Preamble.Length : 0;
            trace.State("hasBom", () => hasBom);
            var text = StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
            return new Utf8TextSource(bytes, text, hasBom);
        });

    /// <summary>
    /// Encodes strict UTF-8 with optional BOM.
    /// </summary>
    /// <param name="text">Text to encode.</param>
    /// <param name="bom">Whether output includes UTF-8 BOM.</param>
    /// <returns>Encoded bytes including requested BOM.</returns>
    /// <exception cref="EncoderFallbackException">Text contains malformed UTF-16.</exception>
    internal static byte[] Encode(string text, bool bom) =>
        DebugTrace.Trace("Utf8TextReader", "Encode", () => new { text, bom }, _ =>
        {
            var offset = bom ? Encoding.UTF8.Preamble.Length : 0;
            var bytes = new byte[checked(StrictUtf8.GetByteCount(text) + offset)];
            if (bom)
                Encoding.UTF8.Preamble.CopyTo(bytes);
            StrictUtf8.GetBytes(text.AsSpan(), bytes.AsSpan(offset));
            return bytes;
        });

    /// <summary>
    /// Counts strict UTF-8 bytes without allocating encoded content or adding BOM.
    /// </summary>
    /// <param name="text">Text span to measure.</param>
    /// <returns>UTF-8 byte count excluding BOM.</returns>
    /// <exception cref="EncoderFallbackException">Text contains malformed UTF-16.</exception>
    internal static int GetByteCount(ReadOnlySpan<char> text)
    {
        var length = text.Length;
        using var trace = DebugTrace.Enter("Utf8TextReader", "GetByteCount", () => new { length });
        try
        {
            return trace.Return(StrictUtf8.GetByteCount(text));
        }
        catch (Exception error)
        {
            trace.Error(error);
            throw;
        }
    }
}
