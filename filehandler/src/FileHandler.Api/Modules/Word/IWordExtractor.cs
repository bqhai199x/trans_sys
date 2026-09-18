using FileHandler.Api.Modules.Office;

namespace FileHandler.Api.Modules.Word;

/// <summary>
/// Extracts translation units and semantic structure from WordprocessingML packages.
/// </summary>
public interface IWordExtractor
{

    /// <summary>
    /// Analyzes Word package and extracts ordered translation units.
    /// </summary>
    /// <param name="source">Source Office document snapshot.</param>
    /// <param name="inventory">Discovered package inventory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted Word extraction plan.</returns>
    WordPlan Analyze(OfficeSource source, OfficeInventory inventory, CancellationToken cancellationToken);
}
