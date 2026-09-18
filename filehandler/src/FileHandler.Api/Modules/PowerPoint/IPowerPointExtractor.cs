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
}
