using System.Buffers;
using System.Text;
using FileHandler.Api.Common;

namespace FileHandler.Api.Modules.Markdown;

internal static class MarkdownSourceReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static async Task<(MarkdownSource? Source, FileError? Error)> ReadAsync(
        Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            using var output = new MemoryStream();
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0) break;
                if (output.Length + read > maxBytes)
                    return (null, new("file_too_large", $"Tệp vượt giới hạn {maxBytes} byte."));
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            var bytes = output.ToArray();
            try
            {
                var source = DecodeUtf8(bytes);
                return (source, null);
            }
            catch (DecoderFallbackException)
            {
                return (null, new("invalid_encoding", "Tệp phải dùng UTF-8 hợp lệ."));
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    internal static MarkdownSource DecodeUtf8(byte[] bytes)
    {
        var hasBom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        var offset = hasBom ? Encoding.UTF8.Preamble.Length : 0;
        var text = StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
        return new(bytes, text, hasBom, BuildLineMap(text));
    }

    internal static LineMap BuildLineMap(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') { starts.Add(i + 2); i++; }
            else if (text[i] is '\r' or '\n') starts.Add(i + 1);
        }
        return new(starts.ToArray());
    }

    internal static byte[] Encode(string text, bool bom)
    {
        var content = StrictUtf8.GetBytes(text);
        if (!bom) return content;
        var result = new byte[Encoding.UTF8.Preamble.Length + content.Length];
        Encoding.UTF8.Preamble.CopyTo(result.AsSpan());
        content.CopyTo(result, Encoding.UTF8.Preamble.Length);
        return result;
    }
}
