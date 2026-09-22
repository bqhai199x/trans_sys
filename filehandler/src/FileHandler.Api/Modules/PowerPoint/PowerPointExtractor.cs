using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;
using P = DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace FileHandler.Api.Modules.PowerPoint;

/// <summary>
/// Extracts translation units and shapes from PowerPoint presentations.
/// </summary>
public sealed class PowerPointExtractor : IPowerPointExtractor
{

    /// <summary>
    /// Text codec used for canonical unit encoding.
    /// </summary>
    private readonly OfficeTextCodec _codec;

    /// <summary>
    /// DrawingML table reader.
    /// </summary>
    private readonly PowerPointTableReader _tableReader;

    /// <summary>
    /// Configuration options governing limits.
    /// </summary>
    private readonly OfficeProcessingOptions _options;

    /// <summary>
    /// Creates PowerPoint extractor instance.
    /// </summary>
    /// <param name="codec">Office text codec.</param>
    /// <param name="tableReader">PowerPoint table reader.</param>
    /// <param name="options">Office processing options.</param>
    public PowerPointExtractor(OfficeTextCodec codec, PowerPointTableReader tableReader, OfficeProcessingOptions options)
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
    public PowerPointPlan Analyze(OfficeSource source, OfficeInventory inventory, CancellationToken cancellationToken) =>
        Analyze(source, inventory, new PowerPointSelection(null), cancellationToken);

    /// <summary>
    /// Extracts selected source regions while recording preserved exclusions.
    /// </summary>
    /// <param name="source">Source snapshot.</param>
    /// <param name="inventory">Preflight inventory.</param>
    /// <param name="selection">Native identifiers to select.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Source mapping, inventory and exclusions.</returns>
    public PowerPointPlan Analyze(OfficeSource source, OfficeInventory inventory, PowerPointSelection selection, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream(source.Bytes);
        using var doc = PresentationDocument.Open(ms, false, OfficeTextBindings.Settings(_options));

        if (doc.PresentationPart?.Presentation?.SlideIdList is null)
            throw new InvalidDataException("PowerPoint package missing presentation or slide list.");

        var units = new OfficeUnitCollection(_options, _codec.MaxUnits);
        var slides = new List<PowerPointSlideSnapshot>();

        var catalog = OfficeCatalog.Slides(doc, cancellationToken).ToArray();
        var selectedIds = selection.SlideIds?.ToHashSet(StringComparer.Ordinal);
        if (selectedIds is not null && selectedIds.Except(catalog.Select(s => s.SlideId)).Any())
            throw new UnknownSelectionException(FileMetadata.Create("powerpoint") with { Slides = catalog });
        var skipped = new OfficeSkipCollector(source);
        source.ProcessingMetadata = FileMetadata.Create("powerpoint") with { Slides = catalog, Skipped = skipped };
        var slideIndex = 0;

        foreach (var slideId in doc.PresentationPart.Presentation.SlideIdList.Elements<SlideId>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            slideIndex++;

            var descriptor = catalog[slideIndex - 1];
            var selected = selectedIds?.Contains(descriptor.SlideId) ?? !descriptor.Hidden;
            descriptor = descriptor with { Selected = selected };
            catalog[slideIndex - 1] = descriptor;
            var idVal = descriptor.SlideId;
            var relId = slideId.RelationshipId?.Value;

            if (string.IsNullOrEmpty(relId) || !doc.PresentationPart.TryGetPartById(relId, out var part) || part is not SlidePart slidePart)
                continue;

            var isHidden = slidePart.Slide?.Show?.Value == false;
            var slideUri = "/" + slidePart.Uri.ToString().TrimStart('/');
            var shapeTree = slidePart.Slide?.CommonSlideData?.ShapeTree;
            var shapeCount = shapeTree?.Descendants<P.Shape>().Count() ?? 0;

            if (!selected || shapeTree is null)
            {
                skipped.Info(SkipCodes.SlideNotSelected, SkipStage.Selection, SkipScope.Slide, ProcessingMessages.SlideNotSelected, slideUri, slideId: idVal);
                slides.Add(new PowerPointSlideSnapshot(idVal, slideUri, !isHidden, shapeCount));
                continue;
            }

            var before = units.Count;
            WalkShapeTree(shapeTree, slideUri, idVal, units, skipped, cancellationToken);
            catalog[slideIndex - 1] = descriptor with { UnitStartIndex = before, UnitEndIndex = units.Count };
            slides.Add(new PowerPointSlideSnapshot(idVal, slideUri, !isHidden, shapeCount));
        }

        long totalPlanChars = 0;
        foreach (var u in units)
            totalPlanChars += u.EncodedSource.Length;

        if (totalPlanChars > _options.MaxPlanChars)
            throw new FileLimitException("office_plan_limit_exceeded");

        if (units.Count > _options.MaxObjects || units.Any(u => u.Slots.Count + u.Anchors.Count > _options.MaxTokensPerUnit))
            throw new FileLimitException("office_plan_limit_exceeded");

        return new PowerPointPlan(source.SourceHash, units, slides) { Metadata = OfficeMetadata.Describe("powerpoint", units) with { Slides = catalog, Skipped = skipped, Status = ProcessingStatus.Resolve(skipped) } };
    }

    /// <summary>
    /// Recursively walks shape tree elements extracting text from shapes and tables.
    /// </summary>
    /// <param name="container">Shape container element.</param>
    /// <param name="slideUri">Canonical slide part URI.</param>
    /// <param name="slideId">Slide identifier string.</param>
    /// <param name="units">Accumulated units collection.</param>
    /// <param name="skipped">Preserved source regions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No return value.</returns>
    private void WalkShapeTree(
        OpenXmlElement container,
        string slideUri,
        string slideId,
        OfficeUnitCollection units,
        OfficeSkipCollector skipped,
        CancellationToken cancellationToken)
    {
        var shapeOrdinal = 0;
        foreach (var child in container.ChildElements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (child is P.Shape sp)
            {
                shapeOrdinal++;
                var shapeId = sp.NonVisualShapeProperties?.NonVisualDrawingProperties?.Id?.Value.ToString() ?? shapeOrdinal.ToString();
                var textBody = sp.TextBody;
                if (textBody is null)
                    continue;

                var shapeLoc = new OfficeLocation(slideUri, Array.Empty<OfficeElementPathSegment>(), SlideId: slideId, ShapeId: shapeId);
                var paraOrdinal = 0;

                foreach (var p in textBody.Elements<A.Paragraph>())
                {
                    paraOrdinal++;
                    var template = DrawingTextCodec.ReadParagraph(p, shapeLoc, paraOrdinal, _options);
                    if (template is not null)
                    {
                        var unitId = OfficeIdentity.CreateUnitId(
                            "office-v1",
                            string.Empty,
                            OfficeFormat.PowerPoint,
                            slideUri,
                            OfficeObjectKind.Paragraph,
                            Array.Empty<OfficeElementPathSegment>(),
                            units.Count);

                        var plainSb = new StringBuilder();
                        foreach (var s in template.Slots) plainSb.Append(s.OriginalText);
                        var plainHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plainSb.ToString())));

                        var unit = new OfficeTranslationUnit(
                            units.Count,
                            unitId,
                            unitId,
                            OfficeMetadata.At(shapeLoc, p),
                            template.Mode,
                            _codec.Encode(template),
                            template.Slots,
                            template.Anchors,
                            template.Bindings,
                            plainHash);

                        units.Add(unit);
                    }
                }
            }
            else if (child is P.GroupShape grp)
            {
                WalkShapeTree(grp, slideUri, slideId, units, skipped, cancellationToken);
            }
            else if (child is P.GraphicFrame gf)
            {
                shapeOrdinal++;
                var gfId = gf.NonVisualGraphicFrameProperties?.NonVisualDrawingProperties?.Id?.Value.ToString() ?? shapeOrdinal.ToString();
                var table = gf.Descendants<A.Table>().FirstOrDefault();
                if (table is not null)
                {
                    var tableLoc = new OfficeLocation(slideUri, Array.Empty<OfficeElementPathSegment>(), SlideId: slideId, ShapeId: gfId);
                    _tableReader.ReadTable(table, tableLoc, units, _codec, _options, skipped.Entries);
                }
                else
                {
                    skipped.Add(new(SkipCodes.UnsupportedGraphicFrame, SkipSeverity.Warning, SkipStage.Extraction, SkipScope.Shape, 1, ProcessingMessages.UnsupportedGraphicFrame, OfficeMetadata.Location(OfficeMetadata.At(new(slideUri, [], SlideId: slideId, ShapeId: gfId), gf))));
                }
            }
        }
    }
}
