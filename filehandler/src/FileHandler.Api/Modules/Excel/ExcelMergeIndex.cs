using FileHandler.Api.Common;
using DocumentFormat.OpenXml.Spreadsheet;

namespace FileHandler.Api.Modules.Excel;

/// <summary>
/// Resolves merged-cell ownership during ascending worksheet row traversal.
/// </summary>
internal sealed class ExcelMergeIndex
{

    /// <summary>
    /// Merge rectangles ordered by first row.
    /// </summary>
    private readonly (int Left, int Top, int Right, int Bottom)[] _ranges;

    /// <summary>
    /// Active rectangles ordered by first column and range index.
    /// </summary>
    private readonly SortedSet<(int Column, int Index)> _active = [];

    /// <summary>
    /// Active range expiry queue ordered by last row.
    /// </summary>
    private readonly PriorityQueue<int, int> _expiry = new();

    /// <summary>
    /// Next range awaiting activation.
    /// </summary>
    private int _next;

    /// <summary>
    /// Creates a sweep index without expanding merged rectangles into cells.
    /// </summary>
    /// <param name="merges">Worksheet merge definitions.</param>
    internal ExcelMergeIndex(IEnumerable<MergeCell> merges)
    {
        _ranges = merges.Select(Parse).OrderBy(r => r.Top).ToArray();
    }

    /// <summary>
    /// Parses and validates one rectangle.
    /// </summary>
    /// <param name="merge">Merge definition.</param>
    /// <returns>Inclusive rectangle coordinates.</returns>
    private static (int Left, int Top, int Right, int Bottom) Parse(MergeCell merge)
    {
        var bounds = merge.Reference?.Value?.Split(':');
        if (bounds is not { Length: 2 } || !ExcelCellResolver.TryParseCoordinates(bounds[0], out var left, out var top) ||
            !ExcelCellResolver.TryParseCoordinates(bounds[1], out var right, out var bottom) || left > right || top > bottom)
            throw new InvalidDataException("Invalid merged range.");
        return (left, top, right, bottom);
    }

    /// <summary>
    /// Checks whether a selected cell is a follower instead of merge owner.
    /// </summary>
    /// <param name="column">One-based column.</param>
    /// <param name="row">One-based row; calls must use ascending rows.</param>
    /// <returns>True for a merged follower cell.</returns>
    internal bool IsFollower(int column, int row)
    {
        while (_expiry.TryPeek(out var expired, out var end) && end < row)
        {
            _expiry.Dequeue();
            _active.Remove((_ranges[expired].Left, expired));
        }
        while (_next < _ranges.Length && _ranges[_next].Top <= row)
        {
            var index = _next++;
            var range = _ranges[index];
            if (range.Bottom < row) continue;
            var previous = _active.GetViewBetween((0, 0), (range.Left, int.MaxValue)).Max;
            var following = _active.GetViewBetween((range.Left, 0), (int.MaxValue, int.MaxValue)).Min;
            if (previous.Column > 0 && _ranges[previous.Index].Right >= range.Left || following.Column > 0 && following.Column <= range.Right)
                throw new InvalidDataException("Overlapping merged ranges.");
            _active.Add((range.Left, index));
            _expiry.Enqueue(index, range.Bottom);
        }
        var candidate = _active.GetViewBetween((0, 0), (column, int.MaxValue)).Max;
        if (candidate.Column == 0) return false;
        var owner = _ranges[candidate.Index];
        return column <= owner.Right && (column != owner.Left || row != owner.Top);
    }
}
