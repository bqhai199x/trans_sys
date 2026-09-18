using System.Globalization;
using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace FileHandler.Tests.Modules.Office;

/// <summary>
/// Programmatic test fixture generator for valid and invalid Office Open XML documents.
/// </summary>
internal static class OfficeFixtureFactory
{

    /// <summary>
    /// Creates a minimal valid Word document (.docx) with specified paragraphs.
    /// </summary>
    /// <param name="paragraphs">Text content of paragraphs to insert.</param>
    /// <returns>Bytes of generated .docx package.</returns>
    internal static byte[] CreateWordDocument(params string[] paragraphs)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new W.Document(new W.Body());
            foreach (var text in paragraphs)
            {
                mainPart.Document.Body!.AppendChild(new W.Paragraph(new W.Run(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve })));
            }
            mainPart.Document.Save();
        }
        return stream.ToArray();
    }

    /// <summary>
    /// Creates a Word document with custom body elements such as tables and paragraphs.
    /// </summary>
    /// <param name="elements">Open XML body elements to append.</param>
    /// <returns>Bytes of generated .docx package.</returns>
    internal static byte[] CreateWordDocumentWithElements(params OpenXmlElement[] elements)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new W.Document(new W.Body());
            foreach (var el in elements)
            {
                mainPart.Document.Body!.AppendChild(el.CloneNode(true));
            }
            mainPart.Document.Save();
        }
        return stream.ToArray();
    }

    /// <summary>
    /// Creates a Word document containing headers, footers, footnotes, endnotes, and comments.
    /// </summary>
    /// <param name="bodyText">Body paragraph text.</param>
    /// <param name="headerText">Header paragraph text.</param>
    /// <param name="footerText">Footer paragraph text.</param>
    /// <returns>Bytes of generated .docx package.</returns>
    internal static byte[] CreateWordWithStories(string bodyText, string headerText, string footerText)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new W.Document(new W.Body());
            mainPart.Document.Body!.AppendChild(new W.Paragraph(new W.Run(new W.Text(bodyText))));

            var headerPart = mainPart.AddNewPart<HeaderPart>();
            headerPart.Header = new W.Header(new W.Paragraph(new W.Run(new W.Text(headerText))));
            headerPart.Header.Save();
            var headerRelId = mainPart.GetIdOfPart(headerPart);

            var footerPart = mainPart.AddNewPart<FooterPart>();
            footerPart.Footer = new W.Footer(new W.Paragraph(new W.Run(new W.Text(footerText))));
            footerPart.Footer.Save();
            var footerRelId = mainPart.GetIdOfPart(footerPart);

            var sectionProps = new W.SectionProperties(
                new W.HeaderReference { Id = headerRelId, Type = W.HeaderFooterValues.Default },
                new W.FooterReference { Id = footerRelId, Type = W.HeaderFooterValues.Default });
            mainPart.Document.Body.AppendChild(sectionProps);

            mainPart.Document.Save();
        }
        return stream.ToArray();
    }

    /// <summary>
    /// Creates a Word table helper element.
    /// </summary>
    /// <param name="cells">Table cells to include in a single row.</param>
    /// <returns>Word table element.</returns>
    internal static W.Table MakeWordTable(params W.TableCell[] cells)
    {
        var table = new W.Table(
            new W.TableProperties(),
            new W.TableGrid(cells.Select(_ => new W.GridColumn { Width = "2400" })),
            new W.TableRow(cells));
        return table;
    }

    /// <summary>
    /// Creates a Word paragraph with one run of text.
    /// </summary>
    /// <param name="text">Text string.</param>
    /// <returns>Word paragraph element.</returns>
    internal static W.Paragraph WordParagraph(string text) =>
        new(new W.Run(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    /// <summary>
    /// Creates a minimal valid Excel workbook (.xlsx) with inline string cells in Sheet1.
    /// </summary>
    /// <param name="cellValues">Grid of cell text values [row][col].</param>
    /// <returns>Bytes of generated .xlsx package.</returns>
    internal static byte[] CreateExcelWithInlineStrings(string[][] cellValues)
    {
        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new S.Workbook(new S.Sheets());

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new S.SheetData();
            worksheetPart.Worksheet = new S.Worksheet(sheetData);

            for (var r = 0; r < cellValues.Length; r++)
            {
                var row = new S.Row { RowIndex = (uint)(r + 1) };
                for (var c = 0; c < cellValues[r].Length; c++)
                {
                    var colLetter = (char)('A' + c);
                    var cellRef = $"{colLetter}{r + 1}";
                    var val = cellValues[r][c];
                    var cell = new S.Cell
                    {
                        CellReference = cellRef,
                        DataType = S.CellValues.InlineString,
                        InlineString = new S.InlineString(new S.Text(val) { Space = SpaceProcessingModeValues.Preserve })
                    };
                    row.AppendChild(cell);
                }
                sheetData.AppendChild(row);
            }

            var sheet = new S.Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1U,
                Name = "Sheet1"
            };
            workbookPart.Workbook.Sheets!.AppendChild(sheet);

            worksheetPart.Worksheet.Save();
            workbookPart.Workbook.Save();
        }
        return stream.ToArray();
    }

    /// <summary>
    /// Creates an Excel workbook (.xlsx) with Shared String Table (SST).
    /// </summary>
    /// <param name="sharedStrings">SST items.</param>
    /// <param name="cellIndexes">Grid of SST index references [row][col].</param>
    /// <returns>Bytes of generated .xlsx package.</returns>
    internal static byte[] CreateExcelWithSharedStrings(string[] sharedStrings, int[][] cellIndexes)
    {
        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new S.Workbook(new S.Sheets());

            var sstPart = workbookPart.AddNewPart<SharedStringTablePart>();
            var sst = new S.SharedStringTable
            {
                Count = (uint)sharedStrings.Length,
                UniqueCount = (uint)sharedStrings.Length
            };
            foreach (var s in sharedStrings)
            {
                sst.AppendChild(new S.SharedStringItem(new S.Text(s)));
            }
            sstPart.SharedStringTable = sst;
            sstPart.SharedStringTable.Save();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new S.SheetData();
            worksheetPart.Worksheet = new S.Worksheet(sheetData);

            for (var r = 0; r < cellIndexes.Length; r++)
            {
                var row = new S.Row { RowIndex = (uint)(r + 1) };
                for (var c = 0; c < cellIndexes[r].Length; c++)
                {
                    var colLetter = (char)('A' + c);
                    var cellRef = $"{colLetter}{r + 1}";
                    var sstIdx = cellIndexes[r][c];
                    var cell = new S.Cell
                    {
                        CellReference = cellRef,
                        DataType = S.CellValues.SharedString,
                        CellValue = new S.CellValue(sstIdx.ToString())
                    };
                    row.AppendChild(cell);
                }
                sheetData.AppendChild(row);
            }

            var sheet = new S.Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1U,
                Name = "Sheet1"
            };
            workbookPart.Workbook.Sheets!.AppendChild(sheet);

            worksheetPart.Worksheet.Save();
            workbookPart.Workbook.Save();
        }
        return stream.ToArray();
    }

    /// <summary>
    /// Creates an Excel workbook with a structured Table definition (TableDefinitionPart).
    /// </summary>
    /// <param name="tableName">Table name.</param>
    /// <param name="tableRange">Table cell range e.g. A1:B2.</param>
    /// <param name="columns">Column header names.</param>
    /// <param name="dataRows">Data row cell text values.</param>
    /// <param name="formulaRow">Optional formula cell definitions.</param>
    /// <returns>Bytes of generated .xlsx package.</returns>
    internal static byte[] CreateExcelWithTable(
        string tableName,
        string tableRange,
        string[] columns,
        string[][] dataRows,
        (string cellRef, string formula, string val)[]? formulaRow = null)
    {
        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new S.Workbook(new S.Sheets());

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new S.SheetData();
            worksheetPart.Worksheet = new S.Worksheet(sheetData);

            // Row 1: Headers
            var headerRow = new S.Row { RowIndex = 1U };
            for (var c = 0; c < columns.Length; c++)
            {
                var colLetter = (char)('A' + c);
                headerRow.AppendChild(new S.Cell
                {
                    CellReference = $"{colLetter}1",
                    DataType = S.CellValues.InlineString,
                    InlineString = new S.InlineString(new S.Text(columns[c]))
                });
            }
            sheetData.AppendChild(headerRow);

            // Data rows
            for (var r = 0; r < dataRows.Length; r++)
            {
                var row = new S.Row { RowIndex = (uint)(r + 2) };
                for (var c = 0; c < dataRows[r].Length; c++)
                {
                    var colLetter = (char)('A' + c);
                    var cellVal = dataRows[r][c];
                    if (double.TryParse(cellVal, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                    {
                        row.AppendChild(new S.Cell
                        {
                            CellReference = $"{colLetter}{r + 2}",
                            CellValue = new S.CellValue(cellVal)
                        });
                    }
                    else
                    {
                        row.AppendChild(new S.Cell
                        {
                            CellReference = $"{colLetter}{r + 2}",
                            DataType = S.CellValues.InlineString,
                            InlineString = new S.InlineString(new S.Text(cellVal))
                        });
                    }
                }
                sheetData.AppendChild(row);
            }

            // Formula rows if specified
            if (formulaRow is not null)
            {
                var fRow = new S.Row { RowIndex = (uint)(dataRows.Length + 2) };
                foreach (var (cellRef, formula, val) in formulaRow)
                {
                    fRow.AppendChild(new S.Cell
                    {
                        CellReference = cellRef,
                        CellFormula = new S.CellFormula(formula),
                        CellValue = new S.CellValue(val)
                    });
                }
                sheetData.AppendChild(fRow);
            }

            // Table part
            var tablePart = worksheetPart.AddNewPart<TableDefinitionPart>();
            var tableCols = new S.TableColumns { Count = (uint)columns.Length };
            for (uint i = 0; i < columns.Length; i++)
            {
                tableCols.AppendChild(new S.TableColumn { Id = i + 1, Name = columns[(int)i] });
            }

            tablePart.Table = new S.Table(
                new S.AutoFilter { Reference = tableRange },
                tableCols)
            {
                Id = 1U,
                Name = tableName,
                DisplayName = tableName,
                Reference = tableRange,
                TotalsRowShown = false
            };
            tablePart.Table.Save();

            worksheetPart.Worksheet.AppendChild(new S.TableParts(
                new S.TablePart { Id = worksheetPart.GetIdOfPart(tablePart) })
            { Count = 1U });

            var sheet = new S.Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1U,
                Name = "Sheet1"
            };
            workbookPart.Workbook.Sheets!.AppendChild(sheet);

            worksheetPart.Worksheet.Save();
            workbookPart.Workbook.Save();
        }
        return stream.ToArray();
    }

    /// <summary>
    /// Creates a minimal valid PowerPoint presentation (.pptx) with slides.
    /// </summary>
    /// <param name="slideTexts">Array of slide text contents.</param>
    /// <returns>Bytes of generated .pptx package.</returns>
    internal static byte[] CreatePowerPointPresentation(params string[] slideTexts)
    {
        using var stream = new MemoryStream();
        using (var doc = PresentationDocument.Create(stream, PresentationDocumentType.Presentation, true))
        {
            var presentationPart = doc.AddPresentationPart();
            presentationPart.Presentation = new P.Presentation(new P.SlideIdList());

            uint slideId = 256U;
            foreach (var text in slideTexts)
            {
                var slidePart = presentationPart.AddNewPart<SlidePart>();
                var shapeTree = new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties());

                var shape = new P.Shape(
                    new P.NonVisualShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 2U, Name = "TextBox 1" },
                        new P.NonVisualShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.ShapeProperties(),
                    new P.TextBody(
                        new A.BodyProperties(),
                        new A.ListStyle(),
                        new A.Paragraph(new A.Run(new A.Text(text)))));

                shapeTree.AppendChild(shape);
                slidePart.Slide = new P.Slide(new P.CommonSlideData(shapeTree));
                slidePart.Slide.Save();

                var relId = presentationPart.GetIdOfPart(slidePart);
                presentationPart.Presentation.SlideIdList!.AppendChild(new P.SlideId { Id = slideId++, RelationshipId = relId });
            }

            AddPresentationMetadata(presentationPart);
            presentationPart.Presentation.Save();
        }
        return stream.ToArray();
    }

    /// <summary>
    /// Creates a PowerPoint presentation with an A.Table on first slide.
    /// </summary>
    /// <param name="rows">DrawingML table rows to insert.</param>
    /// <returns>Bytes of generated .pptx package.</returns>
    internal static byte[] CreatePowerPointWithTable(params A.TableRow[] rows)
    {
        using var stream = new MemoryStream();
        using (var doc = PresentationDocument.Create(stream, PresentationDocumentType.Presentation, true))
        {
            var presentationPart = doc.AddPresentationPart();
            presentationPart.Presentation = new P.Presentation(new P.SlideIdList());

            var slidePart = presentationPart.AddNewPart<SlidePart>();
            var shapeTree = new P.ShapeTree(
                new P.NonVisualGroupShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                    new P.NonVisualGroupShapeDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.GroupShapeProperties());

            var table = new A.Table(
                new A.TableProperties(),
                new A.TableGrid(new A.GridColumn { Width = 3000000L }, new A.GridColumn { Width = 3000000L }));
            foreach (var row in rows)
            {
                var cloned = (A.TableRow)row.CloneNode(true);
                if (cloned.Height is null)
                    cloned.Height = 370840L;
                table.AppendChild(cloned);
            }

            var graphicFrame = new P.GraphicFrame(
                new P.NonVisualGraphicFrameProperties(
                    new P.NonVisualDrawingProperties { Id = 2U, Name = "Table 1" },
                    new P.NonVisualGraphicFrameDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.Transform(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = 6000000L, Cy = 2000000L }),
                new A.Graphic(new A.GraphicData(table) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/table" }));

            shapeTree.AppendChild(graphicFrame);
            slidePart.Slide = new P.Slide(new P.CommonSlideData(shapeTree));
            slidePart.Slide.Save();

            var relId = presentationPart.GetIdOfPart(slidePart);
            presentationPart.Presentation.SlideIdList!.AppendChild(new P.SlideId { Id = 256U, RelationshipId = relId });
            AddPresentationMetadata(presentationPart);
            presentationPart.Presentation.Save();
        }
        return stream.ToArray();
    }

    /// <summary>
    /// Adds required presentation metadata for schema-valid fixtures.
    /// </summary>
    /// <param name="main">Presentation part.</param>
    /// <returns>No return value.</returns>
    private static void AddPresentationMetadata(PresentationPart main)
    {
        var master = main.AddNewPart<SlideMasterPart>();
        master.SlideMaster = new P.SlideMaster(
            new P.CommonSlideData(new P.ShapeTree(new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                new P.NonVisualGroupShapeDrawingProperties(), new P.ApplicationNonVisualDrawingProperties()), new P.GroupShapeProperties())),
            new P.ColorMap
            {
                Background1 = A.ColorSchemeIndexValues.Light1,
                Text1 = A.ColorSchemeIndexValues.Dark1,
                Background2 = A.ColorSchemeIndexValues.Light2,
                Text2 = A.ColorSchemeIndexValues.Dark2,
                Accent1 = A.ColorSchemeIndexValues.Accent1,
                Accent2 = A.ColorSchemeIndexValues.Accent2,
                Accent3 = A.ColorSchemeIndexValues.Accent3,
                Accent4 = A.ColorSchemeIndexValues.Accent4,
                Accent5 = A.ColorSchemeIndexValues.Accent5,
                Accent6 = A.ColorSchemeIndexValues.Accent6,
                Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink
            });
        main.Presentation!.PrependChild(new P.SlideMasterIdList(new P.SlideMasterId
        { Id = 2147483648U, RelationshipId = main.GetIdOfPart(master) }));
        main.Presentation.AppendChild(new P.NotesSize { Cx = 6858000L, Cy = 9144000L });
    }

    /// <summary>
    /// Creates a DrawingML table cell with specified paragraphs.
    /// </summary>
    /// <param name="paragraphs">DrawingML paragraphs in cell.</param>
    /// <returns>DrawingML TableCell element.</returns>
    internal static A.TableCell DrawingCell(params A.Paragraph[] paragraphs)
    {
        var body = new A.TextBody(new A.BodyProperties(), new A.ListStyle());
        foreach (var p in paragraphs)
        {
            body.AppendChild(p.CloneNode(true));
        }
        return new A.TableCell(body, new A.TableCellProperties());
    }

    /// <summary>
    /// Creates a non-Office ZIP file for preflight testing.
    /// </summary>
    /// <returns>Bytes of non-office zip archive.</returns>
    internal static byte[] CreateNonOfficeZip()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("notes.txt");
            using var entryStream = entry.Open();
            entryStream.Write(Encoding.UTF8.GetBytes("Just some notes."));
        }
        return stream.ToArray();
    }

    /// <summary>
    /// Creates a package with an XML entry containing a DTD declaration.
    /// </summary>
    /// <returns>Bytes of corrupted zip archive with DTD.</returns>
    internal static byte[] CreatePackageWithDtd()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var rels = zip.CreateEntry("_rels/.rels");
            using (var rs = rels.Open())
            {
                rs.Write(Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"utf-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"></Relationships>"));
            }

            var ct = zip.CreateEntry("[Content_Types].xml");
            using (var s = ct.Open())
            {
                var content = "<?xml version=\"1.0\" encoding=\"utf-8\"?><!DOCTYPE Types [<!ENTITY xxe \"exploit\">]><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"></Types>";
                s.Write(Encoding.UTF8.GetBytes(content));
            }
        }
        return stream.ToArray();
    }
}
