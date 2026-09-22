using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FileHandler.Api.Modules.Excel;
using FileHandler.Tests.Modules.Office;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace FileHandler.Tests.Modules.Excel;

/// <summary>
/// Regressions for reference safety and partial sheet-name processing.
/// </summary>
public sealed class ExcelRenameReviewTests
{

    /// <summary>
    /// Retains names for sheet references outside supported formula elements.
    /// </summary>
    /// <param name="owner">Reference carrier added beside editable cell.</param>
    /// <returns>Task completing after fallback and unchanged reference assertions.</returns>
    [Theory]
    [InlineData("shape")]
    [InlineData("consolidation")]
    [InlineData("hyperlinkRelationship")]
    public async Task Rename_UnsupportedReferenceCarrierRetainsName(string owner)
    {
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreateExcelWithInlineStrings([["Original"]]));
        using (var document = SpreadsheetDocument.Open(buffer, true))
        {
            var worksheet = document.WorkbookPart!.WorksheetParts.Single();
            if (owner == "shape")
            {
                var drawings = worksheet.AddNewPart<DrawingsPart>();
                drawings.WorksheetDrawing = new Xdr.WorksheetDrawing(new Xdr.AbsoluteAnchor(
                    new Xdr.Position { X = 0, Y = 0 }, new Xdr.Extent { Cx = 1000000, Cy = 1000000 },
                    new Xdr.Shape(new Xdr.NonVisualShapeProperties(new Xdr.NonVisualDrawingProperties { Id = 2, Name = "Linked" },
                        new Xdr.NonVisualShapeDrawingProperties()), new Xdr.ShapeProperties()) { TextLink = "Sheet1!$A$1" },
                    new Xdr.ClientData()));
                worksheet.Worksheet!.Append(new S.Drawing { Id = worksheet.GetIdOfPart(drawings) });
            }
            else if (owner == "consolidation")
            {
                worksheet.Worksheet!.Append(new S.DataConsolidate(new S.DataReferences(
                    new S.DataReference { Sheet = "Sheet1", Reference = "B1:B2" }) { Count = 1 }));
            }
            else
            {
                var relationship = worksheet.AddHyperlinkRelationship(new Uri("#Sheet1!A1", UriKind.Relative), true);
                worksheet.Worksheet!.Append(new S.Hyperlinks(new S.Hyperlink { Reference = "A1", Id = relationship.Id }));
            }
        }
        var bytes = buffer.ToArray();
        AssertValidSource(bytes);
        var output = await ExcelService.Create().ExportAsync(new MemoryStream(bytes), ["Renamed", "Translated"]);
        Assert.Empty(output.Errors);
        Assert.Equal("partial", output.Metadata.Status);
        Assert.Contains(output.Metadata.Skipped, s => s.Code == "unsafe_sheet_reference" && s.UnitIndex == 0);
        using var result = SpreadsheetDocument.Open(new MemoryStream(output.Content!), false);
        Assert.Equal("Sheet1", result.WorkbookPart!.Workbook!.Sheets!.Elements<S.Sheet>().Single().Name!.Value);
        var worksheetResult = result.WorkbookPart.WorksheetParts.Single();
        Assert.Equal("Translated", worksheetResult.Worksheet!.Descendants<S.Text>().Single().Text);
        if (owner == "shape")
            Assert.Equal("Sheet1!$A$1", worksheetResult.DrawingsPart!.WorksheetDrawing!.Descendants<Xdr.Shape>().Single().TextLink!.Value);
        else if (owner == "consolidation")
            Assert.Equal("Sheet1", worksheetResult.Worksheet.Descendants<S.DataReference>().Single().Sheet!.Value);
        else
            Assert.Equal("#Sheet1!A1", worksheetResult.HyperlinkRelationships.Single().Uri.OriginalString);
    }

    /// <summary>
    /// Leaves external workbook hyperlink locations independent of local sheet names.
    /// </summary>
    /// <returns>Task completing after external target and local rename assertions.</returns>
    [Fact]
    public async Task Rename_PreservesExternalHyperlinkLocation()
    {
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreateExcelWithInlineStrings([["Original"]]));
        var target = new Uri("https://example.test/other.xlsx");
        using (var document = SpreadsheetDocument.Open(buffer, true))
        {
            var worksheet = document.WorkbookPart!.WorksheetParts.Single();
            var relationship = worksheet.AddHyperlinkRelationship(target, true);
            worksheet.Worksheet!.Append(new S.Hyperlinks(new S.Hyperlink
            {
                Reference = "A1", Id = relationship.Id, Location = "Sheet1!A1"
            }));
        }
        var bytes = buffer.ToArray();
        AssertValidSource(bytes);
        var output = await ExcelService.Create().ExportAsync(new MemoryStream(bytes), ["Renamed", "Translated"]);
        Assert.Empty(output.Errors);
        Assert.Equal("success", output.Metadata.Status);
        using var result = SpreadsheetDocument.Open(new MemoryStream(output.Content!), false);
        Assert.Equal("Renamed", result.WorkbookPart!.Workbook!.Sheets!.Elements<S.Sheet>().Single().Name!.Value);
        var worksheetResult = result.WorkbookPart.WorksheetParts.Single();
        Assert.Equal("Sheet1!A1", worksheetResult.Worksheet!.Descendants<S.Hyperlink>().Single().Location!.Value);
        Assert.Equal(target, worksheetResult.HyperlinkRelationships.Single().Uri);
    }

    /// <summary>
    /// Updates formula-valued conditional formatting thresholds with local sheet names.
    /// </summary>
    /// <returns>Task completing after threshold and cell translation assertions.</returns>
    [Fact]
    public async Task Rename_UpdatesConditionalFormattingThresholdFormula()
    {
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreateExcelWithInlineStrings([["Original"]]));
        using (var document = SpreadsheetDocument.Open(buffer, true))
        {
            var worksheet = document.WorkbookPart!.WorksheetParts.Single().Worksheet!;
            worksheet.Append(new S.ConditionalFormatting(new S.ConditionalFormattingRule(new S.ColorScale(
                new S.ConditionalFormatValueObject { Type = S.ConditionalFormatValueObjectValues.Formula, Val = "Sheet1!$B$1" },
                new S.ConditionalFormatValueObject { Type = S.ConditionalFormatValueObjectValues.Max },
                new S.Color { Rgb = "FFFF0000" }, new S.Color { Rgb = "FF00FF00" }))
            { Type = S.ConditionalFormatValues.ColorScale, Priority = 1 })
            { SequenceOfReferences = new DocumentFormat.OpenXml.ListValue<DocumentFormat.OpenXml.StringValue> { InnerText = "B1:B2" } });
        }
        var bytes = buffer.ToArray();
        AssertValidSource(bytes);
        var output = await ExcelService.Create().ExportAsync(new MemoryStream(bytes), ["Renamed", "Translated"]);
        Assert.Empty(output.Errors);
        Assert.Equal("success", output.Metadata.Status);
        using var result = SpreadsheetDocument.Open(new MemoryStream(output.Content!), false);
        Assert.Equal("Renamed", result.WorkbookPart!.Workbook!.Sheets!.Elements<S.Sheet>().Single().Name!.Value);
        var worksheetResult = result.WorkbookPart.WorksheetParts.Single().Worksheet!;
        Assert.Equal("'Renamed'!$B$1", worksheetResult.Descendants<S.ConditionalFormatValueObject>().First().Val!.Value);
        Assert.Equal("Translated", worksheetResult.Descendants<S.Text>().Single().Text);
    }

    /// <summary>
    /// Preserves structured column names while rewriting adjacent sheet references.
    /// </summary>
    /// <param name="formula">Source formula containing structured references.</param>
    /// <param name="expected">Formula after renaming Sheet1.</param>
    /// <returns>Task completing after output formula and table assertions.</returns>
    [Theory]
    [InlineData("SUM(Items[[Sheet1!A1]])", "SUM(Items[[Sheet1!A1]])")]
    [InlineData("SUM(Items[[#Data],[Sheet1!A1]])+Sheet1!B2", "SUM(Items[[#Data],[Sheet1!A1]])+'Renamed'!B2")]
    public async Task Rename_PreservesStructuredColumnNames(string formula, string expected)
    {
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreateExcelWithTable("Items", "A1:B2", ["Sheet1!A1", "Value"], [["Item", "1"]]));
        using (var document = SpreadsheetDocument.Open(buffer, true))
        {
            document.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<S.Row>().Last()
                .Append(new S.Cell(new S.CellFormula(formula)) { CellReference = "C2" });
        }
        var bytes = buffer.ToArray();
        AssertValidSource(bytes);
        var output = await ExcelService.Create().ExportAsync(new MemoryStream(bytes), ["Renamed", "Translated"]);
        Assert.Empty(output.Errors);
        Assert.Equal("success", output.Metadata.Status);
        using var result = SpreadsheetDocument.Open(new MemoryStream(output.Content!), false);
        var worksheet = result.WorkbookPart!.WorksheetParts.Single();
        Assert.Equal("Renamed", result.WorkbookPart.Workbook!.Sheets!.Elements<S.Sheet>().Single().Name!.Value);
        Assert.Equal(expected, worksheet.Worksheet!.Descendants<S.CellFormula>().Single().Text);
        Assert.Equal("Sheet1!A1", worksheet.TableDefinitionParts.Single().Table!.TableColumns!.Elements<S.TableColumn>().First().Name!.Value);
        Assert.Contains(worksheet.Worksheet.Descendants<S.Text>(), t => t.Text == "Translated");
    }

    /// <summary>
    /// Distinguishes escaped column text, external references and formula literals.
    /// </summary>
    /// <param name="formula">Source formula.</param>
    /// <param name="expected">Rewritten formula, or null when syntax is uncertain.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData("SUM(Items[[Sheet1!A1]])+Sheet1!A1", "SUM(Items[[Sheet1!A1]])+'O''Brien'!A1")]
    [InlineData("Items[O''Brien Sheet1!A1]+Sheet1!A1", "Items[O''Brien Sheet1!A1]+'O''Brien'!A1")]
    [InlineData("Items[Column']Sheet1!A1]+Sheet1!A1", "Items[Column']Sheet1!A1]+'O''Brien'!A1")]
    [InlineData("Items['[Sheet1!A1']]+Sheet1!A1", "Items['[Sheet1!A1']]+'O''Brien'!A1")]
    [InlineData("Items[[#Headers],[Sheet1!A1]]&\"Sheet1!A1\"", "Items[[#Headers],[Sheet1!A1]]&\"Sheet1!A1\"")]
    [InlineData("[Book.xlsx]Sheet1!A1", null)]
    [InlineData("'[Book.xlsx]Sheet1'!A1", null)]
    [InlineData("Items[[Sheet1!A1]", null)]
    [InlineData("Items[Column']+Sheet1!A1", null)]
    public void References_PreserveStructuredReferenceTokens(string formula, string? expected)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Sheet1"] = "O'Brien" };
        Assert.Equal(expected is not null, ExcelFormulaReferences.TryRewrite(formula, names, out var actual));
        Assert.Equal(expected ?? formula, actual);
    }

    /// <summary>
    /// Retains names for unsupported VML formulas while translating ordinary cells.
    /// </summary>
    /// <param name="formulaElement">VML formula element containing sheet reference.</param>
    /// <returns>Task completing after partial-result and VML preservation assertions.</returns>
    [Theory]
    [InlineData("FmlaLink")]
    [InlineData("FmlaRange")]
    public async Task Rename_VmlReferencesRetainSheetName(string formulaElement)
    {
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreateExcelWithInlineStrings([["Original"]]));
        string vmlUri;
        var vml = "<xml xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:x=\"urn:schemas-microsoft-com:office:excel\">" +
            "<v:shape id=\"_x0000_s1025\"><x:ClientData ObjectType=\"Checkbox\"><x:" + formulaElement + ">Sheet1!$A$1</x:" +
            formulaElement + "></x:ClientData></v:shape></xml>";
        using (var document = SpreadsheetDocument.Open(buffer, true))
        {
            var worksheet = document.WorkbookPart!.WorksheetParts.Single();
            var part = worksheet.AddNewPart<VmlDrawingPart>();
            part.FeedData(new MemoryStream(Encoding.UTF8.GetBytes(vml)));
            vmlUri = part.Uri.ToString();
            worksheet.Worksheet!.Append(new S.LegacyDrawing { Id = worksheet.GetIdOfPart(part) });
        }
        var bytes = buffer.ToArray();
        AssertValidSource(bytes);
        var output = await ExcelService.Create().ExportAsync(new MemoryStream(bytes), ["Renamed", "Translated"]);
        Assert.Empty(output.Errors);
        Assert.Equal("partial", output.Metadata.Status);
        Assert.Contains(output.Metadata.Skipped, s => s.Code == "unsafe_sheet_reference" && s.UnitIndex == 0);
        Assert.Equal("Sheet1", Assert.Single(output.Metadata.SheetNameChanges!).FinalName);
        using var result = SpreadsheetDocument.Open(new MemoryStream(output.Content!), false);
        Assert.Equal("Sheet1", result.WorkbookPart!.Workbook!.Sheets!.Elements<S.Sheet>().Single().Name!.Value);
        var worksheetResult = result.WorkbookPart.WorksheetParts.Single();
        Assert.Equal("Translated", worksheetResult.Worksheet!.Descendants<S.Text>().Single().Text);
        var vmlResult = Assert.Single(worksheetResult.VmlDrawingParts);
        Assert.Equal(vmlUri, vmlResult.Uri.ToString());
        using var reader = new StreamReader(vmlResult.GetStream());
        Assert.Equal(vml, reader.ReadToEnd());
    }

    /// <summary>
    /// Rejects invalid XML names locally and preserves valid cell translations.
    /// </summary>
    /// <param name="codeUnit">Forbidden XML character or unmatched UTF-16 surrogate.</param>
    /// <returns>Task completing after name fallback and cell assertions.</returns>
    [Theory]
    [InlineData(0xFFFE)]
    [InlineData(0xFFFF)]
    [InlineData(0xD800)]
    [InlineData(0xDC00)]
    public async Task Rename_InvalidXmlNameRetainsSource(int codeUnit)
    {
        var bytes = OfficeFixtureFactory.CreateExcelWithInlineStrings([["Original"]]);
        var requested = "Bad" + (char)codeUnit + "Name";
        var output = await ExcelService.Create().ExportAsync(new MemoryStream(bytes), [requested, "Translated"]);
        Assert.Empty(output.Errors);
        Assert.Equal("partial", output.Metadata.Status);
        Assert.Contains(output.Metadata.Skipped, s => s.Code == "invalid_translation" && s.UnitIndex == 0);
        Assert.Equal("Sheet1", Assert.Single(output.Metadata.SheetNameChanges!).FinalName);
        using var result = SpreadsheetDocument.Open(new MemoryStream(output.Content!), false);
        Assert.Equal("Sheet1", result.WorkbookPart!.Workbook!.Sheets!.Elements<S.Sheet>().Single().Name!.Value);
        Assert.Equal("Translated", result.WorkbookPart.WorksheetParts.Single().Worksheet!.Descendants<S.Text>().Single().Text);
    }

    /// <summary>
    /// Retains normalization of controls and valid Unicode sheet names.
    /// </summary>
    /// <param name="requested">Raw translated sheet name.</param>
    /// <param name="expected">Normalized sheet name.</param>
    /// <returns>Task completing after successful export assertions.</returns>
    [Theory]
    [InlineData("A\tB", "A_B")]
    [InlineData("A\0B", "A_B")]
    [InlineData("日本語😀", "日本語😀")]
    public async Task Rename_NormalizesBeforeXmlValidation(string requested, string expected)
    {
        var bytes = OfficeFixtureFactory.CreateExcelWithInlineStrings([["Original"]]);
        var output = await ExcelService.Create().ExportAsync(new MemoryStream(bytes), [requested, "Translated"]);
        Assert.Empty(output.Errors);
        Assert.Equal("success", output.Metadata.Status);
        Assert.Equal(expected, Assert.Single(output.Metadata.SheetNameChanges!).FinalName);
    }

    /// <summary>
    /// Verifies fixture schema before testing export behavior.
    /// </summary>
    /// <param name="bytes">Source workbook bytes.</param>
    /// <returns>No return value.</returns>
    private static void AssertValidSource(byte[] bytes)
    {
        using var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        Assert.Empty(new OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2019).Validate(document));
    }
}
