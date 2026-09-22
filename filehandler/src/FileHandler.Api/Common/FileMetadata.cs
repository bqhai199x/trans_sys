using System.Text.Json.Serialization;

namespace FileHandler.Api.Common;

/// <summary>
/// Public processing facts without internal bindings or source fingerprints.
/// </summary>
public sealed record FileMetadata
{

    /// <summary>
    /// Totals captured before informational response entries are removed.
    /// </summary>
    private SkipCounts? _skipCount;

    /// <summary>
    /// Lowercase file format identifier.
    /// </summary>
    public required string Format { get; init; }

    /// <summary>
    /// Operation outcome: success, partial or failed.
    /// </summary>
    public string Status { get; init; } = ProcessingStatus.Success;

    /// <summary>
    /// Mapping size, or null before extraction completes.
    /// </summary>
    public int? UnitCount { get; init; }

    /// <summary>
    /// Source mapping detached into requested units.json attachment by HTTP import.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<UnitMetadata>? Units { get; init; }

    /// <summary>
    /// Preserved regions in deterministic processing order.
    /// </summary>
    public IReadOnlyList<SkipMetadata> Skipped { get; init; } = [];

    /// <summary>
    /// Warning and informational object totals before debug filtering.
    /// </summary>
    public SkipCounts SkipCount
    {
        get => _skipCount ?? SkipCounts.From(Skipped);
        init => _skipCount = value;
    }

    /// <summary>
    /// Excel inventory with selection and optional import ranges.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SheetMetadata>? Sheets { get; init; }

    /// <summary>
    /// PowerPoint inventory with selection and optional import ranges.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SlideMetadata>? Slides { get; init; }

    /// <summary>
    /// Requested and effective sheet renames, Excel export only.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SheetNameChange>? SheetNameChanges { get; init; }

    /// <summary>
    /// Markdown line break preservation policy.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NewlinePolicy { get; init; }

    /// <summary>
    /// Plain text source encoding.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Encoding { get; init; }

    /// <summary>
    /// Creates metadata before source mapping is available.
    /// </summary>
    /// <param name="format">Lowercase format identifier.</param>
    /// <returns>Metadata with format-specific source policies.</returns>
    public static FileMetadata Create(string format) => new()
    {
        Format = format,
        Encoding = format == "plaintext" ? "utf-8" : null,
        NewlinePolicy = format == "markdown" ? "preserve" : null
    };

    /// <summary>
    /// Filters informational skips at public boundary without changing internal preservation facts.
    /// </summary>
    /// <param name="debug">Whether informational skips may be returned.</param>
    /// <returns>Metadata retaining warnings and status; debug also retains informational skips.</returns>
    public FileMetadata ForResponse(bool debug)
    {
        var counts = SkipCount;
        return this with
        {
            SkipCount = counts,
            Skipped = debug || counts.Info == 0 ? Skipped : Skipped.Where(skip => skip.Severity != SkipSeverity.Info).ToArray()
        };
    }

    /// <summary>
    /// Appends diagnostics while retaining totals for unmaterialized source skips.
    /// </summary>
    /// <param name="additional">New translation or rename diagnostics.</param>
    /// <returns>Metadata containing combined entries and complete object totals.</returns>
    internal FileMetadata AppendSkipped(IReadOnlyList<SkipMetadata> additional)
    {
        if (additional.Count == 0) return this;
        var current = SkipCount;
        var added = SkipCounts.From(additional);
        return this with
        {
            Skipped = Skipped.Concat(additional).ToArray(),
            SkipCount = new(current.Warning + added.Warning, current.Info + added.Info)
        };
    }

    /// <summary>
    /// Removes import mapping and derives outcome from retained warnings.
    /// </summary>
    /// <param name="failed">Whether operation failed.</param>
    /// <returns>Export-safe metadata retaining collected facts.</returns>
    public FileMetadata ForExport(bool failed = false) => this with
    {
        Units = null,
        Sheets = Sheets?.Select(s => s with { UnitStartIndex = null, UnitEndIndex = null }).ToArray(),
        Slides = Slides?.Select(s => s with { UnitStartIndex = null, UnitEndIndex = null }).ToArray(),
        Status = ProcessingStatus.Resolve(Skipped, failed)
    };
}

/// <summary>
/// Public translation mapping entry.
/// </summary>
/// <param name="Index">Zero-based translation index.</param>
/// <param name="Kind">Semantic unit category.</param>
/// <param name="Location">Location in original source.</param>
public sealed record UnitMetadata(int Index, string Kind, SourceLocation Location);

/// <summary>
/// Source coordinates independent of internal XML bindings.
/// </summary>
/// <param name="Line">Inclusive one-based source lines.</param>
/// <param name="PartUri">Actual package part URI.</param>
/// <param name="Path">Root-inclusive XML element path with one-based ordinals.</param>
/// <param name="SheetId">Native workbook sheet identifier.</param>
/// <param name="SlideId">Native presentation slide identifier.</param>
/// <param name="CellReference">A1 cell address.</param>
/// <param name="ShapeId">Drawing shape identifier.</param>
/// <param name="RowIndex">One-based table row.</param>
/// <param name="ColumnIndex">One-based table column.</param>
public sealed record SourceLocation(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SourceLineRange? Line = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PartUri = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Path = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SheetId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SlideId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CellReference = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ShapeId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? RowIndex = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ColumnIndex = null);

/// <summary>
/// Recoverable exclusion preserving original source content.
/// </summary>
/// <param name="Code">Stable diagnostic identifier.</param>
/// <param name="Severity">Info for policy exclusions, warning for unsupported or invalid content.</param>
/// <param name="Stage">Selection, extraction, translation or rename.</param>
/// <param name="Scope">Semantic category counted by this entry.</param>
/// <param name="Count">Number of preserved objects within scope.</param>
/// <param name="Message">Human-readable explanation.</param>
/// <param name="Location">Original source coordinates.</param>
/// <param name="UnitIndex">Zero-based index, absent when object never became a unit.</param>
public sealed record SkipMetadata(string Code, string Severity, string Stage, string Scope, int Count,
    string Message, SourceLocation Location,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? UnitIndex = null);

/// <summary>
/// Metadata and fatal errors returned without a file on failure.
/// </summary>
/// <param name="Metadata">Facts collected before completion or failure.</param>
/// <param name="Errors">Fatal errors only.</param>
public sealed record FileResponse(FileMetadata Metadata, IReadOnlyList<FileError> Errors);

/// <summary>
/// Requested worksheet name and final normalized or retained name.
/// </summary>
/// <param name="SheetId">Native sheet identifier.</param>
/// <param name="OriginalName">Original source name.</param>
/// <param name="RequestedName">Caller-supplied name.</param>
/// <param name="FinalName">Actual output name.</param>
public sealed record SheetNameChange(string SheetId, string OriginalName, string RequestedName, string FinalName);
