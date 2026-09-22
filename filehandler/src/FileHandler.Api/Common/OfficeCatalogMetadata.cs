using System.Text.Json.Serialization;

namespace FileHandler.Api.Common;

/// <summary>
/// Native Excel sheet inventory and optional import mapping range.
/// </summary>
/// <param name="SheetId">Native sheet identifier.</param>
/// <param name="Index">One-based source order.</param>
/// <param name="Name">Original sheet name.</param>
/// <param name="State">Visible, hidden or veryHidden.</param>
/// <param name="Kind">Worksheet, chartsheet or SDK part kind.</param>
/// <param name="CanImport">Whether sheet kind supports extraction.</param>
public sealed record SheetMetadata(string SheetId, int Index, string Name, string State, string Kind, bool CanImport)
{

    /// <summary>
    /// Selected state, omitted by discovery.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Selected { get; init; }

    /// <summary>
    /// Zero-based first unit index, import only.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? UnitStartIndex { get; init; }

    /// <summary>
    /// Exclusive unit range end, import only.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? UnitEndIndex { get; init; }

    /// <summary>
    /// Internal part address reused during extraction.
    /// </summary>
    [JsonIgnore]
    public string PartUri { get; init; } = "";
}

/// <summary>
/// Native slide inventory and optional import mapping range.
/// </summary>
/// <param name="SlideId">Native slide identifier.</param>
/// <param name="Index">One-based source order.</param>
/// <param name="Title">Title placeholder text, or null when absent.</param>
/// <param name="Hidden">Whether slide is hidden in presentation.</param>
public sealed record SlideMetadata(string SlideId, int Index, string? Title, bool Hidden)
{

    /// <summary>
    /// Selected state, omitted by discovery.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Selected { get; init; }

    /// <summary>
    /// Zero-based first unit index, import only.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? UnitStartIndex { get; init; }

    /// <summary>
    /// Exclusive unit range end, import only.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? UnitEndIndex { get; init; }

    /// <summary>
    /// Internal part address reused during extraction.
    /// </summary>
    [JsonIgnore]
    public string PartUri { get; init; } = "";
}

/// <summary>
/// Inventory operation facts without translation mapping.
/// </summary>
/// <param name="Format">Source format identifier.</param>
/// <param name="Status">Success, partial or failed.</param>
/// <param name="Skipped">Inventory exclusions.</param>
public sealed record DiscoveryMetadata(string Format, string Status, IReadOnlyList<SkipMetadata> Skipped)
{

    /// <summary>
    /// Collected warning and informational object totals, zero before inventory exclusions.
    /// </summary>
    public SkipCounts SkipCount => SkipCounts.From(Skipped);
}

/// <summary>
/// Sheet inventory response.
/// </summary>
/// <param name="Sheets">Sheets in source order.</param>
/// <param name="Metadata">Discovery facts.</param>
/// <param name="Errors">Fatal inventory errors.</param>
public sealed record SheetsResponse(IReadOnlyList<SheetMetadata> Sheets, DiscoveryMetadata Metadata, IReadOnlyList<FileError> Errors);

/// <summary>
/// Slide inventory response.
/// </summary>
/// <param name="Slides">Slides in source order.</param>
/// <param name="Metadata">Discovery facts.</param>
/// <param name="Errors">Fatal inventory errors.</param>
public sealed record SlidesResponse(IReadOnlyList<SlideMetadata> Slides, DiscoveryMetadata Metadata, IReadOnlyList<FileError> Errors);

/// <summary>
/// Discovery failure without translation mapping or file content.
/// </summary>
/// <param name="Metadata">Inventory facts collected before failure.</param>
/// <param name="Errors">Fatal discovery errors.</param>
public sealed record DiscoveryFailureResponse(DiscoveryMetadata Metadata, IReadOnlyList<FileError> Errors);
