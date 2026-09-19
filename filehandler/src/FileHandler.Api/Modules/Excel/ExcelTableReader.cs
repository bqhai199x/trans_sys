using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace FileHandler.Api.Modules.Excel;

/// <summary>
/// Reads Excel table definitions and identifies protected header and totals rows.
/// </summary>
public sealed class ExcelTableReader
{

    /// <summary>
    /// Reads table definition parts for a worksheet and extracts table snapshots.
    /// </summary>
    /// <param name="sheetPart">Worksheet part.</param>
    /// <param name="partUri">Canonical part URI.</param>
    /// <returns>List of table snapshots.</returns>
    public IReadOnlyList<ExcelTableSnapshot> ReadTables(WorksheetPart sheetPart, string partUri)
    {
        var tables = new List<ExcelTableSnapshot>();
        foreach (var tablePart in sheetPart.TableDefinitionParts)
        {
            var table = tablePart.Table;
            if (table is null)
                continue;

            var name = table.DisplayName?.Value ?? table.Name?.Value ?? "Table";
            var reference = table.Reference?.Value ?? string.Empty;
            var headerCount = (table.HeaderRowCount?.Value is { } h) ? (int)h : 1;
            var totalsCount = (table.TotalsRowCount?.Value is { } t) ? (int)t : (table.TotalsRowShown?.Value == true ? 1 : 0);

            var columnNames = new List<string>();
            if (table.TableColumns is not null)
            {
                foreach (var col in table.TableColumns.Elements<S.TableColumn>())
                {
                    if (col.Name?.Value is { } colName)
                        columnNames.Add(colName);
                }
            }

            tables.Add(new ExcelTableSnapshot(name, partUri, reference, headerCount, totalsCount, columnNames));
        }
        return tables;
    }
}
