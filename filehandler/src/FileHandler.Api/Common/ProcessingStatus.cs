namespace FileHandler.Api.Common;

/// <summary>
/// Task outcome values serialized in metadata.
/// </summary>
internal static class ProcessingStatus
{

    /// <summary>
    /// success value in public metadata.
    /// </summary>
    internal const string Success = "success";

    /// <summary>
    /// partial value in public metadata.
    /// </summary>
    internal const string Partial = "partial";

    /// <summary>
    /// failed value in public metadata.
    /// </summary>
    internal const string Failed = "failed";

    /// <summary>
    /// Derives task outcome without treating informational skips as partial failure.
    /// </summary>
    /// <param name="skipped">Collected source preservation facts.</param>
    /// <param name="failed">Whether a fatal error prevents completion.</param>
    /// <returns>Failed for fatal errors, partial for warnings, otherwise success.</returns>
    internal static string Resolve(IReadOnlyList<SkipMetadata> skipped, bool failed = false) =>
        failed ? Failed : skipped.Any(skip => skip.Severity == SkipSeverity.Warning) ? Partial : Success;
}
