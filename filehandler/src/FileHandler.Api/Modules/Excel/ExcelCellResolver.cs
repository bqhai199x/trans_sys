using FileHandler.Api.Common;
using DocumentFormat.OpenXml.Spreadsheet;

namespace FileHandler.Api.Modules.Excel;

/// <summary>
/// Parses cell coordinates and detects formula cells.
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

        return int.TryParse(cellReference.AsSpan(i), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out rowNumber) && rowNumber is > 0 and <= 1048576 && columnNumber > 0;
    }

    /// <summary>
    /// Checks whether cell contains a formula or is a formula-computed string.
    /// </summary>
    /// <param name="cell">Cell to evaluate.</param>
    /// <returns>True when cell contains formula definition; otherwise false.</returns>
    public static bool HasFormula(Cell cell) => cell.CellFormula is not null;
}
