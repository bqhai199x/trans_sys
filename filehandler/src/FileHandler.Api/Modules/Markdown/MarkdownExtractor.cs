using System.Text;
using FileHandler.Api.Common;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace FileHandler.Api.Modules.Markdown;

internal sealed class MarkdownExtractor
{
    private readonly MarkdownPipeline _pipeline;
    public MarkdownExtractor(MarkdownPipeline pipeline) => _pipeline = pipeline;

    public MarkdownExtraction Extract(MarkdownSource source, int maxUnits, CancellationToken cancellationToken)
    {
        var document = ParseDocument(source.Text);
        var allocator = new MarkerAllocationContext(MarkdownMarkerCodec.FindReservedIds(source.Text));
        var units = new List<MarkdownUnit>();

        foreach (var block in document.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (block is not LeafBlock { Inline: not null } leaf) continue;
            if (block is CodeBlock) continue;
            var encoded = EncodeContainer(leaf.Inline, source.Text, allocator);
            if (encoded is null || string.IsNullOrWhiteSpace(RemoveMarkers(encoded.Text))) continue;
            var (newlineReplacement, hasSoftBreak) = FindNewlinePolicy(leaf.Inline, source.Text);
            units.Add(new(encoded.Start, encoded.End, encoded.Text, encoded.Markers, source.Lines.GetRange(encoded.Start, encoded.End), leaf is HeadingBlock, newlineReplacement, hasSoftBreak));
            if (units.Count > maxUnits)
                return new(source, [], [new("too_many_units", $"Tài liệu vượt giới hạn {maxUnits} đơn vị dịch.")]);
        }
        var hasInternalLinks = document.Descendants<LinkInline>().Any(x => x.Url?.StartsWith('#') == true);
        return new(source, units, [], hasInternalLinks);
    }

    internal MarkdownDocument ParseDocument(string text) => Markdig.Markdown.Parse(text, _pipeline);

    internal IReadOnlyList<FileError> ValidateStructure(string original, string candidate)
    {
        try
        {
            var before = BuildStructureSignature(ParseDocument(original), original);
            var after = BuildStructureSignature(ParseDocument(candidate), candidate);
            return before.SequenceEqual(after, StringComparer.Ordinal)
                ? []
                : [new("invalid_structure", "Bản dịch làm thay đổi cấu trúc Markdown được bảo vệ.")];
        }
        catch
        {
            return [new("invalid_structure", "Không thể parse lại cấu trúc Markdown sau khi áp dụng bản dịch.")];
        }
    }

    private static IEnumerable<string> BuildStructureSignature(MarkdownDocument document, string source)
    {
        foreach (var item in document.Descendants())
        {
            if (item is Block block) yield return $"B:{block.GetType().FullName}";
            switch (item)
            {
                case LinkInline link:
                    yield return $"L:{link.IsImage}:{link.IsAutoLink}:{link.Url}";
                    break;
                case CodeInline or AutolinkInline or HtmlInline:
                    yield return $"P:{item.GetType().FullName}:{SafeSlice(source, item.Span.Start, item.Span.End + 1)}";
                    break;
                default:
                    if (item.GetType().Name.Contains("Math", StringComparison.Ordinal))
                        yield return $"P:{item.GetType().FullName}:{SafeSlice(source, item.Span.Start, item.Span.End + 1)}";
                    break;
            }
        }
    }

    private static EncodedInline? EncodeContainer(ContainerInline container, string source, MarkerAllocationContext allocator)
    {
        var children = container.ToList();
        if (children.Count == 0) return null;
        var start = children.Min(x => x.Span.Start);
        var end = children.Max(x => x.Span.End) + 1;
        if (start < 0 || end <= start || end > source.Length) return null;
        var markers = new Dictionary<int, MarkerDefinition>();
        var sb = new StringBuilder();
        foreach (var inline in children) EncodeInline(inline, source, allocator, markers, sb);
        return new(sb.ToString(), start, end, markers);
    }

    private static (string Replacement, bool HasSoftBreak) FindNewlinePolicy(ContainerInline container, string source)
    {
        var children = container.ToList();
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is not LineBreakInline { IsHard: false } lineBreak) continue;
            var next = i + 1 < children.Count ? children[i + 1].Span.Start : lineBreak.Span.End + 1;
            var start = Math.Max(0, lineBreak.Span.Start);
            var end = Math.Min(source.Length, Math.Max(start, next));
            var raw = source[start..end];
            var newlineAt = raw.IndexOfAny(['\r', '\n']);
            if (newlineAt >= 0) return (raw[newlineAt..], true);
            return ("\n", true);
        }

        var first = source.IndexOfAny(['\r', '\n']);
        if (first < 0) return ("\n", false);
        return source[first] == '\r' && first + 1 < source.Length && source[first + 1] == '\n'
            ? ("\r\n", false)
            : (source[first].ToString(), false);
    }

    private static void EncodeInline(Inline inline, string source, MarkerAllocationContext allocator, Dictionary<int, MarkerDefinition> markers, StringBuilder sb)
    {
        switch (inline)
        {
            case LiteralInline literal:
                EncodeLiteral(literal, source, allocator, markers, sb);
                break;
            case LineBreakInline lineBreak when lineBreak.IsHard:
                AddProtected(lineBreak, source, allocator, markers, sb);
                break;
            case LineBreakInline:
                sb.Append('\n');
                break;
            case CodeInline:
            case AutolinkInline:
            case HtmlInline:
                AddProtected(inline, source, allocator, markers, sb);
                break;
            case ContainerInline nested:
                AddFormatting(nested, source, allocator, markers, sb);
                break;
            default:
                AddProtected(inline, source, allocator, markers, sb);
                break;
        }
    }

    private static void EncodeLiteral(LiteralInline literal, string source, MarkerAllocationContext allocator, Dictionary<int, MarkerDefinition> markers, StringBuilder sb)
    {
        var value = literal.Content.ToString();
        var tokens = MarkdownMarkerCodec.Parse(value);
        if (!tokens.Any(x => x.IsMarker)) { sb.Append(value); return; }

        foreach (var token in tokens)
        {
            if (!token.IsMarker) { sb.Append(token.Value); continue; }
            var id = allocator.AllocateId();
            markers[id] = new(id, MarkerKind.Protected, token.Value, string.Empty);
            sb.Append(MarkdownMarkerCodec.Open(id)).Append(MarkdownMarkerCodec.Close(id));
        }
    }

    private static void AddProtected(Inline inline, string source, MarkerAllocationContext allocator, Dictionary<int, MarkerDefinition> markers, StringBuilder sb)
    {
        var id = allocator.AllocateId();
        var raw = SafeSlice(source, inline.Span.Start, inline.Span.End + 1);
        markers[id] = new(id, MarkerKind.Protected, raw, string.Empty);
        sb.Append(MarkdownMarkerCodec.Open(id)).Append(MarkdownMarkerCodec.Close(id));
    }

    private static void AddFormatting(ContainerInline inline, string source, MarkerAllocationContext allocator, Dictionary<int, MarkerDefinition> markers, StringBuilder sb)
    {
        var children = inline.ToList();
        if (children.Count == 0) { AddProtected(inline, source, allocator, markers, sb); return; }
        var id = allocator.AllocateId();
        var first = children.Min(x => x.Span.Start);
        var last = children.Max(x => x.Span.End) + 1;
        var open = SafeSlice(source, inline.Span.Start, first);
        var close = SafeSlice(source, last, inline.Span.End + 1);
        markers[id] = new(id, MarkerKind.Formatting, open, close);
        sb.Append(MarkdownMarkerCodec.Open(id));
        foreach (var child in children) EncodeInline(child, source, allocator, markers, sb);
        sb.Append(MarkdownMarkerCodec.Close(id));
    }

    private static string SafeSlice(string source, int start, int end) =>
        start >= 0 && end >= start && end <= source.Length ? source[start..end] : string.Empty;

    private static string RemoveMarkers(string text)
    {
        var sb = new StringBuilder();
        foreach (var token in MarkdownMarkerCodec.Parse(text)) if (!token.IsMarker) sb.Append(token.Value);
        return sb.ToString();
    }
}
