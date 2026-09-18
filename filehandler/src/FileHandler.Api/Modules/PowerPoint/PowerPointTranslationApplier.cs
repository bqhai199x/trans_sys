using System.Text;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Modules.Office;

namespace FileHandler.Api.Modules.PowerPoint;

/// <summary>
/// Applies changed translations exclusively through verified scalar bindings.
/// </summary>
public sealed class PowerPointTranslationApplier
{

    /// <summary>
    /// Derives touched parts from all changed slots.
    /// </summary>
    /// <param name="plan">Original extraction plan.</param>
    /// <param name="decodedUnits">Validated translations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Prepared patch with matching part masks.</returns>
    public PowerPointPreparedPatch Prepare(PowerPointPlan plan, IReadOnlyList<OfficeDecodedUnit> decodedUnits, CancellationToken cancellationToken)
    {
        var masks = new Dictionary<string, OfficeEditMask>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < plan.Units.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OfficeTextBindings.Changed(plan.Units[i], decodedUnits[i]))
            {
                var uri = plan.Units[i].Location.PartUri;
                masks[uri] = new OfficeEditMask(uri, new[] { "//a:t" });
            }
        }
        foreach (var uri in masks.Keys.ToArray())
            masks[uri] = masks[uri] with { ScalarEdits = OfficeTextBindings.Edits(plan.Units, decodedUnits, uri) };
        return new(plan, decodedUnits, masks);
    }

    /// <summary>
    /// Patches bound scalars and serializes changed parts only.
    /// </summary>
    /// <param name="session">Atomic export session.</param>
    /// <param name="doc">Read-only source package.</param>
    /// <param name="patch">Prepared translations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No return value.</returns>
    public void Apply(OfficeExportSession session, PresentationDocument doc, PowerPointPreparedPatch patch, CancellationToken cancellationToken)
    {
        IEnumerable<OpenXmlPart> parts = doc.PresentationPart?.SlideParts.Cast<OpenXmlPart>() ?? [];
        var map = parts.ToDictionary(p => "/" + p.Uri.ToString().TrimStart('/'), StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < patch.Plan.Units.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var unit = patch.Plan.Units[i];
            var decoded = patch.DecodedUnits[i];
            if (!OfficeTextBindings.Changed(unit, decoded)) continue;
            if (!map.TryGetValue(unit.Location.PartUri, out var part) || part.RootElement is null)
                throw new InvalidOperationException("Missing target part.");
            OfficeTextBindings.Apply(part.RootElement, unit, decoded);
        }
        foreach (var uri in patch.EditMasks.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.WritePart(uri, map[uri].RootElement!);
        }
    }
}
