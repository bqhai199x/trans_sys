using FileHandler.Api.Common;
namespace FileHandler.Api.Modules.Excel;

/// <summary>
/// Indexes protected table header and totals intervals by worksheet row.
/// </summary>
internal sealed class ExcelProtectedCellIndex
{

    /// <summary>
    /// Sorted disjoint column intervals per protected row.
    /// </summary>
    private readonly Dictionary<int, List<(int Left, int Right)>> _rows = [];

    /// <summary>
    /// Parses table ranges once and combines overlapping protected intervals.
    /// </summary>
    /// <param name="tables">Worksheet table definitions.</param>
    internal ExcelProtectedCellIndex(IReadOnlyList<ExcelTableSnapshot> tables)
    {
        foreach (var table in tables)
        {
            var split = table.Reference.IndexOf(':');
            if (split < 0 || !ExcelCellResolver.TryParseCoordinates(table.Reference[..split], out var left, out var top) ||
                !ExcelCellResolver.TryParseCoordinates(table.Reference[(split + 1)..], out var right, out var bottom) ||
                left > right || top > bottom || table.HeaderRowCount is < 0 or > 1 || table.TotalsRowCount is < 0 or > 1)
                throw new InvalidDataException("Unsupported table range.");
            if (table.HeaderRowCount > 0) Add(top, left, right);
            if (table.TotalsRowCount > 0) Add(bottom, left, right);
        }
        foreach (var intervals in _rows.Values)
        {
            intervals.Sort((a, b) => a.Left.CompareTo(b.Left));
            var count = 0;
            for (var i = 0; i < intervals.Count; i++)
            {
                var interval = intervals[i];
                if (count > 0 && interval.Left <= intervals[count - 1].Right + 1)
                    intervals[count - 1] = (intervals[count - 1].Left, Math.Max(intervals[count - 1].Right, interval.Right));
                else intervals[count++] = interval;
            }
            intervals.RemoveRange(count, intervals.Count - count);
        }
    }

    /// <summary>
    /// Adds one header or totals interval.
    /// </summary>
    /// <param name="row">One-based row.</param>
    /// <param name="left">First column.</param>
    /// <param name="right">Last column.</param>
    /// <returns>No return value.</returns>
    private void Add(int row, int left, int right)
    {
        if (!_rows.TryGetValue(row, out var intervals)) _rows[row] = intervals = [];
        intervals.Add((left, right));
    }

    /// <summary>
    /// Checks protected ownership without reparsing table coordinates.
    /// </summary>
    /// <param name="column">One-based column.</param>
    /// <param name="row">One-based row.</param>
    /// <returns>True inside header or totals cells.</returns>
    internal bool Contains(int column, int row)
    {
        if (!_rows.TryGetValue(row, out var intervals)) return false;
        var low = 0;
        var high = intervals.Count - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var interval = intervals[middle];
            if (column < interval.Left) high = middle - 1;
            else if (column > interval.Right) low = middle + 1;
            else return true;
        }
        return false;
    }
}
