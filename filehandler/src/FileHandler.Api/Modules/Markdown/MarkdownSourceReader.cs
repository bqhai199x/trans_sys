using FileHandler.Api.Common;

namespace FileHandler.Api.Modules.Markdown;

/// <summary>
/// Reads, validates, decodes, and encodes Markdown UTF-8 source streams.
/// </summary>
internal static class MarkdownSourceReader
{

    /// <summary>
    /// Reads size-limited stream and validates its UTF-8 encoding.
    /// </summary>
    /// <param name="stream">Source stream, left open after reading.</param>
    /// <param name="maxBytes">Maximum source byte count.</param>
    /// <param name="cancellationToken">Token for cancelling this operation.</param>
    /// <returns>Task containing decoded source or size or encoding error.</returns>
    public static async Task<(MarkdownSource? Source, FileError? Error)> ReadAsync(Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        var (source, error) = await Utf8TextReader.ReadAsync(stream, maxBytes, cancellationToken);
        return source is null ? (null, error) : (new MarkdownSource(source.Bytes, source.Text, source.HasBom, BuildLineMap(source.Text)), null);
    }

    /// <summary>
    /// Decodes strict UTF-8 and records BOM and line offsets.
    /// </summary>
    /// <param name="bytes">Original source bytes.</param>
    /// <returns>Decoded source with original bytes, BOM flag, and line offsets.</returns>
    internal static MarkdownSource DecodeUtf8(byte[] bytes)
    {
        var source = Utf8TextReader.DecodeUtf8(bytes);
        return new MarkdownSource(bytes, source.Text, source.HasBom, BuildLineMap(source.Text));
    }

    /// <summary>
    /// Indexes line starts across CR, LF, and CRLF line endings.
    /// </summary>
    /// <param name="text">Text to process.</param>
    /// <returns>Zero-based line start offsets.</returns>
    internal static LineMap BuildLineMap(string text)
    {
        var starts = new List<int>
        {
            0
        };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                starts.Add(i + 2);
                i++;
            }
            else if (text[i] is '\r' or '\n')
                starts.Add(i + 1);
        }

        return new LineMap(starts.ToArray());
    }

    /// <summary>
    /// Encodes text as strict UTF-8 with optional BOM.
    /// </summary>
    /// <param name="text">Text to process.</param>
    /// <param name="bom">Whether to include UTF-8 BOM.</param>
    /// <returns>UTF-8 bytes with BOM when requested.</returns>
    internal static byte[] Encode(string text, bool bom) => Utf8TextReader.Encode(text, bom);
}
