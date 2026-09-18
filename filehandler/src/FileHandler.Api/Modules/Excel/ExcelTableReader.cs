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

    /// <summary>
    /// Checks whether a cell reference falls within protected header or totals rows of defined tables.
    /// </summary>
    /// <param name="cellReference">A1-style cell reference.</param>
    /// <param name="tables">Discovered worksheet table snapshots.</param>
    /// <returns>True when cell is part of table header or totals row; otherwise false.</returns>
    public static bool IsProtectedTableIdentifier(string cellReference, IReadOnlyList<ExcelTableSnapshot> tables)
    {
        if (!ExcelCellResolver.TryParseCoordinates(cellReference, out var col, out var row))
            return false;

        foreach (var tbl in tables)
        {
            if (string.IsNullOrEmpty(tbl.Reference))
                continue;

            var parts = tbl.Reference.Split(':');
            if (parts.Length != 2)
                continue;

            if (!ExcelCellResolver.TryParseCoordinates(parts[0], out var startCol, out var startRow) ||
                !ExcelCellResolver.TryParseCoordinates(parts[1], out var endCol, out var endRow))
                continue;

            if (col < startCol || col > endCol)
                continue;

            // Check if cell falls in header rows
            if (tbl.HeaderRowCount > 0 && row >= startRow && row < startRow + tbl.HeaderRowCount)
                return true;

            // Check if cell falls in totals rows
            if (tbl.TotalsRowCount > 0 && row <= endRow && row > endRow - tbl.TotalsRowCount)
                return true;
        }

        return false;
    }
}
