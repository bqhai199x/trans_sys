using System.Text;

namespace FileHandler.Api.Common;

/// <summary>
/// Shared wire syntax for translated runs, movable anchors and fixed boundaries.
/// </summary>
internal static class TranslationTokenSyntax
{

    /// <summary>
    /// Detects reserved wire syntax, including malformed token attempts.
    /// </summary>
    /// <param name="text">Source text to inspect.</param>
    /// <returns>True when text contains reserved token prefixes.</returns>
    internal static bool IsStructured(string text) => text.Contains("<ox:", StringComparison.Ordinal) || text.Contains("</ox:", StringComparison.Ordinal);

    /// <summary>
    /// Wraps literal reserved syntax so callers can distinguish document text from tokens.
    /// </summary>
    /// <param name="text">Original literal text.</param>
    /// <returns>Unchanged text or one escaped run containing reserved syntax.</returns>
    internal static string EncodeLiteral(string text)
    {
        if (!IsStructured(text)) return text;
        var builder = new StringBuilder(Open("r0"));
        AppendEscaped(builder, text);
        return builder.Append(Close("r0")).ToString();
    }

    /// <summary>
    /// Encodes source parts and inserts fixed boundaries between ownership regions.
    /// </summary>
    /// <param name="parts">Source runs, movable anchors and fixed barriers.</param>
    /// <param name="scopes">Internal owner identity for each source part.</param>
    /// <param name="compact">Whether to hide edge boundaries and collapse adjacent boundaries.</param>
    /// <returns>Self-describing wire text with unique synthetic boundary IDs.</returns>
    internal static string Encode(IReadOnlyList<TranslationTokenPart> parts, IReadOnlyList<string> scopes, bool compact = true)
    {
        var nextBoundary = parts.Where(p => p.Id[0] == 'b').Select(p => int.Parse(p.Id.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(-1).Max() + 1;
        var expanded = new List<TranslationTokenPart>();
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (i > 0 && scopes[i] != scopes[i - 1] && parts[i - 1].Id[0] != 'b' && part.Id[0] != 'b')
                expanded.Add(new("b" + (nextBoundary++).ToString(System.Globalization.CultureInfo.InvariantCulture), null));
            expanded.Add(part);
        }
        var builder = new StringBuilder();
        foreach (var part in compact ? Compact(expanded) : expanded)
        {
            if (part.Text is null) builder.Append(Anchor(part.Id));
            else
            {
                builder.Append(Open(part.Id));
                AppendEscaped(builder, part.Text);
                builder.Append(Close(part.Id));
            }
        }
        return builder.ToString();
    }

    /// <summary>
    /// Hides fixed edge objects and represents each interior boundary group once.
    /// </summary>
    /// <param name="parts">Complete source sequence including ownership boundaries.</param>
    /// <returns>Public sequence retaining original visible identifiers.</returns>
    internal static IReadOnlyList<TranslationTokenPart> Compact(IReadOnlyList<TranslationTokenPart> parts)
    {
        var first = 0;
        var last = parts.Count - 1;
        while (first <= last && parts[first].Id[0] == 'b') first++;
        while (last >= first && parts[last].Id[0] == 'b') last--;
        var result = new List<TranslationTokenPart>();
        for (var i = first; i <= last; i++)
            if (parts[i].Id[0] != 'b' || result[^1].Id[0] != 'b') result.Add(parts[i]);
        return result;
    }

    /// <summary>
    /// Restores hidden fixed objects around validated translated public parts.
    /// </summary>
    /// <param name="source">Complete source sequence before compaction.</param>
    /// <param name="translated">Validated compact translation sequence.</param>
    /// <returns>Translated sequence with all original fixed objects restored.</returns>
    internal static IReadOnlyList<TranslationTokenPart> Expand(IReadOnlyList<TranslationTokenPart> source, IReadOnlyList<TranslationTokenPart> translated)
    {
        var prefix = source.TakeWhile(p => p.Id[0] == 'b').ToArray();
        var groups = new Dictionary<string, List<TranslationTokenPart>>();
        var suffix = new List<TranslationTokenPart>();
        for (var i = prefix.Length; i < source.Count; i++)
        {
            if (source[i].Id[0] != 'b') continue;
            var group = new List<TranslationTokenPart>();
            while (i < source.Count && source[i].Id[0] == 'b') group.Add(source[i++]);
            if (i == source.Count) suffix = group;
            else groups[group[0].Id] = group;
            i--;
        }
        var result = new List<TranslationTokenPart>(prefix);
        foreach (var part in translated)
        {
            if (groups.TryGetValue(part.Id, out var group)) result.AddRange(group);
            else result.Add(part);
        }
        result.AddRange(suffix);
        return result;
    }

    /// <summary>
    /// Builds canonical opening run token.
    /// </summary>
    /// <param name="id">Scoped run identifier, including r prefix.</param>
    /// <returns>Opening run token.</returns>
    internal static string Open(string id) => $"<ox:{id}>";

    /// <summary>
    /// Builds canonical closing run token.
    /// </summary>
    /// <param name="id">Scoped run identifier, including r prefix.</param>
    /// <returns>Closing run token.</returns>
    internal static string Close(string id) => $"</ox:{id}>";

    /// <summary>
    /// Builds canonical protected anchor token.
    /// </summary>
    /// <param name="id">Protected anchor or boundary identifier, including k or b prefix.</param>
    /// <returns>Self-closing protected anchor.</returns>
    internal static string Anchor(string id) => $"<ox:{id}/>";

    /// <summary>
    /// Appends escaped literal text inside structured run tokens.
    /// </summary>
    /// <param name="builder">Destination builder.</param>
    /// <param name="text">Unescaped literal text.</param>
    /// <returns>No return value.</returns>
    internal static void AppendEscaped(StringBuilder builder, string text)
    {
        foreach (var character in text)
        {
            if (character is '\\' or '<')
                builder.Append('\\');
            builder.Append(character);
        }
    }

    /// <summary>
    /// Reads escaped text until expected closing run token.
    /// </summary>
    /// <param name="value">Structured translation.</param>
    /// <param name="offset">Current offset, advanced past closing token on success.</param>
    /// <param name="closing">Expected canonical closing token.</param>
    /// <param name="text">Decoded literal text.</param>
    /// <returns>True when escapes and closing token are valid.</returns>
    internal static bool TryReadText(string value, ref int offset, string closing, out string text)
    {
        var builder = new StringBuilder();
        text = string.Empty;
        while (offset < value.Length)
        {
            var character = value[offset++];
            if (character == '\\')
            {
                if (offset == value.Length || value[offset] is not ('\\' or '<'))
                    return false;
                builder.Append(value[offset++]);
            }
            else if (character == '<')
            {
                if (!value.AsSpan(offset - 1).StartsWith(closing, StringComparison.Ordinal))
                    return false;
                offset += closing.Length - 1;
                text = builder.ToString();
                return true;
            }
            else
                builder.Append(character);
        }
        return false;
    }
}
