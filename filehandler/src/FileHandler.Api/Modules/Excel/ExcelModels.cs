using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;

namespace FileHandler.Api.Modules.Excel;

/// <summary>
/// Snapshot of worksheet metadata and cell count.
/// </summary>
/// <param name="SheetName">Name of sheet.</param>
/// <param name="PartUri">Canonical part URI.</param>
/// <param name="State">Sheet visibility state.</param>
/// <param name="CellCount">Number of translatable cells discovered.</param>
public sealed record ExcelSheetSnapshot(string SheetName, string PartUri, string State, int CellCount);

/// <summary>
/// Snapshot of Excel table metadata and protected range.
/// </summary>
/// <param name="TableName">Display name of table.</param>
/// <param name="PartUri">Canonical part URI of worksheet.</param>
/// <param name="Reference">A1 cell range reference of table.</param>
/// <param name="HeaderRowCount">Number of header rows.</param>
/// <param name="TotalsRowCount">Number of totals rows.</param>
/// <param name="ColumnNames">List of table column names.</param>
public sealed record ExcelTableSnapshot(
    string TableName,
    string PartUri,
    string Reference,
    int HeaderRowCount,
    int TotalsRowCount,
    IReadOnlyList<string> ColumnNames);

/// <summary>
/// Extraction plan for Excel workbook.
/// </summary>
/// <param name="SourceHash">Hexadecimal SHA-256 digest of source file.</param>
/// <param name="Units">Ordered extraction units.</param>
/// <param name="Sheets">Discovered worksheet snapshots.</param>
/// <param name="Tables">Discovered table snapshots.</param>
public sealed record ExcelPlan(
    string SourceHash,
    IReadOnlyList<OfficeTranslationUnit> Units,
    IReadOnlyList<ExcelSheetSnapshot> Sheets,
    IReadOnlyList<ExcelTableSnapshot> Tables)
{

    /// <summary>
    /// Source inventory, selected ranges and extraction exclusions.
    /// </summary>
    public FileMetadata Metadata { get; init; } = FileMetadata.Create("excel");
}

/// <summary>
/// Prepared patch for Excel workbook.
/// </summary>
/// <param name="Plan">Original extraction plan.</param>
/// <param name="DecodedUnits">Validated and decoded translation units.</param>
/// <param name="EditMasks">Modification masks per touched part.</param>
public sealed record ExcelPreparedPatch(
    ExcelPlan Plan,
    IReadOnlyList<OfficeDecodedUnit> DecodedUnits,
    Dictionary<string, OfficeEditMask> EditMasks);
