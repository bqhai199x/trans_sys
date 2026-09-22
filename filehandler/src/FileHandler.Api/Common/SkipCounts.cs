namespace FileHandler.Api.Common;

/// <summary>
/// Preserved object totals collected before debug visibility filtering.
/// </summary>
/// <param name="Warning">Sum of warning skip counts, not translation unit count.</param>
/// <param name="Info">Sum of informational skip counts, including hidden response entries.</param>
public sealed record SkipCounts(long Warning, long Info)
{

    /// <summary>
    /// Counts preserved objects by severity in one traversal.
    /// </summary>
    /// <param name="skipped">Collected preservation entries.</param>
    /// <returns>Warning and informational totals from known source facts.</returns>
    public static SkipCounts From(IReadOnlyList<SkipMetadata> skipped)
    {
        if (skipped is ICountedSkips counted) return counted.Counts;
        long warning = 0;
        long info = 0;
        foreach (var skip in skipped)
        {
            if (skip.Severity == SkipSeverity.Warning) warning += skip.Count;
            else if (skip.Severity == SkipSeverity.Info) info += skip.Count;
        }
        return new(warning, info);
    }
}

/// <summary>
/// Skip collections retaining totals without materializing every informational entry.
/// </summary>
internal interface ICountedSkips
{

    /// <summary>
    /// Complete warning and informational object counts.
    /// </summary>
    SkipCounts Counts { get; }
}
