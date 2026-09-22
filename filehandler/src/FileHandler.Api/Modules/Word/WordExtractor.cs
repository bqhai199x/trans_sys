using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;
using W = DocumentFormat.OpenXml.Wordprocessing;
using V = DocumentFormat.OpenXml.Vml;
using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace FileHandler.Api.Modules.Word;

/// <summary>
/// Extracts translation units and semantic structure from Word documents.
/// </summary>
public sealed class WordExtractor : IWordExtractor
{

    /// <summary>
    /// Text codec used for canonical unit encoding.
    /// </summary>
    private readonly OfficeTextCodec _codec;

    /// <summary>
    /// Table topology reader.
    /// </summary>
    private readonly WordTableReader _tableReader;

    /// <summary>
    /// Configuration options governing limits.
    /// </summary>
    private readonly OfficeProcessingOptions _options;

    /// <summary>
    /// Creates Word extractor instance.
    /// </summary>
    /// <param name="codec">Office text codec.</param>
    /// <param name="tableReader">Word table reader.</param>
    /// <param name="options">Office processing options.</param>
    public WordExtractor(OfficeTextCodec codec, WordTableReader tableReader, OfficeProcessingOptions options)
    {
        _codec = codec;
        _tableReader = tableReader;
        _options = options;
    }

    /// <summary>
    /// Extracts bounded translation units and document topology.
    /// </summary>
    /// <param name="source">Immutable source snapshot.</param>
    /// <param name="inventory">Preflight package inventory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extraction plan.</returns>
    public WordPlan Analyze(OfficeSource source, OfficeInventory inventory, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream(source.Bytes);
        using var doc = WordprocessingDocument.Open(ms, false, OfficeTextBindings.Settings(_options));

        if (doc.MainDocumentPart is null)
            throw new InvalidDataException("Word document missing main document part.");

        var exclusions = new WordExclusions(source);
        source.ProcessingMetadata = FileMetadata.Create("word") with { Skipped = exclusions.Skipped };

        var units = new OfficeUnitCollection(_options, _codec.MaxUnits);
        var stories = new List<WordStorySnapshot>();
        var tables = new List<WordTableSnapshot>();

        var mainUri = "/" + doc.MainDocumentPart.Uri.ToString().TrimStart('/');
        stories.Add(new WordStorySnapshot("Body", mainUri, 1));

        // 1. Process Main Document Body
        var body = doc.MainDocumentPart.Document?.Body;
        if (body is not null)
        {
            WalkContainerElements(body, mainUri, doc.MainDocumentPart.RootElement!, units, tables, exclusions, cancellationToken);
        }

        // 2. Process Headers in section order
        var visitedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var headerPart in doc.MainDocumentPart.Document!.Descendants<W.HeaderReference>()
            .Select(r => doc.MainDocumentPart.GetPartById(r.Id!) as HeaderPart).OfType<HeaderPart>())
        {
            var hUri = "/" + headerPart.Uri.ToString().TrimStart('/');
            if (visitedHeaders.Add(hUri) && headerPart.Header is not null)
            {
                stories.Add(new WordStorySnapshot("Header", hUri, 1));
                WalkContainerElements(headerPart.Header, hUri, headerPart.RootElement!, units, tables, exclusions, cancellationToken);
            }
        }

        // 3. Process Footers in section order
        var visitedFooters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var footerPart in doc.MainDocumentPart.Document!.Descendants<W.FooterReference>()
            .Select(r => doc.MainDocumentPart.GetPartById(r.Id!) as FooterPart).OfType<FooterPart>())
        {
            var fUri = "/" + footerPart.Uri.ToString().TrimStart('/');
            if (visitedFooters.Add(fUri) && footerPart.Footer is not null)
            {
                stories.Add(new WordStorySnapshot("Footer", fUri, 1));
                WalkContainerElements(footerPart.Footer, fUri, footerPart.RootElement!, units, tables, exclusions, cancellationToken);
            }
        }

        // 4. Process Footnotes
        if (doc.MainDocumentPart.FootnotesPart?.Footnotes is not null)
        {
            var fnUri = "/" + doc.MainDocumentPart.FootnotesPart.Uri.ToString().TrimStart('/');
            stories.Add(new WordStorySnapshot("Footnotes", fnUri, 1));
            var footnoteMap = doc.MainDocumentPart.FootnotesPart.Footnotes.Elements<W.Footnote>().ToDictionary(n => n.Id!.Value);
            var referencedIds = doc.MainDocumentPart.Document!.Descendants<W.FootnoteReference>().Select(r => r.Id!.Value).Distinct().ToArray();
            var referencedSet = referencedIds.ToHashSet();
            foreach (var (noteId, note) in footnoteMap)
                if (!referencedSet.Contains(noteId)) exclusions.Preserve(note, fnUri, SkipCodes.UnreferencedStory, SkipSeverity.Info, SkipScope.Story);
            foreach (var noteId in referencedIds)
            {
                if (!footnoteMap.TryGetValue(noteId, out var footnote)) throw new InvalidDataException("Missing referenced footnote.");
                var type = footnote.Type?.Value;
                if (type == W.FootnoteEndnoteValues.Separator || type == W.FootnoteEndnoteValues.ContinuationSeparator)
                {
                    exclusions.Preserve(footnote, fnUri, SkipCodes.SystemNote, SkipSeverity.Info, SkipScope.Story);
                    continue;
                }
                WalkContainerElements(footnote, fnUri, doc.MainDocumentPart.FootnotesPart.RootElement!, units, tables, exclusions, cancellationToken);
            }
        }

        // 5. Process Endnotes
        if (doc.MainDocumentPart.EndnotesPart?.Endnotes is not null)
        {
            var enUri = "/" + doc.MainDocumentPart.EndnotesPart.Uri.ToString().TrimStart('/');
            stories.Add(new WordStorySnapshot("Endnotes", enUri, 1));
            var endnoteMap = doc.MainDocumentPart.EndnotesPart.Endnotes.Elements<W.Endnote>().ToDictionary(n => n.Id!.Value);
            var referencedIds = doc.MainDocumentPart.Document!.Descendants<W.EndnoteReference>().Select(r => r.Id!.Value).Distinct().ToArray();
            var referencedSet = referencedIds.ToHashSet();
            foreach (var (noteId, note) in endnoteMap)
                if (!referencedSet.Contains(noteId)) exclusions.Preserve(note, enUri, SkipCodes.UnreferencedStory, SkipSeverity.Info, SkipScope.Story);
            foreach (var noteId in referencedIds)
            {
                if (!endnoteMap.TryGetValue(noteId, out var endnote)) throw new InvalidDataException("Missing referenced endnote.");
                var type = endnote.Type?.Value;
                if (type == W.FootnoteEndnoteValues.Separator || type == W.FootnoteEndnoteValues.ContinuationSeparator)
                {
                    exclusions.Preserve(endnote, enUri, SkipCodes.SystemNote, SkipSeverity.Info, SkipScope.Story);
                    continue;
                }
                WalkContainerElements(endnote, enUri, doc.MainDocumentPart.EndnotesPart.RootElement!, units, tables, exclusions, cancellationToken);
            }
        }

        foreach (var part in doc.MainDocumentPart.HeaderParts)
            if (!visitedHeaders.Contains(part.Uri.ToString()) && part.Header is { } header)
                exclusions.Preserve(header, part.Uri.ToString(), SkipCodes.UnreferencedStory, SkipSeverity.Info, SkipScope.Story);
        foreach (var part in doc.MainDocumentPart.FooterParts)
            if (!visitedFooters.Contains(part.Uri.ToString()) && part.Footer is { } footer)
                exclusions.Preserve(footer, part.Uri.ToString(), SkipCodes.UnreferencedStory, SkipSeverity.Info, SkipScope.Story);

        tables.Clear();
        var storyParts = new OpenXmlPart[] { doc.MainDocumentPart }.Concat(doc.MainDocumentPart.HeaderParts)
            .Concat(doc.MainDocumentPart.FooterParts)
            .Concat(new OpenXmlPart?[] { doc.MainDocumentPart.FootnotesPart, doc.MainDocumentPart.EndnotesPart }.OfType<OpenXmlPart>())
            .ToDictionary(p => p.Uri.ToString());
        foreach (var story in stories)
            foreach (var table in storyParts[story.PartUri].RootElement!.Descendants<W.Table>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                tables.Add(_tableReader.Read(table, new OfficeLocation(story.PartUri, OfficeTextBindings.Path(table))));
            }
        long totalPlanChars = 0;
        foreach (var u in units)
        {
            totalPlanChars += u.EncodedSource.Length;
        }

        if (totalPlanChars > _options.MaxPlanChars)
            throw new FileLimitException("office_plan_limit_exceeded");

        if (units.Count > _options.MaxObjects || units.Any(u => u.Slots.Count + u.Anchors.Count > _options.MaxTokensPerUnit))
            throw new FileLimitException("office_plan_limit_exceeded");

        return new WordPlan(source.SourceHash, units, stories, tables) { Metadata = OfficeMetadata.Describe("word", units) with { Skipped = exclusions.Skipped, Status = ProcessingStatus.Resolve(exclusions.Skipped) } };
    }

    /// <summary>
    /// Walks child elements of a container (body, header, cell, footnote) in document order.
    /// </summary>
    /// <param name="container">Containing OpenXML element.</param>
    /// <param name="partUri">Canonical URI of containing part.</param>
    /// <param name="partRoot">Root Open XML element of containing part.</param>
    /// <param name="units">Accumulated units collection.</param>
    /// <param name="tables">Accumulated table snapshots.</param>
    /// <param name="exclusions">Shared preserved subtree map.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No return value.</returns>
    private void WalkContainerElements(
        OpenXmlElement container,
        string partUri,
        OpenXmlElement partRoot,
        OfficeUnitCollection units,
        List<WordTableSnapshot> tables,
        WordExclusions exclusions,
        CancellationToken cancellationToken)
    {
        exclusions.Prepare(partRoot, partUri, cancellationToken);
        if (exclusions.Contains(container)) return;
        foreach (var child in container.ChildElements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (exclusions.Contains(child)) continue;

            if (child is W.Paragraph p)
            {
                ProcessParagraph(p, partUri, partRoot, units, tables, exclusions, cancellationToken);
            }
            else if (child is W.Table tbl)
            {
                ProcessTable(tbl, partUri, partRoot, units, tables, exclusions, cancellationToken);
            }
            else if (child is W.SdtBlock sdt)
            {
                if (sdt.SdtContentBlock is not null)
                {
                    WalkContainerElements(sdt.SdtContentBlock, partUri, partRoot, units, tables, exclusions, cancellationToken);
                }
            }
            else if (child.Descendants<W.Text>().Any(t => !string.IsNullOrWhiteSpace(t.Text)))
            {
                exclusions.Preserve(child, partUri, SkipCodes.UnsupportedBlock, SkipSeverity.Warning, SkipScope.Block);
            }
        }
    }

    /// <summary>
    /// Processes a single paragraph and extracts a translation unit if translatable content is found.
    /// </summary>
    /// <param name="paragraph">Word paragraph element.</param>
    /// <param name="partUri">Canonical part URI.</param>
    /// <param name="partRoot">Root Open XML element of containing part.</param>
    /// <param name="units">Accumulated units collection.</param>
    /// <param name="tables">Collected table topology.</param>
    /// <param name="exclusions">Shared preserved subtree map.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No return value.</returns>
    private void ProcessParagraph(
        W.Paragraph paragraph,
        string partUri,
        OpenXmlElement partRoot,
        OfficeUnitCollection units,
        List<WordTableSnapshot> tables,
        WordExclusions exclusions,
        CancellationToken cancellationToken)
    {
        if (exclusions.Contains(paragraph)) return;
        var builder = new OfficeTemplateBuilder(_options);
        var fieldDepth = 0;
        ReadInline(paragraph, partUri, builder, exclusions, ref fieldDepth);
        if (fieldDepth != 0)
            throw new InvalidOperationException("Fields crossing paragraph boundaries are unsupported.");
        var template = builder.Build();
        if (template is not null)
        {
            var path = OfficeTextBindings.Path(paragraph);
            var id = OfficeIdentity.CreateUnitId("office-v1", string.Empty, OfficeFormat.Word,
                partUri, OfficeObjectKind.Paragraph, path, units.Count);
            units.Add(new OfficeTranslationUnit(units.Count, id, id, new OfficeLocation(partUri, path) { Root = new(partRoot.NamespaceUri, partRoot.LocalName, 1) },
                template.Mode, _codec.Encode(template), template.Slots, template.Anchors, template.Bindings,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(template.Slots.Select(s => s.OriginalText)))))));
        }
        foreach (var textbox in paragraph.Descendants<W.TextBoxContent>().Where(t => t.Ancestors<W.Paragraph>().FirstOrDefault() == paragraph && !exclusions.Contains(t)))
            WalkContainerElements(textbox, partUri, partRoot, units, tables, exclusions, cancellationToken);
    }

    /// <summary>
    /// Visits inline owners while protecting field results and embedded objects.
    /// </summary>
    /// <param name="container">Current inline owner.</param>
    /// <param name="partUri">Containing part URI.</param>
    /// <param name="builder">Ordered template builder.</param>
    /// <param name="exclusions">Shared preserved subtree map.</param>
    /// <param name="fieldDepth">Open complex field depth.</param>
    /// <returns>No return value.</returns>
    private static void ReadInline(OpenXmlElement container, string partUri, OfficeTemplateBuilder builder, WordExclusions exclusions, ref int fieldDepth)
    {
        foreach (var child in container.ChildElements)
        {
            if (exclusions.Contains(child))
                builder.Anchor(child, AnchorKind.Marker);
            else if (child is W.FieldChar field)
            {
                if (field.FieldCharType?.Value == W.FieldCharValues.Begin) fieldDepth++;
                else if (field.FieldCharType?.Value == W.FieldCharValues.End)
                {
                    if (fieldDepth == 0) throw new InvalidOperationException("Unbalanced field.");
                    fieldDepth--;
                }
                builder.Anchor(child, AnchorKind.Field);
            }
            else if (fieldDepth > 0 && child is not W.Run)
                builder.Anchor(child, AnchorKind.Field);
            else if (child is W.Text text)
            {
                var run = text.Parent as W.Run;
                var owner = run?.Parent;
                var context = owner is W.Hyperlink ? string.Concat(owner.GetAttributes().Select(a => $"{a.NamespaceUri}:{a.LocalName}:{a.Value?.Length ?? 0}:{a.Value}")) +
                    string.Join("/", OfficeTextBindings.Path(owner).Select(p => p.SiblingOrdinal)) : "";
                builder.Text(text, partUri, OfficeStyleFingerprint.Create(run?.RunProperties) + context);
            }
            else if (child is W.SimpleField or W.FieldCode)
                builder.Anchor(child, AnchorKind.Field);
            else if (child is W.Break or W.CarriageReturn)
                builder.Anchor(child, AnchorKind.Break);
            else if (child is W.TabChar)
                builder.Anchor(child, AnchorKind.Tab);
            else if (child is W.Drawing or W.Picture || child.NamespaceUri.Contains("vml", StringComparison.Ordinal))
                builder.Anchor(child, AnchorKind.Picture);
            else if (child is W.Run or W.Hyperlink or W.SdtRun or W.SdtContentRun)
                ReadInline(child, partUri, builder, exclusions, ref fieldDepth);
            else if (child is not W.RunProperties and not W.ParagraphProperties)
                builder.Anchor(child, AnchorKind.Marker);
        }
    }

    /// <summary>
    /// Processes a Word table, analyzing dimensions and walking contained cell paragraphs.
    /// </summary>
    /// <param name="table">Word table element.</param>
    /// <param name="partUri">Canonical part URI.</param>
    /// <param name="partRoot">Root Open XML element of containing part.</param>
    /// <param name="units">Accumulated units collection.</param>
    /// <param name="tables">Accumulated table snapshots.</param>
    /// <param name="exclusions">Shared preserved subtree map.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No return value.</returns>
    private void ProcessTable(
        W.Table table,
        string partUri,
        OpenXmlElement partRoot,
        OfficeUnitCollection units,
        List<WordTableSnapshot> tables,
        WordExclusions exclusions,
        CancellationToken cancellationToken)
    {
        var tableLoc = new OfficeLocation(partUri, BuildElementPath(table, partRoot));
        var snapshot = _tableReader.Read(table, tableLoc);
        tables.Add(snapshot);

        foreach (var row in table.Elements<W.TableRow>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var cell in row.Elements<W.TableCell>())
            {
                if (exclusions.Contains(cell)) continue;
                // Preserve merge continuation cells.
                var horizontal = cell.TableCellProperties?.HorizontalMerge;
                if (WordTableReader.IsVerticalMergeContinuation(cell) ||
                    horizontal is not null && (horizontal.Val is null || horizontal.Val.Value == W.MergedCellValues.Continue))
                {
                    continue;
                }


                WalkContainerElements(cell, partUri, partRoot, units, tables, exclusions, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Builds hierarchical path from part root element down to target element.
    /// </summary>
    /// <param name="element">Target Open XML element.</param>
    /// <param name="root">Root Open XML element of part.</param>
    /// <returns>Ordered element path segments from root to target element.</returns>
    private static IReadOnlyList<OfficeElementPathSegment> BuildElementPath(OpenXmlElement element, OpenXmlElement root)
    {
        return OfficeTextBindings.Path(element);
    }

}
