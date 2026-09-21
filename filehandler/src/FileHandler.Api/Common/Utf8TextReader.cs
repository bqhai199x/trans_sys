using System.Buffers;
using System.Text;

namespace FileHandler.Api.Common;

/// <summary>
/// Reads size-limited UTF-8 streams and preserves optional BOM.
/// </summary>
internal static class Utf8TextReader
{

    /// <summary>
    /// Buffer size in bytes for source reads.
    /// </summary>
    private const int RentBufferSize = 65536;

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
    internal static async Task<(Utf8TextSource? Source, FileError? Error)> ReadAsync(Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var buffer = ArrayPool<byte>.Shared.Rent(RentBufferSize);
        try
        {
            using var output = new MemoryStream();
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, RentBufferSize), cancellationToken);
                if (read == 0)
                    break;
                if (output.Length + read > maxBytes)
                {
                    return (null, new FileError("file_too_large", ProcessingMessages.InputSizeLimit(maxBytes)));
                }
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

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
                return (null, new FileError("invalid_encoding", ProcessingMessages.InvalidEncoding));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>
    /// Decodes strict UTF-8 without changing newlines or Unicode normalization.
    /// </summary>
    /// <param name="bytes">Original source bytes.</param>
    /// <returns>Original bytes, decoded text and BOM flag.</returns>
    /// <exception cref="DecoderFallbackException">Source bytes are not valid UTF-8.</exception>
    internal static Utf8TextSource DecodeUtf8(byte[] bytes)
    {
        var hasBom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        var offset = hasBom ? Encoding.UTF8.Preamble.Length : 0;
        var text = StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
        return new Utf8TextSource(bytes, text, hasBom);
    }

    /// <summary>
    /// Encodes strict UTF-8 with optional BOM.
    /// </summary>
    /// <param name="text">Text to encode.</param>
    /// <param name="bom">Whether output includes UTF-8 BOM.</param>
    /// <returns>Encoded bytes including requested BOM.</returns>
    /// <exception cref="EncoderFallbackException">Text contains malformed UTF-16.</exception>
    internal static byte[] Encode(string text, bool bom)
    {
        var offset = bom ? Encoding.UTF8.Preamble.Length : 0;
        var bytes = new byte[checked(StrictUtf8.GetByteCount(text) + offset)];
        if (bom)
            Encoding.UTF8.Preamble.CopyTo(bytes);
        StrictUtf8.GetBytes(text.AsSpan(), bytes.AsSpan(offset));
        return bytes;
    }

    /// <summary>
    /// Counts strict UTF-8 bytes without allocating encoded content or adding BOM.
    /// </summary>
    /// <param name="text">Text span to measure.</param>
    /// <returns>UTF-8 byte count excluding BOM.</returns>
    /// <exception cref="EncoderFallbackException">Text contains malformed UTF-16.</exception>
    internal static int GetByteCount(ReadOnlySpan<char> text) =>
        StrictUtf8.GetByteCount(text);
}
