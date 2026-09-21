using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;

namespace FileHandler.Api.Modules.Excel;

/// <summary>
/// Extracts translation units and semantic structure from SpreadsheetML packages.
/// </summary>
public interface IExcelExtractor
{

    /// <summary>
    /// Analyzes Excel workbook and extracts ordered translation units.
    /// </summary>
    /// <param name="source">Source Office document snapshot.</param>
    /// <param name="inventory">Discovered package inventory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted Excel extraction plan.</returns>
    ExcelPlan Analyze(OfficeSource source, OfficeInventory inventory, CancellationToken cancellationToken);

    /// <summary>
    /// Extracts only selected native objects before assigning unit indices.
    /// </summary>
    /// <param name="source">Source package.</param>
    /// <param name="inventory">Preflight inventory.</param>
    /// <param name="selection">Native object selection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extraction mapping and source metadata.</returns>
    ExcelPlan Analyze(OfficeSource source, OfficeInventory inventory, ExcelSelection selection, CancellationToken cancellationToken)
    {
        if (selection.SheetIds is not null) throw new NotSupportedException("Extractor does not implement explicit selection.");
        return Analyze(source, inventory, cancellationToken);
    }
}
