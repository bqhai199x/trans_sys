using System.Text.Json.Serialization;

namespace FileHandler.Api.Common;

public sealed record FileError(
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Index = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SourceLineRange? Line = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Marker = null);

public sealed record SourceLineRange(int Start, int End);
