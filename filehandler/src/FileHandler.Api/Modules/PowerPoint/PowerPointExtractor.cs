using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using FileHandler.Api.Common;
using FileHandler.Api.Diagnostics;
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
    public PowerPointPlan Analyze(OfficeSource source, OfficeInventory inventory, CancellationToken cancellationToken)
    {
        using var trace = DebugTrace.Enter("PowerPointExtractor", "Analyze", () => new { sourceHash = source.SourceHash });

        try
        {
            trace.State("stage", () => "selectSlides");
            using var ms = new MemoryStream(source.OriginalBytes);
            using var doc = PresentationDocument.Open(ms, false, OfficeTextBindings.Settings(_options));

            if (doc.PresentationPart?.Presentation?.SlideIdList is null)
                throw new InvalidOperationException("PowerPoint package missing presentation or slide list.");

            var units = new OfficeUnitCollection(_options);
            var slides = new List<PowerPointSlideSnapshot>();

            trace.State("stage", () => "walkShapes");
            var slideIndex = 0;

            foreach (var slideId in doc.PresentationPart.Presentation.SlideIdList.Elements<SlideId>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                slideIndex++;

                var idVal = slideId.Id?.Value.ToString() ?? slideIndex.ToString();
                var relId = slideId.RelationshipId?.Value;

                if (string.IsNullOrEmpty(relId) || !doc.PresentationPart.TryGetPartById(relId, out var part) || part is not SlidePart slidePart)
                    continue;

                var isHidden = slidePart.Slide?.Show?.Value == false;
                var slideUri = "/" + slidePart.Uri.ToString().TrimStart('/');
                var shapeTree = slidePart.Slide?.CommonSlideData?.ShapeTree;
                var shapeCount = shapeTree?.Descendants<P.Shape>().Count() ?? 0;

                using var slideItem = DebugTrace.Item(slideIndex);
                slideItem.State("slide", () => new
                {
                    slideId = idVal,
                    uri = slideUri,
                    show = !isHidden,
                    shapeCount
                });

                if (isHidden || shapeTree is null)
                {
                    slides.Add(new PowerPointSlideSnapshot(idVal, slideUri, !isHidden, shapeCount));
                    continue;
                }

                // Check for charts or diagrams on this visible slide
                if (slidePart.ChartParts.Any() || slidePart.DiagramDataParts.Any())
                {
                    throw new InvalidOperationException("Slide contains Chart or SmartArt diagrams which are not supported for translation in office-v1.");
                }

                WalkShapeTree(shapeTree, slideUri, idVal, units, cancellationToken);
                slides.Add(new PowerPointSlideSnapshot(idVal, slideUri, true, shapeCount));
            }

            trace.State("stage", () => "buildUnits");
            long totalPlanChars = 0;
            foreach (var u in units)
                totalPlanChars += u.EncodedSource.Length;

            if (totalPlanChars > _options.MaxPlanChars)
                throw new FileLimitException("office_plan_limit_exceeded");

            if (units.Count > _options.MaxObjects || units.Any(u => u.Slots.Count + u.Anchors.Count > _options.MaxTokensPerUnit))
                throw new FileLimitException("office_plan_limit_exceeded");

            trace.Return(new { outcome = "success", unitCount = units.Count, slideCount = slides.Count });
            return new PowerPointPlan(source.SourceHash, units, slides);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            trace.Error(ex);
            throw;
        }
    }

    /// <summary>
    /// Recursively walks shape tree elements extracting text from shapes and tables.
    /// </summary>
    /// <param name="container">Shape container element.</param>
    /// <param name="slideUri">Canonical slide part URI.</param>
    /// <param name="slideId">Slide identifier string.</param>
    /// <param name="units">Accumulated units collection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No return value.</returns>
    private void WalkShapeTree(
        OpenXmlElement container,
        string slideUri,
        string slideId,
        OfficeUnitCollection units,
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
                            shapeLoc,
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
                WalkShapeTree(grp, slideUri, slideId, units, cancellationToken);
            }
            else if (child is P.GraphicFrame gf)
            {
                shapeOrdinal++;
                var gfId = gf.NonVisualGraphicFrameProperties?.NonVisualDrawingProperties?.Id?.Value.ToString() ?? shapeOrdinal.ToString();
                var table = gf.Descendants<A.Table>().FirstOrDefault();
                if (table is not null)
                {
                    var tableLoc = new OfficeLocation(slideUri, Array.Empty<OfficeElementPathSegment>(), SlideId: slideId, ShapeId: gfId);
                    _tableReader.ReadTable(table, tableLoc, units, _codec, _options);
                }
                else if (gf.Descendants<A.GraphicData>().Any(gd => gd.Uri?.Value?.Contains("chart") == true || gd.Uri?.Value?.Contains("diagram") == true))
                {
                    throw new InvalidOperationException("GraphicFrame contains Chart or Diagram which is not supported in office-v1.");
                }
            }
        }
    }
}
