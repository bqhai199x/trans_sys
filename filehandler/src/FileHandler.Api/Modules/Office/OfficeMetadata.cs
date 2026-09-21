using FileHandler.Api.Common;

namespace FileHandler.Api.Modules.Office;

/// <summary>
/// Projects extraction facts without exposing Office preservation bindings.
/// </summary>
internal static class OfficeMetadata
{

    /// <summary>
    /// Describes mapping size without allocating public units or location strings.
    /// </summary>
    /// <param name="format">Lowercase source format.</param>
    /// <param name="units">Extracted source units.</param>
    /// <returns>Metadata containing mapping size only.</returns>
    internal static FileMetadata Describe(string format, IReadOnlyList<OfficeTranslationUnit> units) => FileMetadata.Create(format) with
    {
        UnitCount = units.Count
    };

    /// <summary>
    /// Adds public unit mapping only after successful import explicitly requests diagnostics.
    /// </summary>
    /// <param name="metadata">Collected metadata and preserved skips.</param>
    /// <param name="units">Internal source mapping.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Metadata with ordered public units and readable locations.</returns>
    internal static FileMetadata WithUnits(FileMetadata metadata, IReadOnlyList<OfficeTranslationUnit> units, CancellationToken cancellationToken)
    {
        var mapping = new UnitMetadata[units.Count];
        for (var index = 0; index < units.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var unit = units[index];
            mapping[index] = new(unit.Index, unit.Location.CellReference is null ? unit.Kind : SkipScope.Cell, Location(unit.Location));
        }
        return metadata with { Units = mapping };
    }

    /// <summary>
    /// Converts source coordinates without serializing internal hashes.
    /// </summary>
    /// <param name="location">Internal source locator.</param>
    /// <returns>Public source location.</returns>
    internal static SourceLocation Location(OfficeLocation location) => new(
        PartUri: location.PartUri,
        Path: Path(location),
        SheetId: location.SheetId,
        SlideId: location.SlideId,
        CellReference: location.CellReference,
        ShapeId: location.ShapeId,
        RowIndex: location.RowIndex,
        ColumnIndex: location.ColumnIndex);

    /// <summary>
    /// Builds readable paths using canonical namespace prefixes and exact ordinals.
    /// </summary>
    /// <param name="location">Source path and actual root.</param>
    /// <returns>Root-inclusive path, or null when no element locator exists.</returns>
    private static string? Path(OfficeLocation location)
    {
        if (location.Root is null && location.ElementPath.Count == 0) return null;
        var builder = new System.Text.StringBuilder();
        if (location.Root is not null) AppendSegment(builder, location.Root);
        foreach (var segment in location.ElementPath) AppendSegment(builder, segment);
        return builder.ToString();
    }

    /// <summary>
    /// Appends one exact namespace-qualified path segment.
    /// </summary>
    /// <param name="builder">Path buffer.</param>
    /// <param name="segment">Source element address.</param>
    /// <returns>No return value.</returns>
    private static void AppendSegment(System.Text.StringBuilder builder, OfficeElementPathSegment segment)
    {
        var prefix = segment.NamespaceUri switch
        {
            "http://schemas.openxmlformats.org/wordprocessingml/2006/main" => "w:",
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main" => "x:",
            "http://schemas.openxmlformats.org/presentationml/2006/main" => "p:",
            "http://schemas.openxmlformats.org/drawingml/2006/main" => "a:",
            "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" => "xdr:",
            "http://schemas.openxmlformats.org/markup-compatibility/2006" => "mc:",
            _ => "Q{" + segment.NamespaceUri + "}"
        };
        builder.Append('/').Append(prefix).Append(segment.LocalName).Append('[').Append(segment.SiblingOrdinal).Append(']');
    }

    /// <summary>
    /// Attaches exact element and actual part root coordinates to semantic location.
    /// </summary>
    /// <param name="location">Semantic source coordinates.</param>
    /// <param name="element">Actual source element.</param>
    /// <returns>Location with root-inclusive path facts.</returns>
    internal static OfficeLocation At(OfficeLocation location, DocumentFormat.OpenXml.OpenXmlElement element)
    {
        var root = element;
        while (root.Parent is not null) root = root.Parent;
        return location with { ElementPath = OfficeTextBindings.Path(element), Root = new(root.NamespaceUri, root.LocalName, 1) };
    }
}
