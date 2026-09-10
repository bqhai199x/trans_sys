using System.Globalization;

namespace FileHandler.Api.Modules.Markdown;

internal sealed class MarkerAllocationContext
{
    private readonly HashSet<int> _reserved;
    private int _next = 1;
    public MarkerAllocationContext(HashSet<int> reserved) => _reserved = reserved;
    public int AllocateId()
    {
        while (_reserved.Contains(_next))
        {
            if (_next == int.MaxValue) throw new InvalidOperationException("Không còn marker ID khả dụng.");
            _next++;
        }
        var value = _next;
        _reserved.Add(value);
        if (_next < int.MaxValue) _next++;
        return value;
    }
}

internal static class MarkdownMarkerCodec
{
    public static HashSet<int> FindReservedIds(string source)
    {
        var result = new HashSet<int>();
        for (var i = 0; i < source.Length; i++)
        {
            if (!source.AsSpan(i).StartsWith("<keepme", StringComparison.Ordinal)) continue;
            var p = i + 7;
            var start = p;
            while (p < source.Length && char.IsAsciiDigit(source[p])) p++;
            if (p == start || p >= source.Length || (source[p] != '>' && !(source[p] == '/' && p + 1 < source.Length && source[p + 1] == '>'))) continue;
            var digits = source.AsSpan(start, p - start);
            if (digits.Length <= 10 && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
                result.Add(id);
        }
        return result;
    }

    public static string Open(int id) => $"<keepme{id}>";
    public static string Close(int id) => $"<keepme{id}/>";

    public static IReadOnlyList<MarkerToken> Parse(string value)
    {
        var tokens = new List<MarkerToken>();
        var textStart = 0;
        for (var i = 0; i < value.Length;)
        {
            if (!value.AsSpan(i).StartsWith("<keepme", StringComparison.Ordinal)) { i++; continue; }
            var p = i + 7;
            var digitStart = p;
            while (p < value.Length && char.IsAsciiDigit(value[p])) p++;
            if (p == digitStart) { i++; continue; }
            var closing = p < value.Length && value[p] == '/';
            var end = closing ? p + 2 : p + 1;
            if (end > value.Length || value[end - 1] != '>') { i++; continue; }
            if (!int.TryParse(value.AsSpan(digitStart, p - digitStart), out var id) || id <= 0) { i++; continue; }
            if (i > textStart) tokens.Add(new(false, 0, value[textStart..i], false));
            tokens.Add(new(true, id, value[i..end], closing));
            i = end;
            textStart = i;
        }
        if (textStart < value.Length) tokens.Add(new(false, 0, value[textStart..], false));
        return tokens;
    }
}

internal sealed record MarkerToken(bool IsMarker, int Id, string Value, bool IsClosing);
