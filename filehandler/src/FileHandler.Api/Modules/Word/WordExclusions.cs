using DocumentFormat.OpenXml;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace FileHandler.Api.Modules.Word;

/// <summary>
/// Request-local preserved subtrees shared by all Word traversal branches.
/// </summary>
internal sealed class WordExclusions
{

    /// <summary>
    /// Excluded object roots and stable diagnostic reasons.
    /// </summary>
    private readonly Dictionary<OpenXmlElement, string> _excluded = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Part roots already analyzed for cross-paragraph fields.
    /// </summary>
    private readonly HashSet<OpenXmlElement> _prepared = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Preserved subtree roots already emitted to public metadata.
    /// </summary>
    private readonly HashSet<OpenXmlElement> _reported = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Public diagnostics and compact facts for hidden preserved subtrees.
    /// </summary>
    internal OfficeSkipCollector Skipped { get; }

    /// <summary>
    /// Creates exclusions using request-local diagnostic policy.
    /// </summary>
    /// <param name="source">Source owning preservation and diagnostic context.</param>
    internal WordExclusions(OfficeSource source)
    {
        Skipped = new(source);
    }

    /// <summary>
    /// Tests whether element belongs to a preserved subtree.
    /// </summary>
    /// <param name="element">Element under consideration.</param>
    /// <returns>True when element or any ancestor is excluded.</returns>
    internal bool Contains(OpenXmlElement element) => _excluded.ContainsKey(element) || element.Ancestors().Any(_excluded.ContainsKey);

    /// <summary>
    /// Records a region intentionally omitted by story or container traversal.
    /// </summary>
    /// <param name="element">Preserved source subtree.</param>
    /// <param name="partUri">Actual owning part URI.</param>
    /// <param name="code">Stable exclusion reason.</param>
    /// <param name="severity">Info for policy exclusions, warning for unsupported content.</param>
    /// <param name="scope">Semantic object category.</param>
    /// <returns>No return value.</returns>
    internal void Preserve(OpenXmlElement element, string partUri, string code, string severity, string scope)
    {
        if (Contains(element)) return;
        _excluded.Add(element, code);
        _reported.Add(element);
        Skipped.Region(code, severity, scope, ProcessingMessages.PreservedRegion(code), partUri, element);
    }

    /// <summary>
    /// Builds exclusions once per actual part root before creating units.
    /// </summary>
    /// <param name="root">Actual package part root.</param>
    /// <param name="partUri">Actual package part URI.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>No return value.</returns>
    internal void Prepare(OpenXmlElement root, string partUri, CancellationToken token)
    {
        if (!_prepared.Add(root)) return;
        foreach (var element in root.Descendants())
        {
            token.ThrowIfCancellationRequested();
            if (Contains(element)) continue;
            var reason = element switch
            {
                W.SdtElement sdt when sdt.SdtProperties?.GetFirstChild<W.Lock>() is not null || sdt.SdtProperties?.GetFirstChild<W.DataBinding>() is not null => SkipCodes.ProtectedContentControl,
                W.InsertedRun or W.DeletedRun or W.MoveFromRun or W.MoveToRun => SkipCodes.UnsupportedRevision,
                W.Ruby => SkipCodes.UnsupportedRuby,
                W.AltChunk => SkipCodes.UnsupportedAltchunk,
                AlternateContent => SkipCodes.UnsupportedAlternateContent,
                W.TableCell cell when WordTableReader.IsVerticalMergeContinuation(cell) || cell.TableCellProperties?.HorizontalMerge is { } merge && (merge.Val is null || merge.Val.Value == W.MergedCellValues.Continue) => WordTableReader.HasNonEmptyText(cell) ? SkipCodes.MergedFollowerText : SkipCodes.MergedFollower,
                _ => null
            };
            if (reason is null && element.NamespaceUri == "http://schemas.openxmlformats.org/wordprocessingml/2006/main" &&
                (element.LocalName is "ins" or "del" or "moveFrom" or "moveTo" || element.LocalName.EndsWith("PrChange", StringComparison.Ordinal)))
                reason = SkipCodes.UnsupportedRevision;
            if (reason is not null) _excluded[element] = reason;
        }
        var contexts = new[] { root }.Concat(root.Descendants().Where(e => e is W.TextBoxContent or W.Footnote or W.Endnote));
        foreach (var context in contexts)
        {
            if (Contains(context)) continue;
            var paragraphs = context.Descendants<W.Paragraph>().Where(p => !Contains(p) &&
                (p.Ancestors().FirstOrDefault(a => a is W.TextBoxContent or W.Footnote or W.Endnote) ?? root) == context).ToArray();
            var depth = 0;
            var start = -1;
            var invalid = false;
            for (var i = 0; i < paragraphs.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                foreach (var field in paragraphs[i].Descendants<W.FieldChar>().Where(f => !Contains(f) && f.Ancestors<W.Paragraph>().First() == paragraphs[i]))
                {
                    if (field.FieldCharType?.Value == W.FieldCharValues.Begin)
                    {
                        if (depth++ == 0) start = i;
                    }
                    else if (field.FieldCharType?.Value == W.FieldCharValues.End)
                    {
                        if (depth == 0) { invalid = true; break; }
                        if (--depth == 0 && start != i)
                            for (var index = start; index <= i; index++) _excluded[paragraphs[index]] = SkipCodes.CrossParagraphField;
                    }
                }
                if (invalid) break;
            }
            if (invalid || depth != 0) _excluded[context] = SkipCodes.UnboundedFieldStory;
        }
        foreach (var element in new[] { root }.Concat(root.Descendants()))
        {
            if (!Contains(element) && (element is W.SimpleField or W.Drawing or W.Picture || element is W.FieldChar field && field.FieldCharType?.Value == W.FieldCharValues.Begin))
            {
                var unsupported = element.Descendants().Any(e => e.NamespaceUri.Contains("/chart", StringComparison.Ordinal) || e.NamespaceUri.Contains("/diagram", StringComparison.Ordinal));
                Skipped.Region(unsupported ? SkipCodes.UnsupportedDrawing : SkipCodes.ProtectedInline, unsupported ? SkipSeverity.Warning : SkipSeverity.Info,
                    SkipScope.Inline, unsupported ? ProcessingMessages.UnsupportedDrawing : ProcessingMessages.ProtectedInline, partUri, element);
            }
            if (!_excluded.TryGetValue(element, out var code) || element.Ancestors().Any(_excluded.ContainsKey)) continue;
            var scope = element is W.SdtElement ? SkipScope.ContentControl : element is W.TableCell ? SkipScope.Cell : element is W.Paragraph ? SkipScope.Block : element == root ? SkipScope.Story : SkipScope.Region;
            if (_reported.Add(element))
                Skipped.Region(code, code == SkipCodes.MergedFollower ? SkipSeverity.Info : SkipSeverity.Warning, scope, ProcessingMessages.PreservedRegion(code), partUri, element);
        }
    }
}
