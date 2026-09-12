using System.Globalization;
using FileHandler.Api.Diagnostics;

namespace FileHandler.Api.Modules.Markdown;

internal sealed class MarkerAllocationContext
{

    /// <summary>
    /// Marker IDs unavailable for allocation.
    /// </summary>
    private readonly HashSet<int> _reserved;

    /// <summary>
    /// Next candidate marker ID.
    /// </summary>
    private int _next = 1;

    /// <summary>
    /// Creates allocator with reserved marker IDs.
    /// </summary>
    /// <param name="reserved">Marker IDs already used in source text.</param>
    public MarkerAllocationContext(HashSet<int> reserved) => _reserved = reserved;

    /// <summary>
    /// Allocates marker ID that does not collide with reserved IDs.
    /// </summary>
    /// <returns>Next available positive marker ID.</returns>
    public int AllocateId()
    {
        using var trace = DebugTrace.Enter("MarkerAllocationContext", "AllocateId", () => new { next = _next, reservedCount = _reserved.Count });
        try
        {
            while (_reserved.Contains(_next))
            {
                if (_next == int.MaxValue)
                    throw new InvalidOperationException("Không còn marker ID khả dụng.");
                _next++;
            }

            var value = _next;
            _reserved.Add(value);
            if (_next < int.MaxValue)
                _next++;
            trace.State("next", () => _next);
            return trace.Return<int>(value);
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
    }
}

internal static class MarkdownMarkerCodec
{

    /// <summary>
    /// Prefix identifying preservation marker tags.
    /// </summary>
    internal const string MarkerPrefix = "<keepme";

    /// <summary>
    /// Finds marker IDs already present in source text.
    /// </summary>
    /// <param name="source">Original Markdown source.</param>
    /// <returns>Marker IDs found in source text.</returns>
    public static HashSet<int> FindReservedIds(string source) =>
        DebugTrace.Trace("MarkdownMarkerCodec", "FindReservedIds", () => new { source }, _ =>
        {
            var result = new HashSet<int>();
            var i = 0;
            while (i < source.Length)
            {
                var markerPos = source.IndexOf(MarkerPrefix, i, StringComparison.Ordinal);
                if (markerPos < 0)
                    break;

                var p = markerPos + MarkerPrefix.Length;
                var start = p;
                while (p < source.Length && char.IsAsciiDigit(source[p]))
                    p++;

                if (p > start && p < source.Length && (source[p] == '>' || (source[p] == '/' && p + 1 < source.Length && source[p + 1] == '>')))
                {
                    var digits = source.AsSpan(start, p - start);
                    if (digits.Length <= 10 && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
                        result.Add(id);
                }

                i = markerPos + 1;
            }

            return result;
        });

    /// <summary>
    /// Builds canonical opening marker for ID.
    /// </summary>
    /// <param name="id">Marker ID.</param>
    /// <returns>Canonical opening marker.</returns>
    public static string Open(int id) =>
        DebugTrace.Trace("MarkdownMarkerCodec", "Open", () => new { id }, _ => $"{MarkerPrefix}{id}>");

    /// <summary>
    /// Builds canonical closing marker for ID.
    /// </summary>
    /// <param name="id">Marker ID.</param>
    /// <returns>Canonical closing marker.</returns>
    public static string Close(int id) =>
        DebugTrace.Trace("MarkdownMarkerCodec", "Close", () => new { id }, _ => $"{MarkerPrefix}{id}/>");

    /// <summary>
    /// Splits text into literal and marker tokens.
    /// </summary>
    /// <param name="value">Text containing literal segments and markers.</param>
    /// <returns>Literal and marker tokens in source order.</returns>
    public static IReadOnlyList<MarkerToken> Parse(string value) =>
        DebugTrace.Trace("MarkdownMarkerCodec", "Parse", () => new { value }, _ =>
        {
            var tokens = new List<MarkerToken>();
            var textStart = 0;
            var i = 0;
            while (i < value.Length)
            {
                var markerPos = value.IndexOf(MarkerPrefix, i, StringComparison.Ordinal);
                if (markerPos < 0)
                    break;

                var p = markerPos + MarkerPrefix.Length;
                var digitStart = p;
                while (p < value.Length && char.IsAsciiDigit(value[p]))
                    p++;

                if (p == digitStart)
                {
                    i = markerPos + 1;
                    continue;
                }

                var closing = p < value.Length && value[p] == '/';
                var end = closing ? p + 2 : p + 1;
                if (end > value.Length || value[end - 1] != '>')
                {
                    i = markerPos + 1;
                    continue;
                }

                if (!int.TryParse(value.AsSpan(digitStart, p - digitStart), out var id) || id <= 0)
                {
                    i = markerPos + 1;
                    continue;
                }

                if (markerPos > textStart)
                    tokens.Add(new(false, 0, value[textStart..markerPos], false));

                tokens.Add(new(true, id, value[markerPos..end], closing));
                i = end;
                textStart = i;
            }

            if (textStart < value.Length)
                tokens.Add(new(false, 0, value[textStart..], false));

            return tokens;
        });
}

/// <summary>
/// Literal text segment or parsed preservation marker.
/// </summary>
/// <param name="IsMarker">Whether token is marker.</param>
/// <param name="Id">Parsed marker ID, or zero for literal text.</param>
/// <param name="Value">Original token text.</param>
/// <param name="IsClosing">Whether token closes marker pair.</param>
internal sealed record MarkerToken(bool IsMarker, int Id, string Value, bool IsClosing);
