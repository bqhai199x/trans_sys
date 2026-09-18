namespace FileHandler.Api.Diagnostics;

/// <summary>
/// Configuration options for diagnostic request tracing and limits.
/// </summary>
public sealed class DebugTraceOptions
{

    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "DebugTrace";

    /// <summary>
    /// Whether request tracing is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Trace directory, relative to content root or absolute.
    /// </summary>
    public string Directory { get; set; } = "logs/debug";

    /// <summary>
    /// Whether trace values include captured content.
    /// </summary>
    public bool CaptureContent { get; set; } = true;

    /// <summary>
    /// Maximum captured string or value length in characters.
    /// </summary>
    public int MaxValueLength { get; set; } = 1000;

    /// <summary>
    /// Maximum number of trace events per request.
    /// </summary>
    public int MaxEvents { get; set; } = 10000;

    /// <summary>
    /// Maximum serialized trace bytes; overflow produces a summary-only trace.
    /// </summary>
    public long MaxTraceBytes { get; set; } = 4194304;

    /// <summary>
    /// Explicit request paths eligible for tracing.
    /// </summary>
    public string[]? TraceablePaths { get; set; }
}
