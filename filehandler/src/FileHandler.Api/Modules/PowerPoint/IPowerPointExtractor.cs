using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;

namespace FileHandler.Api.Modules.PowerPoint;

/// <summary>
/// Extracts translation units and semantic structure from PresentationML packages.
/// </summary>
public interface IPowerPointExtractor
{

    /// <summary>
    /// Analyzes PowerPoint presentation and extracts ordered translation units.
    /// </summary>
    /// <param name="source">Source Office document snapshot.</param>
    /// <param name="inventory">Discovered package inventory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted PowerPoint extraction plan.</returns>
    PowerPointPlan Analyze(OfficeSource source, OfficeInventory inventory, CancellationToken cancellationToken);

    /// <summary>
    /// Extracts only selected native objects before assigning unit indices.
    /// </summary>
    /// <param name="source">Source package.</param>
    /// <param name="inventory">Preflight inventory.</param>
    /// <param name="selection">Native object selection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extraction mapping and source metadata.</returns>
    PowerPointPlan Analyze(OfficeSource source, OfficeInventory inventory, PowerPointSelection selection, CancellationToken cancellationToken)
    {
        if (selection.SlideIds is not null) throw new NotSupportedException("Extractor does not implement explicit selection.");
        return Analyze(source, inventory, cancellationToken);
    }
}
