using FileHandler.Api.Common;
using FileHandler.Api.Diagnostics;

namespace FileHandler.Api.Modules.PlainText;

/// <summary>
/// Locates paragraphs separated by empty or whitespace-only physical lines.
/// </summary>
internal static class PlainTextSegmenter
{

    /// <summary>
    /// Scans CR, LF and CRLF lines without normalizing source content.
    /// </summary>
    /// <param name="text">Decoded source text without BOM.</param>
    /// <param name="maxUnits">Maximum paragraph count.</param>
    /// <param name="cancellationToken">Token for cancelling scans, including long lines.</param>
    /// <returns>Ordered paragraph spans, or quota error with no partial units.</returns>
    internal static (IReadOnlyList<PlainTextUnit> Units, FileError? Error) Segment(string text, int maxUnits, CancellationToken cancellationToken) =>
        DebugTrace.Trace<(IReadOnlyList<PlainTextUnit> Units, FileError? Error)>("PlainTextSegmenter", "Segment", () => new { text, maxUnits, cancellationToken }, trace =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var units = new List<PlainTextUnit>();
            var offset = 0;
            var line = 1;
            var paragraphStart = -1;
            var paragraphEnd = 0;
            var firstLine = 0;
            var lastLine = 0;
            while (offset < text.Length)
            {
                var lineStart = offset;
                var blank = true;
                while (offset < text.Length && text[offset] is not ('\r' or '\n'))
                {
                    if ((offset & 4095) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    if (!char.IsWhiteSpace(text[offset]))
                        blank = false;
                    offset++;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!blank)
                {
                    if (paragraphStart < 0)
                    {
                        if (units.Count >= maxUnits)
                        {
                            trace.State("attemptedUnitCount", () => units.Count + 1);
                            trace.State("line", () => line);
                            return ([], new FileError("too_many_units", $"Tệp vượt giới hạn {maxUnits} đoạn."));
                        }
                        paragraphStart = lineStart;
                        firstLine = line;
                    }
                    paragraphEnd = offset;
                    lastLine = line;
                }
                else if (paragraphStart >= 0)
                {
                    units.Add(new(paragraphStart, paragraphEnd, new(firstLine, lastLine)));
                    using var item = DebugTrace.Item(units.Count);
                    item.State("unit", () => units[^1]);
                    paragraphStart = -1;
                }

                if (offset < text.Length)
                    offset += text[offset] == '\r' && offset + 1 < text.Length && text[offset + 1] == '\n' ? 2 : 1;
                line++;
            }

            if (paragraphStart >= 0)
            {
                units.Add(new(paragraphStart, paragraphEnd, new(firstLine, lastLine)));
                using var item = DebugTrace.Item(units.Count);
                item.State("unit", () => units[^1]);
            }
            trace.State("unitCount", () => units.Count);
            return (units, null);
        });
}
