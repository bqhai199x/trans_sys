using System.Text.Json.Serialization;

namespace FileHandler.Api.Diagnostics;

/// <summary>
/// Root document containing metadata and call tree for a traced request.
/// </summary>
public sealed class TraceDocument
{

    /// <summary>
    /// Metadata layout version enabling streaming summary reads.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Unique identifier for this trace session.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when request trace was initiated.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// HTTP request method.
    /// </summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// HTTP request path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Original client file name uploaded in request, if any.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileName { get; set; }

    /// <summary>
    /// HTTP response status code.
    /// </summary>
    public int Status { get; set; }

    /// <summary>
    /// Total request execution duration in milliseconds.
    /// </summary>
    public double DurationMs { get; set; }

    /// <summary>
    /// Unhandled error details, if any.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Error { get; set; }

    /// <summary>
    /// Whether request was cancelled.
    /// </summary>
    public bool Cancelled { get; set; }

    /// <summary>
    /// Root calls executed within this trace session.
    /// </summary>
    public List<TraceNode> Calls { get; set; } = [];
}

/// <summary>
/// Node in execution call tree representing a function call or iteration item.
/// </summary>
public sealed class TraceNode
{

    /// <summary>
    /// Node type indicator: "call" or "item".
    /// </summary>
    public string Type { get; set; } = "call";

    /// <summary>
    /// Unique call identifier within session, or null for item scopes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Id { get; set; }

    /// <summary>
    /// Iteration index for item scopes, or null for call scopes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Index { get; set; }

    /// <summary>
    /// Component and method target name, or null for item scopes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Target { get; set; }

    /// <summary>
    /// Captured input parameter values.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? In { get; set; }

    /// <summary>
    /// Captured return value.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Out { get; set; }

    /// <summary>
    /// Execution duration in milliseconds.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DurationMs { get; set; }

    /// <summary>
    /// Captured error details if call failed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Error { get; set; }

    /// <summary>
    /// Intermediate state snapshots recorded during this scope.
    /// </summary>
    public List<TraceStateEntry> States { get; set; } = [];

    /// <summary>
    /// Child calls and iteration items executed inside this scope.
    /// </summary>
    public List<TraceNode> Children { get; set; } = [];
}

/// <summary>
/// Named state snapshot recorded within a trace scope.
/// </summary>
public sealed class TraceStateEntry
{

    /// <summary>
    /// Optional name identifying state value.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Captured state value.
    /// </summary>
    public object? Value { get; set; }
}

/// <summary>
/// Summary metadata for a trace log file used in list views.
/// </summary>
public sealed class DebugLogSummary
{

    /// <summary>
    /// Unique identifier for trace file.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Name of trace file on disk.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Original client file name uploaded in request, if any.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestFileName { get; set; }

    /// <summary>
    /// UTC timestamp when request was initiated.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// HTTP request method.
    /// </summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// HTTP request path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// HTTP response status code.
    /// </summary>
    public int Status { get; set; }

    /// <summary>
    /// Total request duration in milliseconds.
    /// </summary>
    public double DurationMs { get; set; }

    /// <summary>
    /// Size of trace file in bytes.
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// Error summary text if request failed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
}

/// <summary>
/// Request payload to toggle debug trace capture mode.
/// </summary>
public sealed class ToggleDebugRequest
{

    /// <summary>
    /// Whether debug tracing should be enabled.
    /// </summary>
    public bool Enabled { get; set; }
}

/// <summary>
/// Status response detailing current debug trace settings and counts.
/// </summary>
public sealed class DebugStatusResponse
{

    /// <summary>
    /// Whether debug tracing is currently active.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Target directory where trace files are stored.
    /// </summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>
    /// Whether request contents are captured in traces.
    /// </summary>
    public bool CaptureContent { get; set; }

    /// <summary>
    /// Total number of stored trace log files.
    /// </summary>
    public int TotalLogs { get; set; }
}
