using DocumentFormat.OpenXml;
using FileHandler.Api.Modules.Office;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace FileHandler.Api.Modules.Word;

/// <summary>
/// Reads and analyzes WordprocessingML table topology, row grids, and merged cells.
/// </summary>
public sealed class WordTableReader
{

    /// <summary>
    /// Analyzes Word table structure and records snapshot metrics.
    /// </summary>
    /// <param name="table">Word table element.</param>
    /// <param name="location">Location of table within document part.</param>
    /// <returns>Table structure snapshot.</returns>
    public WordTableSnapshot Read(W.Table table, OfficeLocation location)
    {
        var rowCount = 0;
        var cellCount = 0;
        var maxColumns = 0;

        foreach (var row in table.Elements<W.TableRow>())
        {
            rowCount++;
            var rowColumns = 0;
            foreach (var cell in row.Elements<W.TableCell>())
            {
                cellCount++;
                var span = cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1;
                rowColumns += span;
            }
            if (rowColumns > maxColumns)
                maxColumns = rowColumns;
        }

        return new WordTableSnapshot(location, rowCount, maxColumns, cellCount);
    }

    /// <summary>
    /// Determines whether a table cell is an empty continuation of a vertical merge.
    /// </summary>
    /// <param name="cell">Table cell to evaluate.</param>
    /// <returns>True when cell is a continuation of a vertical merge; otherwise false.</returns>
    public static bool IsVerticalMergeContinuation(W.TableCell cell)
    {
        var vMerge = cell.TableCellProperties?.VerticalMerge;
        if (vMerge is null)
            return false;

        // Restart means this is the master cell; lack of val or Val == Continue means continuation
        return vMerge.Val is null || vMerge.Val.Value == W.MergedCellValues.Continue;
    }

    /// <summary>
    /// Determines whether cell contains any translatable text runs.
    /// </summary>
    /// <param name="cell">Table cell to evaluate.</param>
    /// <returns>True when cell contains at least one non-empty text run; otherwise false.</returns>
    public static bool HasNonEmptyText(W.TableCell cell)
    {
        foreach (var text in cell.Descendants<W.Text>())
        {
            if (!string.IsNullOrEmpty(text.Text))
                return true;
        }
        return false;
    }
}
