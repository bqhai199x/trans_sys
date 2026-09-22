using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;

namespace FileHandler.Api.Modules.Word;

/// <summary>
/// Snapshot of Word story participating in extraction plan.
/// </summary>
/// <param name="StoryKind">Kind of story such as Body, Header, Footer.</param>
/// <param name="PartUri">Canonical part URI of story.</param>
/// <param name="ReferenceCount">Number of sections or occurrences referencing story.</param>
public sealed record WordStorySnapshot(string StoryKind, string PartUri, int ReferenceCount);

/// <summary>
/// Snapshot of Word table structure and dimensions.
/// </summary>
/// <param name="Location">Table location.</param>
/// <param name="RowCount">Total row count.</param>
/// <param name="ColumnCount">Logical column count.</param>
/// <param name="CellCount">Total cell count.</param>
public sealed record WordTableSnapshot(OfficeLocation Location, int RowCount, int ColumnCount, int CellCount);

/// <summary>
/// Extraction plan for Word document.
/// </summary>
/// <param name="SourceHash">Hexadecimal SHA-256 digest of source file.</param>
/// <param name="Units">Ordered extraction units.</param>
/// <param name="Stories">Discovered story snapshots.</param>
/// <param name="Tables">Discovered table snapshots.</param>
public sealed record WordPlan(
    string SourceHash,
    IReadOnlyList<OfficeTranslationUnit> Units,
    IReadOnlyList<WordStorySnapshot> Stories,
    IReadOnlyList<WordTableSnapshot> Tables)
{

    /// <summary>
    /// Public source mapping and preserved region metadata.
    /// </summary>
    public FileMetadata Metadata { get; init; } = FileMetadata.Create("word");
}

/// <summary>
/// Prepared patch for Word document ready for application.
/// </summary>
/// <param name="Plan">Original extraction plan.</param>
/// <param name="DecodedUnits">Validated and decoded translation units.</param>
/// <param name="EditMasks">Modification masks per touched part.</param>
public sealed record WordPreparedPatch(
    WordPlan Plan,
    IReadOnlyList<OfficeDecodedUnit> DecodedUnits,
    IReadOnlyDictionary<string, OfficeEditMask> EditMasks);
