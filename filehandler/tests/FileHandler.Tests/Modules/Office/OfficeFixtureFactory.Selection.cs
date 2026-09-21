using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;
using P = DocumentFormat.OpenXml.Presentation;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace FileHandler.Tests.Modules.Office;

internal static partial class OfficeFixtureFactory
{

    /// <summary>
    /// Creates non-contiguous sheet IDs, all visibility states, an empty worksheet and chartsheet.
    /// </summary>
    /// <returns>Workbook fixture with IDs 7, 21, 42, 99 and 140.</returns>
    internal static byte[] CreateSelectionWorkbook()
    {
        using var buffer = new MemoryStream();
        buffer.Write(CreateExcelWithInlineStrings([["One"]]));
        using (var document = SpreadsheetDocument.Open(buffer, true))
        {
            var workbook = document.WorkbookPart!;
            var first = workbook.Workbook!.Sheets!.Elements<S.Sheet>().Single();
            first.SheetId = 7;
            first.Name = "First";
            foreach (var (id, name, state, text) in new[]
            {
                (21U, "Hidden", S.SheetStateValues.Hidden, "Two"),
                (42U, "Secret", S.SheetStateValues.VeryHidden, "Three"),
                (99U, "Empty", S.SheetStateValues.Visible, "")
            })
            {
                var part = workbook.AddNewPart<WorksheetPart>();
                var data = new S.SheetData();
                if (text.Length > 0) data.Append(new S.Row(new S.Cell
                {
                    CellReference = "A1", DataType = S.CellValues.InlineString,
                    InlineString = new(new S.Text(text))
                }) { RowIndex = 1 });
                part.Worksheet = new(data);
                workbook.Workbook.Sheets.Append(new S.Sheet { SheetId = id, Name = name, State = state, Id = workbook.GetIdOfPart(part) });
            }
            var chart = workbook.AddNewPart<ChartsheetPart>();
            var drawing = chart.AddNewPart<DrawingsPart>();
            drawing.WorksheetDrawing = new Xdr.WorksheetDrawing();
            chart.Chartsheet = new(new S.Drawing { Id = chart.GetIdOfPart(drawing) });
            workbook.Workbook.Sheets.Append(new S.Sheet { SheetId = 140, Name = "Chart", Id = workbook.GetIdOfPart(chart) });
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// Creates slides with native IDs 300 and 900, one hidden and one title placeholder.
    /// </summary>
    /// <returns>Presentation selection fixture.</returns>
    internal static byte[] CreateSelectionPresentation()
    {
        using var buffer = new MemoryStream();
        buffer.Write(CreatePowerPointPresentation("Visible title", "Hidden text"));
        using (var document = PresentationDocument.Open(buffer, true))
        {
            var main = document.PresentationPart!;
            var slides = main.Presentation!.SlideIdList!.Elements<P.SlideId>().ToArray();
            slides[0].Id = 300;
            slides[1].Id = 900;
            var first = (SlidePart)main.GetPartById(slides[0].RelationshipId!);
            first.Slide!.Descendants<P.Shape>().Single().NonVisualShapeProperties!.ApplicationNonVisualDrawingProperties!
                .Append(new P.PlaceholderShape { Type = P.PlaceholderValues.Title });
            ((SlidePart)main.GetPartById(slides[1].RelationshipId!)).Slide!.Show = false;
        }
        return buffer.ToArray();
    }
}
