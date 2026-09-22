using FileHandler.Api.Common;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig.Syntax;

namespace FileHandler.Api.Modules.Markdown;

/// <summary>
/// Extracts translatable Mermaid diagram labels and encodes replacements while preserving diagram syntax and source offsets.
/// </summary>
internal static class MermaidCodec
{

    /// <summary>
    /// Flowchart declarations and orientations.
    /// </summary>
    private static readonly Regex FlowchartHeader = new(@"^\s*(?:flowchart|graph)(?:\s+(?:TB|TD|BT|RL|LR))?\b[ \t]*;?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Sequence diagram header declaration.
    /// </summary>
    private static readonly Regex SequenceHeader = new(@"^\s*sequenceDiagram\b[ \t]*;?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>
    /// State diagram header declaration.
    /// </summary>
    private static readonly Regex StateHeader = new(@"^\s*stateDiagram(?:-v2)?\b[ \t]*;?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Class diagram header declaration.
    /// </summary>
    private static readonly Regex ClassHeader = new(@"^\s*classDiagram(?:-v2)?\b[ \t]*;?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Entity-relationship diagram header declaration.
    /// </summary>
    private static readonly Regex ErHeader = new(@"^\s*erDiagram\b[ \t]*;?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Statements whose arguments are configuration rather than visible labels.
    /// </summary>
    private static readonly Regex FlowchartConfiguration = new(@"^\s*(?:style|classDef|class|click|linkStyle|direction|end|accTitle|accDescr)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Flowchart node identifiers excluding arrow punctuation at identifier boundaries.
    /// </summary>
    private static readonly Regex FlowchartNode = new(@"\G[\p{L}\p{N}_]+(?:[.-][\p{L}\p{N}_]+)*[ \t]*", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Flowchart pipe labels and inline labels on solid, dotted and thick edges.
    /// </summary>
    private static readonly Regex FlowchartEdge = new("""\G(?:(?:<?(?:-+\.+-+|-{2,}|={2,})[>ox]?)[ \t]*\|(?<label>"[^"]*"|[^|\r\n]*)\||-\.[ \t]+(?<label>"[^"]*"|[^\r\n]*?)[ \t]+\.-+>|--[ \t]+(?<label>"[^"]*"|[^\r\n]*?)[ \t]+--+>|==[ \t]+(?<label>"[^"]*"|[^\r\n]*?)[ \t]+==+>)""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Shape delimiters ordered from longest to shortest opener.
    /// </summary>
    private static readonly (string Open, string Close)[] FlowchartShapes =
    [
        ("(((", ")))"), ("((", "))"), ("([", "])"), ("[[", "]]"), ("[(", ")]"),
        ("{{", "}}"), ("[/", "/]"), ("[\\", "\\]"),
        ("[", "]"), ("(", ")"), ("{", "}"), (">", "]")
    ];

    /// <summary>
    /// Subgraph declaration with optional bracketed title.
    /// </summary>
    private static readonly Regex SubgraphPattern = new(@"^\s*subgraph\s+(?:[\p{L}\p{N}_]+(?:[.-][\p{L}\p{N}_]+)*\s*\[(?<label>[^\]\r\n]+)\]|""(?<label>[^""\r\n]+)""|(?<label>[^\r\n;\[]+))", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Participant or actor declaration with optional display alias.
    /// </summary>
    private static readonly Regex SequenceParticipant = new(@"^\s*(?:create\s+)?(?:participant|actor)\s+(?:[\p{L}\p{N}_]+(?:[.-][\p{L}\p{N}_]+)*|""[^""\r\n]+"")\s+as\s+(?<label>[^\r\n]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Quoted participant or actor declaration without alias keyword.
    /// </summary>
    private static readonly Regex SequenceQuotedParticipant = new(@"^\s*(?:create\s+)?(?:participant|actor)\s+""(?<label>[^""\r\n]+)""[ \t]*(?:;|$)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Sequence diagram message interaction between participants.
    /// </summary>
    private static readonly Regex SequenceMessage = new(@"^\s*(?:[\p{L}\p{N}_]+(?:[.-][\p{L}\p{N}_]+)*|""[^""\r\n]+"")\s*(?:<<-->>|<<->>|-->>|->>|-->|->|--x|-x|--\)|-\))[+-]?\s*(?:[\p{L}\p{N}_]+(?:[.-][\p{L}\p{N}_]+)*|""[^""\r\n]+"")[+-]?\s*:\s*(?<label>[^\r\n]+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Sequence diagram control and condition blocks.
    /// </summary>
    private static readonly Regex SequenceBlock = new(@"^\s*(?:alt|else|opt|loop|par|and|critical|option|break)\b(?:\s+(?<label>[^\r\n]+))?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Sequence diagram note directive attached to participants.
    /// </summary>
    private static readonly Regex SequenceNote = new(@"^\s*note\s+(?:left\s+of|right\s+of|over)\s+[^\r\n;:]+:\s*(?<label>[^\r\n]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Sequence diagram participant grouping box.
    /// </summary>
    private static readonly Regex SequenceBox = new(@"^\s*box\b(?:\s+(?:rgb\([^)]*\)|rgba\([^)]*\)|hsl\([^)]*\)|hsla\([^)]*\)|#?[A-Za-z0-9_-]+))?\s*(?<label>[^\r\n]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Diagram title directive.
    /// </summary>
    private static readonly Regex TitlePattern = new(@"^\s*title(?:\s*:)?\s+(?<label>[^\r\n]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Sequence participant menu link.
    /// </summary>
    private static readonly Regex SequenceLink = new(@"^\s*link\s+[^\r\n;:]+:\s*(?<label>[^\r\n@]+)\s*@", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>
    /// State diagram transition between states.
    /// </summary>
    private static readonly Regex StateTransition = new(@"^\s*[\p{L}\p{N}_*\[\]-]+\s*-->\s*[\p{L}\p{N}_*\[\]-]+\s*:\s*(?<label>[^\r\n]+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// State diagram explicit state declaration with alias.
    /// </summary>
    private static readonly Regex StateDeclaration = new(@"^\s*state\s+""(?<label>[^""\r\n]+)""\s+as\s+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>
    /// State diagram note directive.
    /// </summary>
    private static readonly Regex StateNote = new(@"^\s*note\s+(?:left\s+of|right\s+of)\s+[\p{L}\p{N}_*\[\]-]+\s*:\s*(?<label>[^\r\n;]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Class diagram note directive.
    /// </summary>
    private static readonly Regex ClassNote = new(@"^\s*note(?:\s+for\s+[\p{L}\p{N}_]+)?\s+""(?<label>[^""\r\n]+)""", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Entity-relationship diagram relationship label.
    /// </summary>
    private static readonly Regex ErRelationship = new(@"^\s*[\p{L}\p{N}_-]+\s*[:|o}{~-]+\s*[\p{L}\p{N}_-]+\s*:\s*(?<label>""[^""\r\n]+""|[^\r\n;%]+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Mermaid decimal and named entity references.
    /// </summary>
    private static readonly Regex Entity = new(@"#(?<entity>[0-9]+|[A-Za-z]+);", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Locates visible labels across supported Mermaid diagram fences; unsupported fences remain protected.
    /// </summary>
    /// <param name="block">Parsed fenced code block.</param>
    /// <param name="cancellationToken">Token for cancelling extraction.</param>
    /// <returns>Ordered label spans with decoded text.</returns>
    internal static IEnumerable<MermaidLabel> Extract(FencedCodeBlock block, CancellationToken cancellationToken)
    {
        if (!string.Equals(block.Info, "mermaid", StringComparison.OrdinalIgnoreCase)) yield break;
        var (diagramType, headerLineIndex) = DetectDiagramType(block);
        if (diagramType == MermaidDiagramType.Unsupported) yield break;

        var labels = diagramType switch
        {
            MermaidDiagramType.Flowchart => ExtractFlowchart(block, headerLineIndex, cancellationToken),
            MermaidDiagramType.Sequence => ExtractSequence(block, headerLineIndex, cancellationToken),
            MermaidDiagramType.State => ExtractState(block, headerLineIndex, cancellationToken),
            MermaidDiagramType.Class => ExtractClass(block, headerLineIndex, cancellationToken),
            MermaidDiagramType.EntityRelationship => ExtractEr(block, headerLineIndex, cancellationToken),
            _ => Enumerable.Empty<MermaidLabel>()
        };

        foreach (var label in labels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return label;
        }
    }

    /// <summary>
    /// Inspects initial non-comment lines to identify diagram type.
    /// </summary>
    /// <param name="block">Fenced code block to inspect.</param>
    /// <returns>Detected diagram type and line index of header.</returns>
    private static (MermaidDiagramType Type, int HeaderIndex) DetectDiagramType(FencedCodeBlock block)
    {
        for (var i = 0; i < block.Lines.Count; i++)
        {
            var text = block.Lines.Lines[i].Slice.ToString();
            if (string.IsNullOrWhiteSpace(text) || text.TrimStart().StartsWith("%%", StringComparison.Ordinal)) continue;
            if (FlowchartHeader.IsMatch(text)) return (MermaidDiagramType.Flowchart, i);
            if (SequenceHeader.IsMatch(text)) return (MermaidDiagramType.Sequence, i);
            if (StateHeader.IsMatch(text)) return (MermaidDiagramType.State, i);
            if (ClassHeader.IsMatch(text)) return (MermaidDiagramType.Class, i);
            if (ErHeader.IsMatch(text)) return (MermaidDiagramType.EntityRelationship, i);
            return (MermaidDiagramType.Unsupported, i);
        }
        return (MermaidDiagramType.Unsupported, -1);
    }

    /// <summary>
    /// Extracts nodes, edges, and subgraph titles from flowchart diagrams.
    /// </summary>
    /// <param name="block">Flowchart code block.</param>
    /// <param name="headerLineIndex">Line index of flowchart header.</param>
    /// <param name="cancellationToken">Token for cancelling extraction.</param>
    /// <returns>Ordered flowchart labels.</returns>
    private static IEnumerable<MermaidLabel> ExtractFlowchart(FencedCodeBlock block, int headerLineIndex, CancellationToken cancellationToken)
    {
        for (var lineIndex = headerLineIndex; lineIndex < block.Lines.Count; lineIndex++)
        {
            var line = block.Lines.Lines[lineIndex];
            cancellationToken.ThrowIfCancellationRequested();
            var text = line.Slice.ToString();
            if (string.IsNullOrWhiteSpace(text) || text.TrimStart().StartsWith("%%", StringComparison.Ordinal)) continue;
            var offset = 0;
            if (lineIndex == headerLineIndex)
            {
                var header = FlowchartHeader.Match(text);
                if (header.Success) offset = header.Length;
            }
            if (FlowchartConfiguration.IsMatch(text)) continue;

            var subgraph = SubgraphPattern.Match(text);
            if (subgraph.Success)
            {
                var labelGroup = subgraph.Groups["label"];
                if (labelGroup.Success && labelGroup.Length > 0)
                {
                    var decoded = MakeLabel(text, labelGroup.Index, labelGroup.Index + labelGroup.Length, line.Slice.Start, forceQuoted: true);
                    if (decoded is not null) yield return decoded;
                }
                continue;
            }

            while (offset < text.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (text.AsSpan(offset).StartsWith("%%", StringComparison.Ordinal)) break;
                if (text[offset] == ';' && FlowchartConfiguration.IsMatch(text[(offset + 1)..])) break;
                var edge = offset > 0 && "-.=<>".Contains(text[offset - 1]) ? Match.Empty : FlowchartEdge.Match(text, offset);
                if (edge.Success)
                {
                    var label = edge.Groups["label"];
                    var decoded = MakeLabel(text, label.Index, label.Index + label.Length, line.Slice.Start, forceQuoted: true);
                    if (decoded is not null) yield return decoded;
                    offset += edge.Length;
                    continue;
                }
                var node = FlowchartNode.Match(text, offset);
                if (node.Success)
                {
                    var start = node.Index + node.Length;
                    offset = start;
                    foreach (var shape in FlowchartShapes)
                    {
                        if (!text.AsSpan(start).StartsWith(shape.Open, StringComparison.Ordinal)) continue;
                        start += shape.Open.Length;
                        var end = FindEnd(text, start, shape.Close);
                        if (end < 0) { offset = text.Length; break; }
                        var decoded = MakeLabel(text, start, end, line.Slice.Start, forceQuoted: true);
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
    /// Extracts participant aliases, messages, conditions, notes, and group boxes from sequence diagrams.
    /// </summary>
    /// <param name="block">Sequence diagram code block.</param>
    /// <param name="headerLineIndex">Line index of sequence diagram header.</param>
    /// <param name="cancellationToken">Token for cancelling extraction.</param>
    /// <returns>Ordered sequence diagram labels.</returns>
    private static IEnumerable<MermaidLabel> ExtractSequence(FencedCodeBlock block, int headerLineIndex, CancellationToken cancellationToken)
    {
        for (var lineIndex = headerLineIndex + 1; lineIndex < block.Lines.Count; lineIndex++)
        {
            var line = block.Lines.Lines[lineIndex];
            cancellationToken.ThrowIfCancellationRequested();
            var text = line.Slice.ToString();
            if (string.IsNullOrWhiteSpace(text) || text.TrimStart().StartsWith("%%", StringComparison.Ordinal)) continue;
            var trimmed = text.Trim();
            if (trimmed.StartsWith("autonumber", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("activate", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("deactivate", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("destroy", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "end", StringComparison.OrdinalIgnoreCase)) continue;

            var title = TitlePattern.Match(text);
            if (title.Success)
            {
                var label = ExtractCleanSpan(text, title.Groups["label"], line.Slice.Start, forceQuoted: false);
                if (label is not null) yield return label;
                continue;
            }

            var link = SequenceLink.Match(text);
            if (link.Success)
            {
                var label = ExtractCleanSpan(text, link.Groups["label"], line.Slice.Start, forceQuoted: false);
                if (label is not null) yield return label;
                continue;
            }

            var note = SequenceNote.Match(text);
            if (note.Success)
            {
                var label = ExtractCleanSpan(text, note.Groups["label"], line.Slice.Start, forceQuoted: false);
                if (label is not null) yield return label;
                continue;
            }

            var message = SequenceMessage.Match(text);
            if (message.Success)
            {
                var label = ExtractCleanSpan(text, message.Groups["label"], line.Slice.Start, forceQuoted: false);
                if (label is not null) yield return label;
                continue;
            }

            var participant = SequenceParticipant.Match(text);
            if (participant.Success)
            {
                var label = ExtractCleanSpan(text, participant.Groups["label"], line.Slice.Start, forceQuoted: false);
                if (label is not null) yield return label;
                continue;
            }

            var quotedParticipant = SequenceQuotedParticipant.Match(text);
            if (quotedParticipant.Success)
            {
                var group = quotedParticipant.Groups["label"];
                var label = MakeLabel(text, group.Index - 1, group.Index + group.Length + 1, line.Slice.Start, forceQuoted: true);
                if (label is not null) yield return label;
                continue;
            }

            var blockStatement = SequenceBlock.Match(text);
            if (blockStatement.Success)
            {
                var group = blockStatement.Groups["label"];
                if (group.Success && group.Length > 0)
                {
                    var label = ExtractCleanSpan(text, group, line.Slice.Start, forceQuoted: false);
                    if (label is not null) yield return label;
                }
                continue;
            }

            var box = SequenceBox.Match(text);
            if (box.Success)
            {
                var group = box.Groups["label"];
                if (group.Success && group.Length > 0)
                {
                    var label = ExtractCleanSpan(text, group, line.Slice.Start, forceQuoted: null);
                    if (label is not null) yield return label;
                }
                continue;
            }
        }
    }

    /// <summary>
    /// Extracts transition text and note contents from state diagrams.
    /// </summary>
    /// <param name="block">State diagram code block.</param>
    /// <param name="headerLineIndex">Line index of state diagram header.</param>
    /// <param name="cancellationToken">Token for cancelling extraction.</param>
    /// <returns>Ordered state diagram labels.</returns>
    private static IEnumerable<MermaidLabel> ExtractState(FencedCodeBlock block, int headerLineIndex, CancellationToken cancellationToken)
    {
        for (var lineIndex = headerLineIndex + 1; lineIndex < block.Lines.Count; lineIndex++)
        {
            var line = block.Lines.Lines[lineIndex];
            cancellationToken.ThrowIfCancellationRequested();
            var text = line.Slice.ToString();
            if (string.IsNullOrWhiteSpace(text) || text.TrimStart().StartsWith("%%", StringComparison.Ordinal)) continue;

            var declaration = StateDeclaration.Match(text);
            if (declaration.Success)
            {
                var group = declaration.Groups["label"];
                var label = MakeLabel(text, group.Index - 1, group.Index + group.Length + 1, line.Slice.Start, forceQuoted: true);
                if (label is not null) yield return label;
                continue;
            }

            var transition = StateTransition.Match(text);
            if (transition.Success)
            {
                var label = ExtractCleanSpan(text, transition.Groups["label"], line.Slice.Start, forceQuoted: false);
                if (label is not null) yield return label;
                continue;
            }

            var note = StateNote.Match(text);
            if (note.Success)
            {
                var label = ExtractCleanSpan(text, note.Groups["label"], line.Slice.Start, forceQuoted: false);
                if (label is not null) yield return label;
                continue;
            }
        }
    }

    /// <summary>
    /// Extracts note annotations from class diagrams.
    /// </summary>
    /// <param name="block">Class diagram code block.</param>
    /// <param name="headerLineIndex">Line index of class diagram header.</param>
    /// <param name="cancellationToken">Token for cancelling extraction.</param>
    /// <returns>Ordered class diagram labels.</returns>
    private static IEnumerable<MermaidLabel> ExtractClass(FencedCodeBlock block, int headerLineIndex, CancellationToken cancellationToken)
    {
        for (var lineIndex = headerLineIndex + 1; lineIndex < block.Lines.Count; lineIndex++)
        {
            var line = block.Lines.Lines[lineIndex];
            cancellationToken.ThrowIfCancellationRequested();
            var text = line.Slice.ToString();
            if (string.IsNullOrWhiteSpace(text) || text.TrimStart().StartsWith("%%", StringComparison.Ordinal)) continue;

            var note = ClassNote.Match(text);
            if (note.Success)
            {
                var group = note.Groups["label"];
                var label = MakeLabel(text, group.Index - 1, group.Index + group.Length + 1, line.Slice.Start, forceQuoted: true);
                if (label is not null) yield return label;
            }
        }
    }

    /// <summary>
    /// Extracts relationship labels from entity-relationship diagrams.
    /// </summary>
    /// <param name="block">ER diagram code block.</param>
    /// <param name="headerLineIndex">Line index of ER diagram header.</param>
    /// <param name="cancellationToken">Token for cancelling extraction.</param>
    /// <returns>Ordered ER diagram labels.</returns>
    private static IEnumerable<MermaidLabel> ExtractEr(FencedCodeBlock block, int headerLineIndex, CancellationToken cancellationToken)
    {
        for (var lineIndex = headerLineIndex + 1; lineIndex < block.Lines.Count; lineIndex++)
        {
            var line = block.Lines.Lines[lineIndex];
            cancellationToken.ThrowIfCancellationRequested();
            var text = line.Slice.ToString();
            if (string.IsNullOrWhiteSpace(text) || text.TrimStart().StartsWith("%%", StringComparison.Ordinal)) continue;

            var relationship = ErRelationship.Match(text);
            if (relationship.Success)
            {
                var label = ExtractCleanSpan(text, relationship.Groups["label"], line.Slice.Start, forceQuoted: null);
                if (label is not null) yield return label;
            }
        }
    }

    /// <summary>
    /// Trims line comments and trailing semicolons from matched span before decoding.
    /// </summary>
    /// <param name="text">Full line text.</param>
    /// <param name="group">Matched regex group.</param>
    /// <param name="sourceOffset">Line start offset in source text.</param>
    /// <param name="forceQuoted">Explicit quoting requirement override.</param>
    /// <returns>Decoded Mermaid label or null when empty.</returns>
    private static MermaidLabel? ExtractCleanSpan(string text, Group group, int sourceOffset, bool? forceQuoted)
    {
        var start = group.Index;
        var end = FindStatementEnd(text, start, group.Index + group.Length);
        return MakeLabel(text, start, end, sourceOffset, forceQuoted);
    }

    /// <summary>
    /// Finds statement boundary before inline comments or unencoded statement semicolons.
    /// </summary>
    /// <param name="text">Full line text.</param>
    /// <param name="start">Start offset within line.</param>
    /// <param name="end">End offset within line.</param>
    /// <returns>Adjusted end offset terminating statement content.</returns>
    private static int FindStatementEnd(string text, int start, int end)
    {
        var quoted = false;
        for (var i = start; i < end; i++)
        {
            if (text[i] == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (quoted) continue;
            if (i + 1 < end && text[i] == '%' && text[i + 1] == '%')
                return i;
            if (text[i] == ';')
            {
                var entityStart = text.LastIndexOf('#', i - 1, Math.Min(i - start, 12));
                if (entityStart >= 0 && Entity.IsMatch(text[entityStart..(i + 1)]))
                    continue;
                return i;
            }
        }
        return end;
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
    /// <param name="forceQuoted">Whether label requires quotes on export.</param>
    /// <returns>Decoded label binding, or null for empty or unsupported labels.</returns>
    private static MermaidLabel? MakeLabel(string text, int start, int end, int sourceOffset, bool? forceQuoted = null)
    {
        while (start < end && char.IsWhiteSpace(text[start])) start++;
        while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
        if (start >= end) return null;
        var raw = text[start..end];
        var isQuoted = raw.StartsWith('"') && raw.EndsWith('"') && raw.Length >= 2;
        var value = isQuoted ? raw[1..^1] : raw;
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['<', '`']) >= 0) return null;
        if (!isQuoted && value.IndexOf('"') >= 0) return null;
        value = Entity.Replace(value, match =>
        {
            var entity = match.Groups["entity"].Value;
            if (int.TryParse(entity, NumberStyles.None, CultureInfo.InvariantCulture, out var code) && Rune.IsValid(code))
                return char.ConvertFromUtf32(code);
            return WebUtility.HtmlDecode("&" + entity + ";") is { } decoded && decoded != "&" + entity + ";" ? decoded : match.Value;
        });
        return new(sourceOffset + start, sourceOffset + end, value, forceQuoted ?? isQuoted);
    }

    /// <summary>
    /// Quotes or escapes translated label using Mermaid decimal entities according to quoting requirements.
    /// </summary>
    /// <param name="text">Validated single-line translated label.</param>
    /// <param name="quoted">Whether label requires enclosing double quotes.</param>
    /// <returns>Encoded label safe for diagram syntax.</returns>
    internal static string Encode(string text, bool quoted = true)
    {
        if (quoted)
        {
            var output = new StringBuilder("\"");
            foreach (var rune in text.EnumerateRunes())
            {
                if (Rune.IsLetterOrDigit(rune) || rune.Value == ' ') output.Append(rune);
                else output.Append('#').Append(rune.Value.ToString(CultureInfo.InvariantCulture)).Append(';');
            }
            return output.Append('"').ToString();
        }
        else
        {
            var output = new StringBuilder();
            foreach (var rune in text.EnumerateRunes())
            {
                if (rune.Value is ';' or '#' or '"' or '<' or '>')
                    output.Append('#').Append(rune.Value.ToString(CultureInfo.InvariantCulture)).Append(';');
                else
                    output.Append(rune);
            }
            return output.ToString();
        }
    }
}

/// <summary>
/// Visible Mermaid label bound to exact source content span.
/// </summary>
/// <param name="Start">Inclusive source offset including optional quotes.</param>
/// <param name="End">Exclusive source offset including optional quotes.</param>
/// <param name="Text">Decoded visible text.</param>
/// <param name="Quoted">Whether label syntax requires enclosing double quotes on export.</param>
internal sealed record MermaidLabel(int Start, int End, string Text, bool Quoted = true);

/// <summary>
/// Supported Mermaid diagram categories.
/// </summary>
internal enum MermaidDiagramType
{

    /// <summary>
    /// Unsupported or unrecognized Mermaid diagram.
    /// </summary>
    Unsupported,

    /// <summary>
    /// Flowchart or graph diagram.
    /// </summary>
    Flowchart,

    /// <summary>
    /// Sequence diagram.
    /// </summary>
    Sequence,

    /// <summary>
    /// State diagram.
    /// </summary>
    State,

    /// <summary>
    /// Class diagram.
    /// </summary>
    Class,

    /// <summary>
    /// Entity-relationship diagram.
    /// </summary>
    EntityRelationship
}
