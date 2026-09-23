using System.Text;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Modules.Office;

namespace FileHandler.Api.Modules.Word;

/// <summary>
/// Applies translations through verified scalar bindings and reconstructed inline regions.
/// </summary>
public sealed class WordTranslationApplier
{

    /// <summary>
    /// Derives touched parts from all changed slots.
    /// </summary>
    /// <param name="plan">Original extraction plan.</param>
    /// <param name="decodedUnits">Validated translations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Prepared patch with matching part masks.</returns>
    public WordPreparedPatch Prepare(WordPlan plan, IReadOnlyList<OfficeDecodedUnit> decodedUnits, CancellationToken cancellationToken)
    {
        var masks = new Dictionary<string, OfficeEditMask>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < plan.Units.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OfficeTextBindings.Changed(plan.Units[i], decodedUnits[i]))
            {
                var uri = plan.Units[i].Location.PartUri;
                masks[uri] = new OfficeEditMask(uri, new[] { "//w:t" });
            }
        }
        var editsByPart = OfficeTextBindings.EditsByPart(plan.Units, decodedUnits);
        foreach (var uri in masks.Keys.ToArray())
            masks[uri] = masks[uri] with { ScalarEdits = editsByPart[uri] };
        return new(plan, decodedUnits, masks);
    }

    /// <summary>
    /// Applies nested units before owner reconstruction and serializes changed parts only.
    /// </summary>
    /// <param name="session">Atomic export session.</param>
    /// <param name="doc">Read-only source package.</param>
    /// <param name="patch">Prepared translations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No return value.</returns>
    public void Apply(OfficeExportSession session, WordprocessingDocument doc, WordPreparedPatch patch, CancellationToken cancellationToken)
    {
        IEnumerable<OpenXmlPart> parts = doc.MainDocumentPart is null ? [] : new OpenXmlPart[] { doc.MainDocumentPart }.Concat(doc.MainDocumentPart.HeaderParts).Concat(doc.MainDocumentPart.FooterParts).Concat(new OpenXmlPart?[] { doc.MainDocumentPart.FootnotesPart, doc.MainDocumentPart.EndnotesPart }.OfType<OpenXmlPart>());
        var map = parts.ToDictionary(p => "/" + p.Uri.ToString().TrimStart('/'), StringComparer.OrdinalIgnoreCase);
        var translations = patch.Plan.Units.Select((unit, index) => (Unit: unit, Decoded: patch.DecodedUnits[index]))
            .Where(pair => OfficeTextBindings.Changed(pair.Unit, pair.Decoded));
        foreach (var group in translations.GroupBy(pair => pair.Unit.Location.PartUri, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!map.TryGetValue(group.Key, out var part) || part.RootElement is null)
                throw new InvalidOperationException("Missing target part.");
            OfficeTextBindings.ApplyNested(part.RootElement, group.ToArray(), patch.EditMasks[group.Key], cancellationToken);
        }
        foreach (var uri in patch.EditMasks.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.WritePart(uri, map[uri].RootElement!);
        }
    }
}
