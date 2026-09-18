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
}
