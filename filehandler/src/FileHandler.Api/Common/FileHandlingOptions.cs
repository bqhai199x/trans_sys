namespace FileHandler.Api.Common;

/// <summary>
/// Configuration options defining processing limits and thresholds for uploaded files.
/// </summary>
public sealed class FileHandlingOptions
{

    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "FileHandling";

    /// <summary>
    /// Maximum source file size in bytes.
    /// </summary>
    public long MaxFileBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// Maximum multipart request size in bytes.
    /// </summary>
    public long MaxMultipartBytes { get; set; } = 25 * 1024 * 1024;

    /// <summary>
    /// Maximum translation unit count.
    /// </summary>
    public int MaxUnits { get; set; } = 10_000;

    /// <summary>
    /// Maximum simultaneous import/export HTTP requests per process, clamped to 1–64.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 8;

    /// <summary>
    /// Maximum character count per translation.
    /// </summary>
    public int MaxTranslationChars { get; set; } = 100_000;

    /// <summary>
    /// Maximum exported file size in bytes.
    /// </summary>
    public long MaxOutputBytes { get; set; } = 20 * 1024 * 1024;
}
