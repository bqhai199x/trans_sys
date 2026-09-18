using System.Text;
using FileHandler.Api.Common;
using FileHandler.Api.Diagnostics;
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
        using var trace = DebugTrace.Enter("MarkdownExtractor", "Extract", () => new { source, maxUnits, cancellationToken });
        try
        {
            var document = ParseDocument(source.Text);
            var allocator = new MarkerAllocationContext(MarkdownMarkerCodec.FindReservedIds(source.Text));
            var units = new List<MarkdownUnit>();
            var blockIndex = 0;

            foreach (var block in document.Descendants())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (block is not LeafBlock { Inline: not null } leaf)
                    continue;
                if (block is CodeBlock)
                    continue;
                using var item = DebugTrace.Item(++blockIndex);
                item.State("block", () => new { type = leaf.GetType().Name, line = leaf.Line + 1, start = leaf.Span.Start, end = leaf.Span.End + 1 });
                var encoded = EncodeContainer(leaf.Inline, source.Text, allocator);
                if (encoded is null || string.IsNullOrWhiteSpace(RemoveMarkers(encoded.Text)))
                {
                    item.State("outcome", () => "noTranslatableText");
                    continue;
                }
                var (newlineReplacement, hasSoftBreak) = FindNewlinePolicy(leaf.Inline, source.Text);
                var wire = MarkdownTokenCodec.Encode(encoded);
                units.Add(new(encoded.Start, encoded.End, wire.Text, encoded.Markers, source.Lines.GetRange(encoded.Start, encoded.End), leaf is HeadingBlock, newlineReplacement, hasSoftBreak, wire.Template));
                item.State("unitIndex", () => units.Count - 1);
                item.State("unit", () => units[^1]);
                if (units.Count > maxUnits)
                {
                    trace.State("unitCount", () => units.Count);
                    return trace.Return<MarkdownExtraction>(new(source, [], [new("too_many_units", $"Tài liệu vượt giới hạn {maxUnits} đơn vị dịch.")]));
                }
            }

            var hasInternalLinks = document.Descendants<LinkInline>().Any(x => x.Url?.StartsWith('#') == true);
            trace.State("unitCount", () => units.Count);
            trace.State("hasInternalLinks", () => hasInternalLinks);
            return trace.Return<MarkdownExtraction>(new(source, units, [], hasInternalLinks));
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
    }

    /// <summary>
    /// Parses raw Markdown text into abstract syntax tree document.
    /// </summary>
    /// <param name="text">Markdown source text.</param>
    /// <returns>Parsed Markdown document.</returns>
    internal MarkdownDocument ParseDocument(string text) =>
        DebugTrace.Trace("MarkdownExtractor", "ParseDocument", () => new { text }, _ => Markdig.Markdown.Parse(text, _pipeline));

    /// <summary>
    /// Validates translated Markdown structural parity against source.
    /// </summary>
    /// <param name="original">Original Markdown source.</param>
    /// <param name="candidate">Translated Markdown text.</param>
    /// <returns>Structural validation errors, if any.</returns>
    public IReadOnlyList<FileError> ValidateStructure(string original, string candidate)
    {
        using var trace = DebugTrace.Enter("MarkdownExtractor", "ValidateStructure", () => new { original, candidate });
        try
        {
            try
            {
                var before = BuildStructureSignature(ParseDocument(original), original);
                trace.State("signature", () => before);
                var after = BuildStructureSignature(ParseDocument(candidate), candidate);
                trace.State("signature", () => after);
                return trace.Return<IReadOnlyList<FileError>>(before.SequenceEqual(after, StringComparer.Ordinal) ? [] : [new("invalid_structure", "Bản dịch làm thay đổi cấu trúc Markdown được bảo vệ.")]);
            }
            catch
            {
                return trace.Return<IReadOnlyList<FileError>>([new("invalid_structure", "Không thể parse lại cấu trúc Markdown sau khi áp dụng bản dịch.")]);
            }
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
    }

    /// <summary>
    /// Collects block types, link targets, and protected inline content.
    /// </summary>
    /// <param name="document">Parsed Markdown document.</param>
    /// <param name="source">Original Markdown source.</param>
    /// <returns>Ordered block types, link targets, and protected content.</returns>
    private static IReadOnlyList<string> BuildStructureSignature(MarkdownDocument document, string source)
    {
        using var trace = DebugTrace.Enter("MarkdownExtractor", "BuildStructureSignature", () => new { document, source });
        try
        {
            var signature = new List<string>();
            foreach (var item in document.Descendants())
            {
                if (item is Block block)
                    signature.Add($"B:{block.GetType().FullName}");
                switch (item)
                {
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

            return trace.Return<IReadOnlyList<string>>(signature);
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
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
        using var trace = DebugTrace.Enter("MarkdownExtractor", "EncodeContainer", () => new { container, source, allocator });
        try
        {
            var children = container.ToList();
            if (children.Count == 0)
                return trace.Return<EncodedInline?>(null);
            var start = children.Min(x => x.Span.Start);
            var end = children.Max(x => x.Span.End) + 1;
            if (start < 0 || end <= start || end > source.Length)
                return trace.Return<EncodedInline?>(null);
            var markers = new Dictionary<int, MarkerDefinition>();
            var sb = new StringBuilder();
            foreach (var inline in children)
                EncodeInline(inline, source, allocator, markers, sb);
            return trace.Return<EncodedInline?>(new(sb.ToString(), start, end, markers));
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
    }

    /// <summary>
    /// Finds newline sequence and prefix to preserve in translations.
    /// </summary>
    /// <param name="container">Inline container to inspect.</param>
    /// <param name="source">Original Markdown source.</param>
    /// <returns>Newline replacement and soft line break flag.</returns>
    private static (string Replacement, bool HasSoftBreak) FindNewlinePolicy(ContainerInline container, string source)
    {
        using var trace = DebugTrace.Enter("MarkdownExtractor", "FindNewlinePolicy", () => new { container, source });
        try
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
                    return trace.Return<(string Replacement, bool HasSoftBreak)>((raw[newlineAt..].ToString(), true));
                return trace.Return<(string Replacement, bool HasSoftBreak)>(("\n", true));
            }

            var first = source.IndexOfAny(['\r', '\n']);
            if (first < 0)
                return trace.Return<(string Replacement, bool HasSoftBreak)>(("\n", false));
            return trace.Return<(string Replacement, bool HasSoftBreak)>(source[first] == '\r' && first + 1 < source.Length && source[first + 1] == '\n' ? ("\r\n", false) : (source[first].ToString(), false));
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
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
    private static void EncodeInline(Inline inline, string source, MarkerAllocationContext allocator, Dictionary<int, MarkerDefinition> markers, StringBuilder sb)
    {
        using var trace = DebugTrace.Enter("MarkdownExtractor", "EncodeInline", () => new { inline });
        trace.State("buffer", () => new { text = sb, markerCount = markers.Count });
        try
        {
            switch (inline)
            {
                case LiteralInline literal:
                    EncodeLiteral(literal, source, allocator, markers, sb);
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
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
        finally
        {
            trace.State("buffer", () => new { text = sb, markerCount = markers.Count });
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
    private static void AddSoftBreak(LineBreakInline lineBreak, string source, MarkerAllocationContext allocator, Dictionary<int, MarkerDefinition> markers, StringBuilder sb)
    {
        var id = allocator.AllocateId();
        var end = lineBreak.NextSibling?.Span.Start ?? lineBreak.Span.End + 1;
        if (end < source.Length && end > 0 && source[end - 1] == '\r' && source[end] == '\n') end++;
        markers[id] = new(id, MarkerKind.Protected, SafeSlice(source, lineBreak.Span.Start, end), string.Empty);
        sb.Append(MarkdownMarkerCodec.Open(id)).Append(MarkdownMarkerCodec.Close(id));
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
    private static void AddHardBreak(LineBreakInline lineBreak, string source, MarkerAllocationContext allocator, Dictionary<int, MarkerDefinition> markers, StringBuilder sb)
    {
        using var trace = DebugTrace.Enter("MarkdownExtractor", "AddHardBreak", () => new { lineBreak });
        try
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
            trace.State("marker", () => markers[id]);
            sb.Append(MarkdownMarkerCodec.Open(id)).Append(MarkdownMarkerCodec.Close(id));
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
    }

    /// <summary>
    /// Appends literal text while protecting marker-like content and existing prefix patterns.
    /// </summary>
    /// <param name="literal">Literal inline node to encode.</param>
    /// <param name="source">Original Markdown source.</param>
    /// <param name="allocator">Allocator for collision-free marker IDs.</param>
    /// <param name="markers">Marker definitions indexed by ID.</param>
    /// <param name="sb">Output buffer for encoded text.</param>
    /// <returns>No return value.</returns>
    private static void EncodeLiteral(LiteralInline literal, string source, MarkerAllocationContext allocator, Dictionary<int, MarkerDefinition> markers, StringBuilder sb)
    {
        using var trace = DebugTrace.Enter("MarkdownExtractor", "EncodeLiteral", () => new { literal });
        try
        {
            var value = literal.Content.ToString();
            var tokens = MarkdownMarkerCodec.Parse(value);
            if (!tokens.Any(x => x.IsMarker) && !value.Contains(MarkdownMarkerCodec.MarkerPrefix, StringComparison.Ordinal))
            {
                sb.Append(value);
                return;
            }

            foreach (var token in tokens)
            {
                if (token.IsMarker)
                {
                    var id = allocator.AllocateId();
                    markers[id] = new(id, MarkerKind.Protected, token.Value, string.Empty);
                    trace.State("marker", () => markers[id]);
                    sb.Append(MarkdownMarkerCodec.Open(id)).Append(MarkdownMarkerCodec.Close(id));
                }
                else if (token.Value.Contains(MarkdownMarkerCodec.MarkerPrefix, StringComparison.Ordinal))
                {
                    var part = token.Value;
                    var idx = 0;
                    while (idx < part.Length)
                    {
                        var p = part.IndexOf(MarkdownMarkerCodec.MarkerPrefix, idx, StringComparison.Ordinal);
                        if (p < 0)
                        {
                            sb.Append(part[idx..]);
                            break;
                        }

                        if (p > idx)
                            sb.Append(part[idx..p]);

                        var id = allocator.AllocateId();
                        markers[id] = new(id, MarkerKind.Protected, MarkdownMarkerCodec.MarkerPrefix, string.Empty);
                        trace.State("marker", () => markers[id]);
                        sb.Append(MarkdownMarkerCodec.Open(id)).Append(MarkdownMarkerCodec.Close(id));
                        idx = p + MarkdownMarkerCodec.MarkerPrefix.Length;
                    }
                }
                else
                {
                    sb.Append(token.Value);
                }
            }
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
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
    private static void AddProtected(Inline inline, string source, MarkerAllocationContext allocator, Dictionary<int, MarkerDefinition> markers, StringBuilder sb)
    {
        using var trace = DebugTrace.Enter("MarkdownExtractor", "AddProtected", () => new { inline });
        try
        {
            var id = allocator.AllocateId();
            var raw = SafeSlice(source, inline.Span.Start, inline.Span.End + 1);
            markers[id] = new(id, MarkerKind.Protected, raw, string.Empty);
            trace.State("marker", () => markers[id]);
            sb.Append(MarkdownMarkerCodec.Open(id)).Append(MarkdownMarkerCodec.Close(id));
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
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
    private static void AddFormatting(ContainerInline inline, string source, MarkerAllocationContext allocator, Dictionary<int, MarkerDefinition> markers, StringBuilder sb)
    {
        using var trace = DebugTrace.Enter("MarkdownExtractor", "AddFormatting", () => new { inline });
        try
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
            markers[id] = new(id, MarkerKind.Formatting, open, close);
            trace.State("marker", () => markers[id]);
            sb.Append(MarkdownMarkerCodec.Open(id));
            foreach (var child in children)
                EncodeInline(child, source, allocator, markers, sb);
            sb.Append(MarkdownMarkerCodec.Close(id));
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
    }

    /// <summary>
    /// Returns requested source slice, or empty string for invalid bounds.
    /// </summary>
    /// <param name="source">Original Markdown source.</param>
    /// <param name="start">Inclusive start character offset.</param>
    /// <param name="end">Exclusive end character offset.</param>
    /// <returns>Requested source slice, or empty string for invalid bounds.</returns>
    private static string SafeSlice(string source, int start, int end)
    {
        using var trace = DebugTrace.Enter("MarkdownExtractor", "SafeSlice", () => new { source, start, end });
        try
        {
            return trace.Return<string>(start >= 0 && end >= start && end <= source.Length ? source[start..end] : string.Empty);
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
    }

    /// <summary>
    /// Removes marker tokens while preserving literal text.
    /// </summary>
    /// <param name="text">Text to process.</param>
    /// <returns>Literal text without marker tokens.</returns>
    private static string RemoveMarkers(string text)
    {
        using var trace = DebugTrace.Enter("MarkdownExtractor", "RemoveMarkers", () => new { text });
        try
        {
            var sb = new StringBuilder();
            foreach (var token in MarkdownMarkerCodec.Parse(text))
                if (!token.IsMarker)
                    sb.Append(token.Value);
            return trace.Return<string>(sb.ToString());
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
    }
}
