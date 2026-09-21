using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;

namespace FileHandler.Api.Modules.PowerPoint;

/// <summary>
/// Snapshot of presentation slide metadata.
/// </summary>
/// <param name="SlideId">Unique presentation slide identifier.</param>
/// <param name="PartUri">Canonical part URI of slide.</param>
/// <param name="Show">True when slide is visible during presentation.</param>
/// <param name="ShapeCount">Number of shapes in slide tree.</param>
public sealed record PowerPointSlideSnapshot(string SlideId, string PartUri, bool Show, int ShapeCount);

/// <summary>
/// Extraction plan for PowerPoint presentation.
/// </summary>
/// <param name="SourceHash">Hexadecimal SHA-256 digest of source file.</param>
/// <param name="Units">Ordered extraction units.</param>
/// <param name="Slides">Discovered slide snapshots.</param>
public sealed record PowerPointPlan(
    string SourceHash,
    IReadOnlyList<OfficeTranslationUnit> Units,
    IReadOnlyList<PowerPointSlideSnapshot> Slides)
{

    /// <summary>
    /// Source inventory, selected ranges and extraction exclusions.
    /// </summary>
    public FileMetadata Metadata { get; init; } = FileMetadata.Create("powerpoint");
}

/// <summary>
/// Prepared patch for PowerPoint presentation.
/// </summary>
/// <param name="Plan">Original extraction plan.</param>
/// <param name="DecodedUnits">Validated and decoded translation units.</param>
/// <param name="EditMasks">Modification masks per touched part.</param>
public sealed record PowerPointPreparedPatch(
    PowerPointPlan Plan,
    IReadOnlyList<OfficeDecodedUnit> DecodedUnits,
    IReadOnlyDictionary<string, OfficeEditMask> EditMasks);
