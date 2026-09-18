using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig.Syntax;

namespace FileHandler.Api.Modules.Markdown;

/// <summary>
/// Extracts single-line flowchart labels while preserving diagram syntax and source offsets.
/// </summary>
internal static class MermaidFlowchartCodec
{

    /// <summary>
    /// Supported flowchart declarations and orientations.
    /// </summary>
    private static readonly Regex Header = new(@"^\s*(?:flowchart|graph)\s+(?:TB|TD|BT|RL|LR)\b[ \t]*;?", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Statements whose arguments are configuration rather than visible labels.
    /// </summary>
    private static readonly Regex Configuration = new(@"^\s*(?:style|classDef|class|click|linkStyle|direction|end|accTitle|accDescr)\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Node identifiers excluding arrow punctuation at identifier boundaries.
    /// </summary>
    private static readonly Regex Node = new(@"\G[\p{L}\p{N}_]+(?:[.-][\p{L}\p{N}_]+)*[ \t]*", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Pipe labels and inline labels on solid, dotted and thick edges.
    /// </summary>
    private static readonly Regex Edge = new("""\G(?:(?:<?(?:-+\.+-+|-{2,}|={2,})[>ox]?)[ \t]*\|(?<label>"[^"]*"|[^|\r\n]*)\||-\.[ \t]+(?<label>"[^"]*"|[^\r\n]*?)[ \t]+\.-+>|--[ \t]+(?<label>"[^"]*"|[^\r\n]*?)[ \t]+--+>|==[ \t]+(?<label>"[^"]*"|[^\r\n]*?)[ \t]+==+>)""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Shape delimiters ordered from longest to shortest opener.
    /// </summary>
    private static readonly (string Open, string Close)[] Shapes =
    [
        ("(((", ")))"), ("((", "))"), ("([", "])"), ("[[", "]]"), ("[(", ")]"),
        ("{{", "}}"), ("[/", "/]"), ("[\\", "\\]"),
        ("[", "]"), ("(", ")"), ("{", "}"), (">", "]")
    ];

    /// <summary>
    /// Mermaid decimal and named entity references.
    /// </summary>
    private static readonly Regex Entity = new(@"#(?<entity>[0-9]+|[A-Za-z]+);", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Locates labels in Mermaid flowchart fences; other diagram types remain protected.
    /// </summary>
    /// <param name="block">Parsed fenced code block.</param>
    /// <param name="cancellationToken">Token for cancelling extraction.</param>
    /// <returns>Ordered label spans with decoded text.</returns>
    internal static IEnumerable<MermaidLabel> Extract(FencedCodeBlock block, CancellationToken cancellationToken)
    {
        if (!string.Equals(block.Info, "mermaid", StringComparison.OrdinalIgnoreCase)) yield break;
        var started = false;
        for (var lineIndex = 0; lineIndex < block.Lines.Count; lineIndex++)
        {
            var line = block.Lines.Lines[lineIndex];
            cancellationToken.ThrowIfCancellationRequested();
            var text = line.Slice.ToString();
            if (string.IsNullOrWhiteSpace(text) || text.TrimStart().StartsWith("%%", StringComparison.Ordinal)) continue;
            var offset = 0;
            if (!started)
            {
                var header = Header.Match(text);
                if (!header.Success) yield break;
                started = true;
                offset = header.Length;
            }
            if (Configuration.IsMatch(text)) continue;
            while (offset < text.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (text.AsSpan(offset).StartsWith("%%", StringComparison.Ordinal)) break;
                if (text[offset] == ';' && Configuration.IsMatch(text[(offset + 1)..])) break;
                var edge = offset > 0 && "-.=<>".Contains(text[offset - 1]) ? Match.Empty : Edge.Match(text, offset);
                if (edge.Success)
                {
                    var label = edge.Groups["label"];
                    var decoded = MakeLabel(text, label.Index, label.Index + label.Length, line.Slice.Start);
                    if (decoded is not null) yield return decoded;
                    offset += edge.Length;
                    continue;
                }
                var node = Node.Match(text, offset);
                if (node.Success)
                {
                    var start = node.Index + node.Length;
                    offset = start;
                    foreach (var shape in Shapes)
                    {
                        if (!text.AsSpan(start).StartsWith(shape.Open, StringComparison.Ordinal)) continue;
                        start += shape.Open.Length;
                        var end = FindEnd(text, start, shape.Close);
                        if (end < 0) { offset = text.Length; break; }
                        var decoded = MakeLabel(text, start, end, line.Slice.Start);
                        if (decoded is not null) yield return decoded;
                        offset = end + shape.Close.Length;
                        break;
                    }
                    continue;
                }
                if (text[offset] == '"')
                {
                    var end = text.IndexOf('"', offset + 1);
                    offset = end < 0 ? text.Length : end + 1;
                }
                else offset++;
            }
        }
    }

    /// <summary>
    /// Finds shape closing delimiter without interpreting delimiters inside quoted labels.
    /// </summary>
    /// <param name="text">Current source line.</param>
    /// <param name="start">Label start offset.</param>
    /// <param name="close">Shape closing delimiter.</param>
    /// <returns>Closing delimiter offset, or minus one when incomplete.</returns>
    private static int FindEnd(string text, int start, string close)
    {
        var quoted = false;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '"') quoted = !quoted;
            if (!quoted && text.AsSpan(i).StartsWith(close, StringComparison.Ordinal)) return i;
        }
        return -1;
    }

    /// <summary>
    /// Builds label binding, leaving HTML and Mermaid Markdown strings protected.
    /// </summary>
    /// <param name="text">Source line text.</param>
    /// <param name="start">Inclusive label offset within line.</param>
    /// <param name="end">Exclusive label offset within line.</param>
    /// <param name="sourceOffset">Absolute source offset of line slice.</param>
    /// <returns>Decoded label binding, or null for empty or unsupported labels.</returns>
    private static MermaidLabel? MakeLabel(string text, int start, int end, int sourceOffset)
    {
        while (start < end && char.IsWhiteSpace(text[start])) start++;
        while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
        var value = text[start..end];
        if (value.StartsWith('"') && value.EndsWith('"') && value.Length >= 2) value = value[1..^1];
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['<', '`', '"']) >= 0) return null;
        value = Entity.Replace(value, match =>
        {
            var entity = match.Groups["entity"].Value;
            if (int.TryParse(entity, NumberStyles.None, CultureInfo.InvariantCulture, out var code) && Rune.IsValid(code))
                return char.ConvertFromUtf32(code);
            return WebUtility.HtmlDecode("&" + entity + ";") is { } decoded && decoded != "&" + entity + ";" ? decoded : match.Value;
        });
        return new(sourceOffset + start, sourceOffset + end, value);
    }

    /// <summary>
    /// Quotes plain translated labels and encodes punctuation using Mermaid decimal entities.
    /// </summary>
    /// <param name="text">Validated single-line translated label.</param>
    /// <returns>Quoted label safe for node and edge delimiters.</returns>
    internal static string Encode(string text)
    {
        var output = new StringBuilder("\"");
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune) || rune.Value == ' ') output.Append(rune);
            else output.Append('#').Append(rune.Value.ToString(CultureInfo.InvariantCulture)).Append(';');
        }
        return output.Append('"').ToString();
    }
}

/// <summary>
/// Visible flowchart label bound to exact source content span.
/// </summary>
/// <param name="Start">Inclusive source offset including optional quotes.</param>
/// <param name="End">Exclusive source offset including optional quotes.</param>
/// <param name="Text">Decoded visible text.</param>
internal sealed record MermaidLabel(int Start, int End, string Text);
