using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Excel;
using FileHandler.Tests.Modules.Office;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace FileHandler.Tests.Modules.Excel;

/// <summary>
/// Excel module acceptance, table protection, SST CoW, and formula tests (E01-E18).
/// </summary>
public sealed class ExcelModuleTests
{

    /// <summary>
    /// Creates ExcelService for testing.
    /// </summary>
    /// <param name="fileOptions">File handling options.</param>
    /// <returns>Configured ExcelService instance.</returns>
    private static ExcelService CreateService(FileHandlingOptions? fileOptions = null) =>
        ExcelService.Create(fileOptions is not null ? Microsoft.Extensions.Options.Options.Create(fileOptions) : null);

    /// <summary>
    /// Verifies E01: Inline string cells extracted and translated.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task E01_InlineStringCells_ExtractsAndTranslates()
    {
        var service = CreateService();
        var xlsx = OfficeFixtureFactory.CreateExcelWithInlineStrings(new[]
        {
            new[] { "Alpha", "Beta" },
            new[] { "Gamma", "Delta" }
        });

        using var importStream = new MemoryStream(xlsx);
        var importResult = await service.ImportAsync(importStream);

        Assert.Empty(importResult.Errors);
        Assert.Equal(new[] { "Sheet1", "Alpha", "Beta", "Gamma", "Delta" }, importResult.Texts);

        using var exportStream = new MemoryStream(xlsx);
        var exportResult = await service.ExportAsync(exportStream, new[] { "Sheet1", "A1", "B1", "G1", "D1" });

        Assert.Empty(exportResult.Errors);
        Assert.NotNull(exportResult.Content);

        using var doc = SpreadsheetDocument.Open(new MemoryStream(exportResult.Content!), false);
        var cells = doc.WorkbookPart!.WorksheetParts.First().Worksheet!.Descendants<S.Cell>().ToList();
        Assert.Equal("A1", cells[0].InnerText);
        Assert.Equal("B1", cells[1].InnerText);
    }

    /// <summary>
    /// Verifies E02: Shared string table cells extracted and translated.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task E02_SharedStrings_ExtractsAndTranslates()
    {
        var service = CreateService();
        var xlsx = OfficeFixtureFactory.CreateExcelWithSharedStrings(
            new[] { "SharedA", "SharedB" },
            new[] { new[] { 0, 1 }, new[] { 0, 1 } });

        using var importStream = new MemoryStream(xlsx);
        var importResult = await service.ImportAsync(importStream);

        Assert.Empty(importResult.Errors);
        Assert.Equal(5, importResult.Texts.Count);
        Assert.Equal("SharedA", importResult.Texts[1]);
        Assert.Equal("SharedB", importResult.Texts[2]);

        using var exportStream = new MemoryStream(xlsx);
        var exportResult = await service.ExportAsync(exportStream, new[] { "Sheet1", "T0", "T1", "T2", "T3" });

        Assert.Empty(exportResult.Errors);
        Assert.NotNull(exportResult.Content);
    }

    /// <summary>
    /// Verifies E03: Excel Table headers (Sales/Count) are protected; data cell (Item) translated; formula untouched.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task E03_TableHeadersProtected_TranslatesOnlyData()
    {
        var service = CreateService();
        var xlsx = OfficeFixtureFactory.CreateExcelWithTable(
            "Table1",
            "A1:B2",
            new[] { "Sales", "Count" },
            new[] { new[] { "Item", "7" } },
            new[] { ("C3", "SUM(Table1[Count])", "7") });

        using var importStream = new MemoryStream(xlsx);
        var importResult = await service.ImportAsync(importStream);

        Assert.Empty(importResult.Errors);
        // "Sales" and "Count" are table headers -> PROTECTED (no units)
        // "7" is numeric -> no unit
        // "Item" is string data cell in row 2 -> extracted
        Assert.Equal(new[] { "Sheet1", "Item" }, importResult.Texts);

        using var exportStream = new MemoryStream(xlsx);
        var exportResult = await service.ExportAsync(exportStream, new[] { "Sheet1", "MatHang" });

        Assert.Empty(exportResult.Errors);
        Assert.NotNull(exportResult.Content);

        // Verify headers, column metadata and formula preserved
        using var doc = SpreadsheetDocument.Open(new MemoryStream(exportResult.Content!), false);
        var wsPart = doc.WorkbookPart!.WorksheetParts.First();
        var tablePart = wsPart.TableDefinitionParts.First();

        Assert.Equal("Sales", tablePart.Table!.TableColumns!.Elements<S.TableColumn>().First().Name?.Value);
        Assert.Equal("Count", tablePart.Table!.TableColumns!.Elements<S.TableColumn>().Last().Name?.Value);

        var c3 = wsPart.Worksheet!.Descendants<S.Cell>().FirstOrDefault(c => c.CellReference?.Value == "C3");
        Assert.NotNull(c3);
        Assert.Equal("SUM(Table1[Count])", c3!.CellFormula?.Text);

        var a2 = wsPart.Worksheet!.Descendants<S.Cell>().FirstOrDefault(c => c.CellReference?.Value == "A2");
        Assert.NotNull(a2);
        Assert.Equal("MatHang", a2!.InnerText);
    }

    /// <summary>
    /// Verifies E05: Formulas and merged cells are preserved after export.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task E05_FormulasAndMergedCells_ArePreserved()
    {
        var service = CreateService();
        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook, true))
        {
            var wbPart = doc.AddWorkbookPart();
            wbPart.Workbook = new S.Workbook(new S.Sheets());

            var wsPart = wbPart.AddNewPart<WorksheetPart>();
            var sheetData = new S.SheetData();
            wsPart.Worksheet = new S.Worksheet(sheetData);

            var row1 = new S.Row { RowIndex = 1U };
            row1.AppendChild(new S.Cell
            {
                CellReference = "A1",
                DataType = S.CellValues.InlineString,
                InlineString = new S.InlineString(new S.Text("Text"))
            });
            row1.AppendChild(new S.Cell
            {
                CellReference = "B1",
                CellFormula = new S.CellFormula("\"Hello\""),
                CellValue = new S.CellValue("Hello")
            });
            sheetData.AppendChild(row1);

            var row2 = new S.Row { RowIndex = 2U };
            row2.AppendChild(new S.Cell
            {
                CellReference = "A2",
                DataType = S.CellValues.InlineString,
                InlineString = new S.InlineString(new S.Text("Merged"))
            });
            sheetData.AppendChild(row2);

            wsPart.Worksheet.AppendChild(new S.MergeCells(new S.MergeCell { Reference = "A2:B2" }));

            var sheet = new S.Sheet { Id = wbPart.GetIdOfPart(wsPart), SheetId = 1U, Name = "Sheet1" };
            wbPart.Workbook.Sheets!.AppendChild(sheet);

            wsPart.Worksheet.Save();
            wbPart.Workbook.Save();
        }

        var xlsx = stream.ToArray();

        using var importStream = new MemoryStream(xlsx);
        var importResult = await service.ImportAsync(importStream);

        Assert.Empty(importResult.Errors);
        Assert.Equal(new[] { "Sheet1", "Text", "Merged" }, importResult.Texts);

        using var exportStream = new MemoryStream(xlsx);
        var exportResult = await service.ExportAsync(exportStream, new[] { "Sheet1", "Text Translated", "Merged Translated" });

        Assert.Empty(exportResult.Errors);
        Assert.NotNull(exportResult.Content);

        using var resultDoc = SpreadsheetDocument.Open(new MemoryStream(exportResult.Content!), false);
        var resWs = resultDoc.WorkbookPart!.WorksheetParts.First().Worksheet!;
        var mergeCells = resWs.Descendants<S.MergeCell>().ToList();
        Assert.Single(mergeCells);
        Assert.Equal("A2:B2", mergeCells[0].Reference?.Value);
    }

    /// <summary>
    /// Verifies E11: Modifying SST removes optional count and uniqueCount counters (SstOptionalCountersRemoved).
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task E11_ModifyingSst_RemovesOptionalCountAndUniqueCount()
    {
        var service = CreateService();
        var xlsx = OfficeFixtureFactory.CreateExcelWithSharedStrings(
            new[] { "Initial" },
            new[] { new[] { 0 } });

        using var exportStream = new MemoryStream(xlsx);
        var exportResult = await service.ExportAsync(exportStream, new[] { "Sheet1", "Changed" });

        Assert.Empty(exportResult.Errors);
        Assert.NotNull(exportResult.Content);

        using var doc = SpreadsheetDocument.Open(new MemoryStream(exportResult.Content!), false);
        var sst = doc.WorkbookPart!.SharedStringTablePart!.SharedStringTable!;
        Assert.Null(sst.Count);
        Assert.Null(sst.UniqueCount);
    }

    /// <summary>
    /// Verifies E12: Identity export returns exact source bytes.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task E12_IdentityExport_ReturnsExactSourceBytes()
    {
        var service = CreateService();
        var xlsx = OfficeFixtureFactory.CreateExcelWithInlineStrings(new[] { new[] { "KeepMe" } });

        using var importStream = new MemoryStream(xlsx);
        var importResult = await service.ImportAsync(importStream);

        using var exportStream = new MemoryStream(xlsx);
        var exportResult = await service.ExportAsync(exportStream, importResult.Texts);

        Assert.Empty(exportResult.Errors);
        Assert.NotNull(exportResult.Content);
        Assert.Equal(xlsx, exportResult.Content);
    }

    /// <summary>
    /// Verifies E15: Translation count mismatch is rejected.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task E15_TranslationCountMismatch_ReturnsError()
    {
        var service = CreateService();
        var xlsx = OfficeFixtureFactory.CreateExcelWithInlineStrings(new[] { new[] { "One", "Two" } });

        using var exportStream = new MemoryStream(xlsx);
        var exportResult = await service.ExportAsync(exportStream, new[] { "Only one" });

        Assert.Null(exportResult.Content);
        Assert.Contains(exportResult.Errors, e => e.Code == "translation_count_mismatch");
    }
}
