using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace FileHandler.Tests.Modules.Office;

internal static partial class OfficeFixtureFactory
{

    /// <summary>
    /// Creates supported text beside a schema-valid chart in Excel or PowerPoint.
    /// </summary>
    /// <param name="excel">Whether to create workbook instead of presentation.</param>
    /// <returns>Office fixture retaining a chart dependency.</returns>
    internal static byte[] CreateChartWithText(bool excel)
    {
        using var buffer = new MemoryStream();
        buffer.Write(excel ? CreateExcelWithInlineStrings([["Visible"]]) : CreatePowerPointPresentation("Visible"));
        if (excel)
        {
            using var document = SpreadsheetDocument.Open(buffer, true);
            var worksheet = document.WorkbookPart!.WorksheetParts.Single();
            var drawings = worksheet.AddNewPart<DrawingsPart>();
            var chart = drawings.AddNewPart<ChartPart>();
            chart.ChartSpace = ChartSpace("Sheet1!$A$1");
            var frame = new Xdr.GraphicFrame(
                new Xdr.NonVisualGraphicFrameProperties(new Xdr.NonVisualDrawingProperties { Id = 2, Name = "Chart" }, new Xdr.NonVisualGraphicFrameDrawingProperties()),
                new Xdr.Transform(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = 1000000, Cy = 1000000 }),
                new A.Graphic(new A.GraphicData(new C.ChartReference { Id = drawings.GetIdOfPart(chart) }) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/chart" }));
            drawings.WorksheetDrawing = new(new Xdr.AbsoluteAnchor(new Xdr.Position { X = 0, Y = 0 },
                new Xdr.Extent { Cx = 1000000, Cy = 1000000 }, frame, new Xdr.ClientData()));
            worksheet.Worksheet!.Append(new S.Drawing { Id = worksheet.GetIdOfPart(drawings) });
        }
        else
        {
            using var document = PresentationDocument.Open(buffer, true);
            var slide = document.PresentationPart!.SlideParts.Single();
            var chart = slide.AddNewPart<ChartPart>();
            chart.ChartSpace = ChartSpace("Sheet1!$A$1");
            slide.Slide!.CommonSlideData!.ShapeTree!.Append(new P.GraphicFrame(
                new P.NonVisualGraphicFrameProperties(new P.NonVisualDrawingProperties { Id = 3, Name = "Chart" },
                    new P.NonVisualGraphicFrameDrawingProperties(), new P.ApplicationNonVisualDrawingProperties()),
                new P.Transform(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = 1000000, Cy = 1000000 }),
                new A.Graphic(new A.GraphicData(new C.ChartReference { Id = slide.GetIdOfPart(chart) }) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/chart" })));
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// Creates a minimal schema-valid pie chart with source formula.
    /// </summary>
    /// <param name="formula">Chart source reference.</param>
    /// <returns>Chart part root.</returns>
    private static C.ChartSpace ChartSpace(string formula) => new(new C.Chart(new C.PlotArea(new C.Layout(),
        new C.PieChart(new C.PieChartSeries(new C.Index { Val = 0 }, new C.Order { Val = 0 },
            new C.Values(new C.NumberReference(new C.Formula(formula))))))));
}
