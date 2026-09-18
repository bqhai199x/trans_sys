using DocumentFormat.OpenXml;
using FileHandler.Api.Modules.Office;
using A = DocumentFormat.OpenXml.Drawing;

namespace FileHandler.Api.Modules.PowerPoint;

/// <summary>
/// Reads DrawingML table topology and extracts cell paragraph text templates.
/// </summary>
public sealed class PowerPointTableReader
{

    /// <summary>
    /// Reads DrawingML table and extracts translation unit templates for each cell paragraph.
    /// </summary>
    /// <param name="table">DrawingML table element.</param>
    /// <param name="tableLoc">Base location of table shape.</param>
    /// <param name="units">Accumulated units collection.</param>
    /// <param name="codec">Office text codec.</param>
    /// <param name="limits">Active template quotas, or defaults.</param>
    /// <returns>No return value.</returns>
    public void ReadTable(
        A.Table table,
        OfficeLocation tableLoc,
        IList<OfficeTranslationUnit> units,
        OfficeTextCodec codec,
        OfficeProcessingOptions? limits = null)
    {
        var rowIndex = 0;
        foreach (var row in table.Elements<A.TableRow>())
        {
            rowIndex++;
            var colIndex = 0;

            foreach (var cell in row.Elements<A.TableCell>())
            {
                colIndex++;
                // Skip horizontal or vertical merge continuation cells
                if (cell.HorizontalMerge?.Value == true || cell.VerticalMerge?.Value == true)
                {
                    if (cell.TextBody is null || !HasNonEmptyText(cell.TextBody))
                        continue;
                }

                if (cell.TextBody is null)
                    continue;

                var cellLoc = new OfficeLocation(
                    tableLoc.PartUri,
                    tableLoc.ElementPath,
                    SlideId: tableLoc.SlideId,
                    ShapeId: tableLoc.ShapeId,
                    RowIndex: rowIndex,
                    ColumnIndex: colIndex);

                var paraOrdinal = 0;
                foreach (var p in cell.TextBody.Elements<A.Paragraph>())
                {
                    paraOrdinal++;
                    var template = DrawingTextCodec.ReadParagraph(p, cellLoc, paraOrdinal, limits);
                    if (template is not null)
                    {
                        var unitId = OfficeIdentity.CreateUnitId(
                            "office-v1",
                            string.Empty,
                            OfficeFormat.PowerPoint,
                            tableLoc.PartUri,
                            OfficeObjectKind.TableCell,
                            Array.Empty<OfficeElementPathSegment>(),
                            units.Count);

                        var unit = new OfficeTranslationUnit(
                            units.Count,
                            unitId,
                            unitId,
                            cellLoc,
                            template.Mode,
                            codec.Encode(template),
                            template.Slots,
                            template.Anchors,
                            template.Bindings,
                            string.Empty);

                        units.Add(unit);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Determines whether text body contains any non-empty text nodes.
    /// </summary>
    /// <param name="body">DrawingML text body.</param>
    /// <returns>True when text body contains non-empty text; otherwise false.</returns>
    private static bool HasNonEmptyText(A.TextBody body)
    {
        foreach (var t in body.Descendants<A.Text>())
        {
            if (!string.IsNullOrEmpty(t.Text))
                return true;
        }
        return false;
    }
}
