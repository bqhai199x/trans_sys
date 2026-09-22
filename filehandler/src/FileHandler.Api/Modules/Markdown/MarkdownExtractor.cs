using FileHandler.Api.Common;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace FileHandler.Api.Modules.Markdown;

/// <summary>
/// Extracts translation units and validates structural integrity of Markdown documents.
/// </summary>
internal sealed class MarkdownExtractor : IMarkdownExtractor
{

    /// <summary>
    /// Markdown pipeline configured for supported syntax.
    /// </summary>
    private readonly MarkdownPipeline _pipeline;

    /// <summary>
    /// Creates extractor with configured Markdown pipeline.
    /// </summary>
    /// <param name="pipeline">Configured Markdown parsing pipeline.</param>
    public MarkdownExtractor(MarkdownPipeline pipeline) => _pipeline = pipeline;

    /// <summary>
    /// Extracts translation units while preserving Markdown syntax with markers.
    /// </summary>
    /// <param name="source">Original Markdown source.</param>
    /// <param name="maxUnits">Maximum translation unit count.</param>
    /// <param name="cancellationToken">Token for cancelling this operation.</param>
    /// <returns>Translation units, source metadata, and extraction errors.</returns>
    public MarkdownExtraction Extract(MarkdownSource source, int maxUnits, CancellationToken cancellationToken)
    {
        var document = ParseDocument(source.Text);
        var allocator = new MarkerAllocationContext([]);
        var units = new List<MarkdownUnit>();
        var skipped = new List<SkipMetadata>();

        foreach (var block in document.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (block is FencedCodeBlock fence)
            {
                var labels = MermaidCodec.Extract(fence, cancellationToken).ToArray();
                if (labels.Length == 0)
                {
                    var mermaid = string.Equals(fence.Info, "mermaid", StringComparison.OrdinalIgnoreCase);
                    skipped.Add(new(mermaid ? SkipCodes.UnsupportedMermaid : SkipCodes.ProtectedCodeBlock, mermaid ? SkipSeverity.Warning : SkipSeverity.Info, SkipStage.Extraction, SkipScope.Block, 1, mermaid ? ProcessingMessages.UnsupportedMermaid : ProcessingMessages.ProtectedCodeBlock, new(Line: source.Lines.GetRange(fence.Span.Start, fence.Span.End + 1))));
                }
                foreach (var label in labels)
                {
                    units.Add(new(label.Start, label.End, label.Text, new Dictionary<int, MarkerDefinition>(),
                        source.Lines.GetRange(label.Start, label.End), false, "\n", false)
                    {
                        IsMermaidLabel = true,
                        MermaidQuoted = label.Quoted
                    });
                    if (units.Count > maxUnits)
                        return new(source, [], [new("too_many_units", ProcessingMessages.DocumentUnitLimit(maxUnits))]);
                }
                continue;
            }
            if (block is not LeafBlock { Inline: not null } leaf)
            {
                if (block is CodeBlock or HtmlBlock || block.GetType().Name.Contains("Yaml", StringComparison.Ordinal))
                    skipped.Add(new(SkipCodes.ProtectedBlock, SkipSeverity.Info, SkipStage.Extraction, SkipScope.Block, 1, ProcessingMessages.ProtectedBlock, new(Line: source.Lines.GetRange(block.Span.Start, block.Span.End + 1))));
                continue;
            }
            if (block is CodeBlock)
                continue;
            foreach (var item in leaf.Inline.Descendants().Where(i => i is CodeInline or AutolinkInline or HtmlInline || i is LinkInline { IsImage: true }))
                skipped.Add(new(SkipCodes.ProtectedInline, SkipSeverity.Info, SkipStage.Extraction, SkipScope.Inline, 1, ProcessingMessages.ProtectedInline, new(Line: source.Lines.GetRange(item.Span.Start, item.Span.End + 1))));
            var encoded = EncodeContainer(leaf.Inline, source.Text, allocator);
            if (encoded is null || !encoded.Tokens.Any(t => !t.IsMarker && !string.IsNullOrWhiteSpace(t.Value)))
                continue;
            var (newlineReplacement, hasSoftBreak) = FindNewlinePolicy(leaf.Inline, source.Text);
            var wire = MarkdownTokenCodec.Encode(encoded);
            units.Add(new(encoded.Start, encoded.End, wire.Text, encoded.Markers, source.Lines.GetRange(encoded.Start, encoded.End), leaf is HeadingBlock, newlineReplacement, hasSoftBreak, wire.Template) { BlockStart = leaf.Span.Start, BlockEnd = leaf.Span.End + 1 });
            if (units.Count > maxUnits)
                return new(source, [], [new("too_many_units", ProcessingMessages.DocumentUnitLimit(maxUnits))]);
        }

        var hasInternalLinks = document.Descendants<LinkInline>().Any(x => x.Url?.StartsWith('#') == true);
        return new(source, units, [], hasInternalLinks) { Skipped = skipped };
    }

    /// <summary>
    /// Parses raw Markdown text into abstract syntax tree document.
    /// </summary>
    /// <param name="text">Markdown source text.</param>
    /// <returns>Parsed Markdown document.</returns>
    internal MarkdownDocument ParseDocument(string text) => Markdig.Markdown.Parse(text, _pipeline);

    /// <summary>
    /// Validates translated Markdown structural parity against source.
    /// </summary>
    /// <param name="original">Original Markdown source.</param>
    /// <param name="candidate">Translated Markdown text.</param>
    /// <returns>Structural validation errors, if any.</returns>
    public IReadOnlyList<FileError> ValidateStructure(string original, string candidate)
    {
        var before = BuildStructureSignature(ParseDocument(original), original);
        var after = BuildStructureSignature(ParseDocument(candidate), candidate);
        return before.SequenceEqual(after, StringComparer.Ordinal) ? [] : [new(SkipCodes.InvalidStructure, ProcessingMessages.MarkdownStructureChanged)];
    }

    /// <summary>
    /// Collects block types, link targets, and protected inline content.
    /// </summary>
    /// <param name="document">Parsed Markdown document.</param>
    /// <param name="source">Original Markdown source.</param>
    /// <returns>Ordered block types, link targets, and protected content.</returns>
    private static IReadOnlyList<string> BuildStructureSignature(MarkdownDocument document, string source)
    {
        var signature = new List<string>();
        foreach (var item in document.Descendants())
        {
            if (item is Block block)
                signature.Add($"B:{block.GetType().FullName}");
            switch (item)
            {
                case FencedCodeBlock fence:
                    var labels = MermaidCodec.Extract(fence, default).ToArray();
                    var offset = fence.Span.Start;
                    foreach (var label in labels)
                    {
                        signature.Add("C:" + SafeSlice(source, offset, label.Start));
                        offset = label.End;
                    }
                    signature.Add("C:" + SafeSlice(source, offset, fence.Span.End + 1));
                    break;
                case LineBreakInline { IsHard: true } lineBreak:
                    signature.Add($"H:{lineBreak.IsBackslash}");
                    break;
                case EmphasisInline emphasis:
                    signature.Add($"E:{emphasis.DelimiterChar}:{emphasis.DelimiterCount}");
                    break;
                case LinkInline link:
                    signature.Add($"L:{link.IsImage}:{link.IsAutoLink}:{link.Url}");
                    break;
                case CodeInline or AutolinkInline or HtmlInline:
                    signature.Add($"P:{item.GetType().FullName}:{SafeSlice(source, item.Span.Start, item.Span.End + 1)}");
                    break;
                default:
                    if (item.GetType().Name.Contains("Math", StringComparison.Ordinal))
                        signature.Add($"P:{item.GetType().FullName}:{SafeSlice(source, item.Span.Start, item.Span.End + 1)}");
                    break;
            }
        }

        return signature;
    }

    /// <summary>
    /// Encodes inline container and records its source span.
    /// </summary>
    /// <param name="container">Inline container to inspect.</param>
    /// <param name="source">Original Markdown source.</param>
    /// <param name="allocator">Allocator for collision-free marker IDs.</param>
    /// <returns>Encoded text and source span, or null for empty or invalid spans.</returns>
    private static EncodedInline? EncodeContainer(ContainerInline container, string source, MarkerAllocationContext allocator)
    {
        var children = container.ToList();
        if (children.Count == 0)
            return null;
        var start = children.Min(x => x.Span.Start);
        var end = children.Max(x => x.Span.End) + 1;
        if (start < 0 || end <= start || end > source.Length)
            return null;
        var markers = new Dictionary<int, MarkerDefinition>();
        var sb = new List<MarkerToken>();
        foreach (var inline in children)
            EncodeInline(inline, source, allocator, markers, sb);
        return new(sb, start, end, markers);
    }

    /// <summary>
    /// Finds newline sequence and prefix to preserve in translations.
    /// </summary>
    /// <param name="container">Inline container to inspect.</param>
    /// <param name="source">Original Markdown source.</param>
    /// <returns>Newline replacement and soft line break flag.</returns>
    private static (string Replacement, bool HasSoftBreak) FindNewlinePolicy(ContainerInline container, string source)
    {
        var children = GetAllInlines(container).ToList();
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is not LineBreakInline { IsHard: false } lineBreak)
                continue;
            var next = i + 1 < children.Count ? children[i + 1].Span.Start : lineBreak.Span.End + 1;
            var start = Math.Max(0, lineBreak.Span.Start);
            var end = Math.Min(source.Length, Math.Max(start, next));
            var raw = source.AsSpan(start, end - start);
            var newlineAt = raw.IndexOfAny('\r', '\n');
            if (newlineAt >= 0)
                return (raw[newlineAt..].ToString(), true);
            return ("\n", true);
        }

        var first = source.IndexOfAny(['\r', '\n']);
        if (first < 0)
            return ("\n", false);
        return source[first] == '\r' && first + 1 < source.Length && source[first + 1] == '\n' ? ("\r\n", false) : (source[first].ToString(), false);
    }

    /// <summary>
    /// Flattens inline container into recursive sequence of inline children.
    /// </summary>
    /// <param name="container">Inline container to flatten.</param>
    /// <returns>Sequence of child and descendant inline nodes.</returns>
    private static IEnumerable<Inline> GetAllInlines(ContainerInline container)
    {
        foreach (var child in container)
        {
            yield return child;
            if (child is ContainerInline nested)
            {
                foreach (var descendant in GetAllInlines(nested))
                    yield return descendant;
            }
        }
    }

    /// <summary>
    /// Appends translatable text or markers for inline node.
    /// </summary>
    /// <param name="inline">Inline node to encode.</param>
    /// <param name="source">Original Markdown source.</param>
    /// <param name="allocator">Allocator for collision-free marker IDs.</param>
    /// <param name="markers">Marker definitions indexed by ID.</param>
    /// <param name="sb">Output buffer for encoded text.</param>
    /// <returns>No return value.</returns>
    private static void EncodeInline(Inline inline, string source, MarkerAllocationContext allocator, Dictionary<int, MarkerDefinition> markers, List<MarkerToken> sb)
    {
        switch (inline)
        {
            case LiteralInline literal:
                EncodeLiteral(literal, sb);
                break;
            case LineBreakInline { IsHard: true } lineBreak:
                AddHardBreak(lineBreak, source, allocator, markers, sb);
                break;
            case LineBreakInline softBreak:
                AddSoftBreak(softBreak, source, allocator, markers, sb);
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

    /// <summary>
    /// Protects soft line breaks together with container continuation prefixes.
    /// </summary>
    /// <param name="lineBreak">Soft line break node.</param>
    /// <param name="source">Original Markdown source.</param>
    /// <param name="allocator">Unit marker allocator.</param>
    /// <param name="markers">Marker definitions.</param>
    /// <param name="sb">Encoded output buffer.</param>
    /// <returns>No return value.</returns>
    private static void AddSoftBreak(LineBreakInline lineBreak, string source, MarkerAllocationContext allocator, Dictionary<int, MarkerDefinition> markers, List<MarkerToken> sb)
    {
        var id = allocator.AllocateId();
        var end = lineBreak.NextSibling?.Span.Start ?? lineBreak.Span.End + 1;
        if (end < source.Length && end > 0 && source[end - 1] == '\r' && source[end] == '\n') end++;
        markers[id] = new(id, MarkerKind.Protected, SafeSlice(source, lineBreak.Span.Start, end), string.Empty);
        sb.Add(new(true, id, string.Empty, false));
        sb.Add(new(true, id, string.Empty, true));
    }

    /// <summary>
    /// Preserves hard line break syntax including trailing spaces or backslash and newline characters.
    /// </summary>
    /// <param name="lineBreak">Hard line break inline node.</param>
    /// <param name="source">Original Markdown source.</param>
    /// <param name="allocator">Allocator for collision-free marker IDs.</param>
    /// <param name="markers">Marker definitions indexed by ID.</param>
    /// <param name="sb">Output buffer for encoded text.</param>
    /// <returns>No return value.</returns>
    private static void AddHardBreak(LineBreakInline lineBreak, string source, MarkerAllocationContext allocator, Dictionary<int, MarkerDefinition> markers, List<MarkerToken> sb)
    {
        var id = allocator.AllocateId();
        var start = lineBreak.Span.Start;
        while (start > 0 && source[start - 1] == ' ')
            start--;
        if (start > 0 && source[start - 1] == '\\')
            start--;

        var end = lineBreak.Span.End + 1;
        if (end <= source.Length && end > start && source[end - 1] == '\r' && end < source.Length && source[end] == '\n')
            end++;

        var raw = SafeSlice(source, start, end);
        markers[id] = new(id, MarkerKind.Protected, raw, string.Empty);
        sb.Add(new(true, id, string.Empty, false));
        sb.Add(new(true, id, string.Empty, true));
    }

    /// <summary>
    /// Appends literal text as typed data without interpreting preservation syntax.
    /// </summary>
    /// <param name="literal">Literal inline node to encode.</param>
    /// <param name="sb">Output buffer for encoded text.</param>
    /// <returns>No return value.</returns>
    private static void EncodeLiteral(LiteralInline literal, List<MarkerToken> sb)
    {
        var value = literal.Content.ToString();
        if (sb.Count > 0 && !sb[^1].IsMarker)
            sb[^1] = sb[^1] with { Value = sb[^1].Value + value };
        else
            sb.Add(new(false, 0, value, false));
    }

    /// <summary>
    /// Replaces protected source content with empty marker pair.
    /// </summary>
    /// <param name="inline">Inline node to encode.</param>
    /// <param name="source">Original Markdown source.</param>
    /// <param name="allocator">Allocator for collision-free marker IDs.</param>
    /// <param name="markers">Marker definitions indexed by ID.</param>
    /// <param name="sb">Output buffer for encoded text.</param>
    /// <returns>No return value.</returns>
    private static void AddProtected(Inline inline, string source, MarkerAllocationContext allocator, Dictionary<int, MarkerDefinition> markers, List<MarkerToken> sb)
    {
        var id = allocator.AllocateId();
        var raw = SafeSlice(source, inline.Span.Start, inline.Span.End + 1);
        markers[id] = new(id, MarkerKind.Protected, raw, string.Empty);
        sb.Add(new(true, id, string.Empty, false));
        sb.Add(new(true, id, string.Empty, true));
    }

    /// <summary>
    /// Wraps translatable child content in formatting markers.
    /// </summary>
    /// <param name="inline">Inline node to encode.</param>
    /// <param name="source">Original Markdown source.</param>
    /// <param name="allocator">Allocator for collision-free marker IDs.</param>
    /// <param name="markers">Marker definitions indexed by ID.</param>
    /// <param name="sb">Output buffer for encoded text.</param>
    /// <returns>No return value.</returns>
    private static void AddFormatting(ContainerInline inline, string source, MarkerAllocationContext allocator, Dictionary<int, MarkerDefinition> markers, List<MarkerToken> sb)
    {
        var children = inline.ToList();
        if (children.Count == 0)
        {
            AddProtected(inline, source, allocator, markers, sb);
            return;
        }

        var id = allocator.AllocateId();
        var first = children.Min(x => x.Span.Start);
        var last = children.Max(x => x.Span.End) + 1;
        var open = SafeSlice(source, inline.Span.Start, first);
        var close = SafeSlice(source, last, inline.Span.End + 1);
        markers[id] = new(id, MarkerKind.Formatting, open, close) { IsEmphasis = inline is EmphasisInline };
        sb.Add(new(true, id, string.Empty, false));
        foreach (var child in children)
            EncodeInline(child, source, allocator, markers, sb);
        sb.Add(new(true, id, string.Empty, true));
    }

    /// <summary>
    /// Returns requested source slice, or empty string for invalid bounds.
    /// </summary>
    /// <param name="source">Original Markdown source.</param>
    /// <param name="start">Inclusive start character offset.</param>
    /// <param name="end">Exclusive end character offset.</param>
    /// <returns>Requested source slice, or empty string for invalid bounds.</returns>
    private static string SafeSlice(string source, int start, int end) =>
        start >= 0 && end >= start && end <= source.Length ? source[start..end] : string.Empty;
}
