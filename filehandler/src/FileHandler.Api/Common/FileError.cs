using System.Text.Json.Serialization;

namespace FileHandler.Api.Common;

/// <summary>
/// File handling or validation error.
/// </summary>
/// <param name="Code">Machine-readable error code.</param>
/// <param name="Message">Error description.</param>
/// <param name="Index">Optional zero-based translation index.</param>
/// <param name="Line">Optional source line range.</param>
/// <param name="Marker">Optional marker associated with error.</param>
public sealed record FileError(
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Index = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SourceLineRange? Line = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Marker = null);

/// <summary>
/// Inclusive, one-based source line range.
/// </summary>
/// <param name="Start">First line number.</param>
/// <param name="End">Last line number.</param>
public sealed record SourceLineRange(int Start, int End);
