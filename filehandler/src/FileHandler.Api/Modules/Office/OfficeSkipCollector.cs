using System.Collections;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Validation;
using FileHandler.Api.Common;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace FileHandler.Api.Modules.Office;

/// <summary>
/// Aggregates hidden information while retaining exact schema-preservation coordinates.
/// </summary>
internal sealed class OfficeSkipCollector : IReadOnlyList<SkipMetadata>, ICountedSkips
{

    /// <summary>
    /// Whether caller requests informational diagnostic entries.
    /// </summary>
    private readonly bool _includeInfo;

    /// <summary>
    /// Informational object count omitted from materialized entries.
    /// </summary>
    private long _omittedInfo;

    /// <summary>
    /// Preserved cell coordinates indexed by owning part.
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> _cells = new(StringComparer.Ordinal);

    /// <summary>
    /// Preserved XML paths without public path strings or retained DOM nodes.
    /// </summary>
    private readonly Dictionary<string, List<OfficePreservedRegion>> _regions = new(StringComparer.Ordinal);

    /// <summary>
    /// Materialized warnings and explicitly requested informational entries.
    /// </summary>
    internal List<SkipMetadata> Entries { get; } = [];

    /// <summary>
    /// Number of materialized entries.
    /// </summary>
    public int Count => Entries.Count;

    /// <summary>
    /// Complete totals including informational entries never allocated.
    /// </summary>
    public SkipCounts Counts
    {
        get
        {
            var visible = SkipCounts.From(Entries);
            return visible with { Info = visible.Info + _omittedInfo };
        }
    }

    /// <summary>
    /// Materialized diagnostic at zero-based index.
    /// </summary>
    /// <param name="index">Materialized entry index.</param>
    public SkipMetadata this[int index] => Entries[index];

    /// <summary>
    /// Creates extraction collector attached to one request-local source.
    /// </summary>
    /// <param name="source">Source carrying diagnostic policy and validation context.</param>
    internal OfficeSkipCollector(OfficeSource source)
    {
        _includeInfo = source.IncludeInformationalSkips;
        source.ExtractionSkips = this;
    }

    /// <summary>
    /// Appends an already materialized warning or diagnostic.
    /// </summary>
    /// <param name="entry">Complete diagnostic to preserve.</param>
    /// <returns>No return value.</returns>
    internal void Add(SkipMetadata entry) => Entries.Add(entry);

    /// <summary>
    /// Counts a policy exclusion and materializes its location only for diagnostics.
    /// </summary>
    /// <param name="code">Stable exclusion code.</param>
    /// <param name="stage">Selection or extraction stage.</param>
    /// <param name="scope">Preserved object category.</param>
    /// <param name="message">Public exclusion description.</param>
    /// <param name="partUri">Owning part URI.</param>
    /// <param name="sheetId">Optional native worksheet ID.</param>
    /// <param name="slideId">Optional native slide ID.</param>
    /// <param name="cellReference">Optional cell coordinate or row location.</param>
    /// <returns>No return value.</returns>
    internal void Info(string code, string stage, string scope, string message, string partUri,
        string? sheetId = null, string? slideId = null, string? cellReference = null)
    {
        if (_includeInfo)
        {
            Entries.Add(new(code, SkipSeverity.Info, stage, scope, 1, message,
                new(PartUri: partUri, SheetId: sheetId, SlideId: slideId, CellReference: cellReference)));
            return;
        }
        _omittedInfo++;
        if (stage != SkipStage.Extraction || scope != SkipScope.Cell || cellReference is null) return;
        if (!_cells.TryGetValue(partUri, out var cells)) _cells.Add(partUri, cells = new(StringComparer.Ordinal));
        cells.Add(cellReference);
    }

    /// <summary>
    /// Records an exact protected subtree without formatting hidden diagnostic paths.
    /// </summary>
    /// <param name="code">Stable exclusion code.</param>
    /// <param name="severity">Warning or informational severity.</param>
    /// <param name="scope">Preserved object category.</param>
    /// <param name="message">Public exclusion description.</param>
    /// <param name="partUri">Owning part URI.</param>
    /// <param name="element">Protected subtree root.</param>
    /// <returns>No return value.</returns>
    internal void Region(string code, string severity, string scope, string message, string partUri, OpenXmlElement element)
    {
        var root = element;
        while (root.Parent is not null) root = root.Parent;
        var path = OfficeTextBindings.Path(element);
        if (_includeInfo || severity != SkipSeverity.Info)
        {
            var location = new OfficeLocation(partUri, path) { Root = new(root.NamespaceUri, root.LocalName, 1) };
            Entries.Add(new(code, severity, SkipStage.Extraction, scope, 1, message, OfficeMetadata.Location(location)));
            return;
        }
        _omittedInfo++;
        if (!_regions.TryGetValue(partUri, out var regions)) _regions.Add(partUri, regions = []);
        regions.Add(new(root.NamespaceUri, root.LocalName, path));
    }

    /// <summary>
    /// Matches schema errors against compact facts for hidden informational regions.
    /// </summary>
    /// <param name="error">Source or output schema finding.</param>
    /// <returns>True only inside an explicitly preserved cell or subtree.</returns>
    internal bool IsPreserved(ValidationErrorInfo error)
    {
        if (error.Part is null || error.Node is not { } node) return false;
        var partUri = error.Part.Uri.ToString();
        var cell = node as S.Cell ?? node.Ancestors<S.Cell>().FirstOrDefault();
        if (cell?.CellReference?.Value is { } address && _cells.TryGetValue(partUri, out var cells) && cells.Contains(address)) return true;
        if (!_regions.TryGetValue(partUri, out var regions)) return false;
        var root = node;
        while (root.Parent is not null) root = root.Parent;
        var path = OfficeTextBindings.Path(node);
        return regions.Any(region => region.RootNamespace == root.NamespaceUri && region.RootName == root.LocalName &&
            region.Path.Count <= path.Count && region.Path.SequenceEqual(path.Take(region.Path.Count)));
    }

    /// <summary>
    /// Enumerates only materialized public diagnostics.
    /// </summary>
    /// <returns>Ordered diagnostic enumerator.</returns>
    public IEnumerator<SkipMetadata> GetEnumerator() => Entries.GetEnumerator();

    /// <summary>
    /// Enumerates materialized diagnostics through nongeneric interface.
    /// </summary>
    /// <returns>Ordered diagnostic enumerator.</returns>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Minimal source subtree coordinates independent of package lifetime.
/// </summary>
/// <param name="RootNamespace">Part root namespace.</param>
/// <param name="RootName">Part root local name.</param>
/// <param name="Path">Root-relative element address.</param>
internal sealed record OfficePreservedRegion(string RootNamespace, string RootName, IReadOnlyList<OfficeElementPathSegment> Path);
