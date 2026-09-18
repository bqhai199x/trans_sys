using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace FileHandler.Api.Modules.Excel;

/// <summary>
/// Resolves cell coordinates, data types, and shared or inline string contents.
/// </summary>
public static class ExcelCellResolver
{

    /// <summary>
    /// Parses cell address into column and row numbers for numeric sorting.
    /// </summary>
    /// <param name="cellReference">A1-style cell reference.</param>
    /// <param name="columnNumber">One-based column number.</param>
    /// <param name="rowNumber">One-based row number.</param>
    /// <returns>True when cell reference was parsed successfully; otherwise false.</returns>
    public static bool TryParseCoordinates(string? cellReference, out int columnNumber, out int rowNumber)
    {
        columnNumber = 0;
        rowNumber = 0;
        if (string.IsNullOrEmpty(cellReference))
            return false;

        var i = 0;
        while (i < cellReference.Length && char.IsAsciiLetter(cellReference[i]))
        {
            var val = char.ToUpperInvariant(cellReference[i]) - 'A' + 1;
            columnNumber = columnNumber * 26 + val;
            if (columnNumber > 16384) return false;
            i++;
        }

        if (i == 0 || i >= cellReference.Length)
            return false;

        return int.TryParse(cellReference[i..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out rowNumber) && rowNumber is > 0 and <= 1048576 && columnNumber > 0;
    }

    /// <summary>
    /// Checks whether cell contains a formula or is a formula-computed string.
    /// </summary>
    /// <param name="cell">Cell to evaluate.</param>
    /// <returns>True when cell contains formula definition; otherwise false.</returns>
    public static bool HasFormula(Cell cell) => cell.CellFormula is not null;

    /// <summary>
    /// Resolves string value and rich text runs from cell.
    /// </summary>
    /// <param name="cell">Spreadsheet cell.</param>
    /// <param name="sst">Shared string table elements, or null.</param>
    /// <param name="runs">Extracted rich text runs if cell contains rich formatting.</param>
    /// <returns>Resolved string value, or null if cell is not a translatable string.</returns>
    public static string? ResolveCellString(Cell cell, IReadOnlyList<SharedStringItem>? sst, out IReadOnlyList<S.Run>? runs)
    {
        runs = null;
        if (HasFormula(cell))
            return null;

        if (cell.DataType?.Value == CellValues.SharedString)
        {
            if (int.TryParse(cell.CellValue?.Text, out var index) && sst is not null && index >= 0 && index < sst.Count)
            {
                var item = sst[index];
                var richRuns = item.Elements<S.Run>().ToList();
                if (richRuns.Count > 0)
                {
                    runs = richRuns;
                }
                return item.InnerText;
            }
            return null;
        }

        if (cell.DataType?.Value == CellValues.InlineString && cell.InlineString is not null)
        {
            var richRuns = cell.InlineString.Elements<S.Run>().ToList();
            if (richRuns.Count > 0)
            {
                runs = richRuns;
            }
            return cell.InlineString.InnerText;
        }

        // Untranslatable numeric, boolean, date, or error cell
        return null;
    }
}
