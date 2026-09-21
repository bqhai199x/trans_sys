using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Excel;
using FileHandler.Api.Modules.Office;
using FileHandler.Tests.Modules.Office;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace FileHandler.Tests.Modules.Excel;

/// <summary>
/// Verifies normalized names, simultaneous renames and exact reference preservation.
/// </summary>
public sealed class ExcelRenameTests
{

    /// <summary>
    /// Rewrites all supported formula owners without altering shared or array topology.
    /// </summary>
    /// <returns>Task completing after source schema and output formula assertions.</returns>
    [Fact]
    public async Task Rename_UpdatesTableValidationConditionalAndSharedArrayFormulas()
    {
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreateExcelWithTable("Items", "A1:B2", ["Name", "Value"], [["Item", "1"]]));
        using (var document = SpreadsheetDocument.Open(buffer, true))
        {
            var part = document.WorkbookPart!.WorksheetParts.Single();
            var data = part.Worksheet!.GetFirstChild<S.SheetData>()!;
            var row = data.Elements<S.Row>().Last();
            row.Append(new S.Cell(new S.CellFormula("Sheet1!$B$2") { FormulaType = S.CellFormulaValues.Shared, SharedIndex = 4, Reference = "C2:C3" }) { CellReference = "C2" });
            row.Append(new S.Cell(new S.CellFormula("Sheet1!$B$2") { FormulaType = S.CellFormulaValues.Array, Reference = "D2:D3" }) { CellReference = "D2" });
            data.Append(new S.Row(new S.Cell(new S.CellFormula { FormulaType = S.CellFormulaValues.Shared, SharedIndex = 4 }) { CellReference = "C3" }) { RowIndex = 3 });
            var table = part.TableDefinitionParts.Single().Table!;
            table.TableColumns!.Elements<S.TableColumn>().Last().Append(new S.CalculatedColumnFormula("Sheet1!$B$2"), new S.TotalsRowFormula("SUM(Sheet1!B2)"));
            part.Worksheet.InsertBefore(new S.ConditionalFormatting(new S.ConditionalFormattingRule(new S.Formula("Sheet1!B2>0"))
            { Type = S.ConditionalFormatValues.Expression, Priority = 1 }) { SequenceOfReferences = new DocumentFormat.OpenXml.ListValue<DocumentFormat.OpenXml.StringValue> { InnerText = "A2" } }, part.Worksheet.GetFirstChild<S.TableParts>());
            part.Worksheet.InsertBefore(new S.DataValidations(new S.DataValidation(new S.Formula1("Sheet1!$B$2"), new S.Formula2("Sheet1!$B$2+1"))
            { Type = S.DataValidationValues.Whole, SequenceOfReferences = new DocumentFormat.OpenXml.ListValue<DocumentFormat.OpenXml.StringValue> { InnerText = "B2" } }) { Count = 1 }, part.Worksheet.GetFirstChild<S.TableParts>());
        }
        var bytes = buffer.ToArray();
        using (var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false))
            Assert.Empty(new DocumentFormat.OpenXml.Validation.OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2019).Validate(document));
        var service = ExcelService.Create();
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Empty(imported.Errors);
        Assert.Equal(new[] { "Sheet1", "Item" }, imported.Texts);
        var output = await service.ExportAsync(new MemoryStream(bytes), ["New Name", "Translated"]);
        Assert.Empty(output.Errors);
        using var result = SpreadsheetDocument.Open(new MemoryStream(output.Content!), false);
        var worksheet = result.WorkbookPart!.WorksheetParts.Single();
        var formulas = worksheet.Worksheet!.Descendants<S.CellFormula>().ToArray();
        Assert.Equal(new[] { "'New Name'!$B$2", "'New Name'!$B$2", "" }, formulas.Select(f => f.Text));
        Assert.Equal(S.CellFormulaValues.Shared, formulas[0].FormulaType!.Value);
        Assert.Equal(4U, formulas[0].SharedIndex!.Value);
        Assert.Equal("C2:C3", formulas[0].Reference!.Value);
        Assert.Equal(S.CellFormulaValues.Array, formulas[1].FormulaType!.Value);
        Assert.Equal("D2:D3", formulas[1].Reference!.Value);
        Assert.Equal(4U, formulas[2].SharedIndex!.Value);
        Assert.Equal("'New Name'!B2>0", worksheet.Worksheet.Descendants<S.Formula>().Single().Text);
        Assert.Equal("'New Name'!$B$2", worksheet.Worksheet.Descendants<S.Formula1>().Single().Text);
        Assert.Equal("'New Name'!$B$2+1", worksheet.Worksheet.Descendants<S.Formula2>().Single().Text);
        Assert.Equal("'New Name'!$B$2", worksheet.TableDefinitionParts.Single().Table!.Descendants<S.CalculatedColumnFormula>().Single().Text);
        Assert.Equal("SUM('New Name'!B2)", worksheet.TableDefinitionParts.Single().Table!.Descendants<S.TotalsRowFormula>().Single().Text);
    }

    /// <summary>
    /// Checks a planned sheet name edit does not authorize adjacent attribute changes.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void RenameMask_RejectsUnplannedAttributeMutation()
    {
        var bytes = OfficeFixtureFactory.CreateExcelWithInlineStrings([["Text"]]);
        using var source = new OfficeSource(bytes, "source", OfficeFormat.Excel, new());
        using var buffer = new MemoryStream();
        buffer.Write(bytes);
        string uri;
        string path;
        using (var document = SpreadsheetDocument.Open(buffer, true))
        {
            uri = document.WorkbookPart!.Uri.ToString();
            var sheet = document.WorkbookPart.Workbook!.Sheets!.Elements<S.Sheet>().Single();
            path = OfficeTextBindings.Key(OfficeTextBindings.Path(sheet));
            sheet.Name = "Changed";
            sheet.State = S.SheetStateValues.Hidden;
        }
        var mask = new OfficeEditMask(uri, [], ScalarEdits: new Dictionary<string, OfficeScalarEdit>())
        {
            AttributeEdits = [new(path, "", "name", "Sheet1", "Changed")]
        };
        var output = buffer.ToArray();
        var result = new OfficePackageValidator(new()).ValidateOutput(source, new(output, "output", output.Length, "test"),
            new Dictionary<string, OfficeEditMask> { [uri] = mask }, default);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "office_output_invalid");
    }

    /// <summary>
    /// Checks Excel name constraints while retaining Unicode.
    /// </summary>
    /// <param name="requested">Raw translated name.</param>
    /// <param name="expected">Expected normalized name.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData("  Sales  ", "Sales")]
    [InlineData("A:B/C\\D?E*F[G]", "A_B_C_D_E_F_G_")]
    [InlineData("'Quoted'", "Quoted")]
    [InlineData("'''", "Sheet")]
    [InlineData("History", "History_")]
    [InlineData("hIsToRy", "hIsToRy_")]
    [InlineData("日本語 😀", "日本語 😀")]
    [InlineData("A\tB", "A_B")]
    [InlineData("' 'Sales' '", "Sales")]
    public void Normalize_ProducesValidBaseName(string requested, string expected) => Assert.Equal(expected, ExcelRenamePlanner.Normalize(requested));

    /// <summary>
    /// Checks truncation never splits surrogate pairs.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Normalize_PreservesSurrogateBoundaries() =>
        Assert.Equal(new string('a', 30), ExcelRenamePlanner.Normalize(new string('a', 30) + "😀tail"));

    /// <summary>
    /// Checks truncation cannot expose an invalid trailing apostrophe.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Normalize_RemovesApostropheExposedByTruncation() =>
        Assert.Equal(new string('a', 30), ExcelRenamePlanner.Normalize(new string('a', 30) + "'tail"));

    /// <summary>
    /// Checks qualifier parsing leaves string literals untouched and rejects uncertain references.
    /// </summary>
    /// <param name="formula">Original formula.</param>
    /// <param name="expected">Expected rewritten formula, or null when unsafe.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData("First!$A$1+\"First!A1\"", "'O''Brien'!$A$1+\"First!A1\"")]
    [InlineData("'First'!A1", "'O''Brien'!A1")]
    [InlineData("SUM(First!A1:B4)", "SUM('O''Brien'!A1:B4)")]
    [InlineData("INDIRECT(\"First!A1\")", null)]
    [InlineData("First:Hidden!A1", null)]
    [InlineData("First:'Hidden'!A1", null)]
    [InlineData("'[book.xlsx]First'!A1", null)]
    [InlineData("[book.xlsx]First!A1", null)]
    public void References_RewriteOnlyRecognizedQualifiers(string formula, string? expected)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["First"] = "O'Brien", ["Hidden"] = "Hidden" };
        var safe = ExcelFormulaReferences.TryRewrite(formula, names, out var actual);
        Assert.Equal(expected is not null, safe);
        if (expected is not null) Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Checks unselected-sheet formulas, defined names and hyperlinks follow a selected rename.
    /// </summary>
    /// <returns>Task completing after independent XML assertions.</returns>
    [Fact]
    public async Task Rename_UpdatesReferencesInUnselectedSheetAndPreservesLiterals()
    {
        var bytes = WithFormula("First!$A$1&\"First!A1\"");
        var service = ExcelService.Create();
        var exported = await service.ExportAsync(new MemoryStream(bytes), ["O'Brien", "Changed"], new ExcelSelection(["7"]), default);
        Assert.Empty(exported.Errors);
        var change = Assert.Single(exported.Metadata.SheetNameChanges!);
        Assert.Equal(new SheetNameChange("7", "First", "O'Brien", "O'Brien"), change);
        using var document = SpreadsheetDocument.Open(new MemoryStream(exported.Content!), false);
        Assert.Equal("O'Brien", document.WorkbookPart!.Workbook!.Sheets!.Elements<S.Sheet>().First().Name!.Value);
        var hidden = HiddenSheet(document);
        Assert.Equal("'O''Brien'!$A$1&\"First!A1\"", hidden.Worksheet!.Descendants<S.CellFormula>().Single().Text);
        Assert.Equal("'O''Brien'!$A$1", document.WorkbookPart.Workbook.DefinedNames!.Elements<S.DefinedName>().Single().Text);
        Assert.Equal("'O''Brien'!A1", hidden.Worksheet.Descendants<S.Hyperlink>().Single().Location!.Value);
        Assert.Equal("Two", hidden.Worksheet.Descendants<S.Text>().Single().Text);
    }

    /// <summary>
    /// Checks swapping sheet names uses original qualifiers simultaneously.
    /// </summary>
    /// <returns>Task completing after swapped-name and reference assertions.</returns>
    [Fact]
    public async Task Rename_SwapsNamesWithoutIntermediateCollisions()
    {
        var bytes = WithFormula("First!A1+Hidden!A1");
        var exported = await ExcelService.Create().ExportAsync(new MemoryStream(bytes), ["Hidden", "One", "First", "Two"], new ExcelSelection(["21", "7"]), default);
        Assert.Empty(exported.Errors);
        using var document = SpreadsheetDocument.Open(new MemoryStream(exported.Content!), false);
        Assert.Equal(new[] { "Hidden", "First", "Secret", "Empty", "Chart" }, document.WorkbookPart!.Workbook!.Sheets!.Elements<S.Sheet>().Select(s => s.Name!.Value));
        Assert.Equal("'Hidden'!A1+'First'!A1", HiddenSheet(document).Worksheet!.Descendants<S.CellFormula>().Single().Text);
    }

    /// <summary>
    /// Checks unsafe references skip renames while content still changes.
    /// </summary>
    /// <param name="formula">Unsupported reference syntax.</param>
    /// <returns>Task completing after rename fallback and content assertions.</returns>
    [Theory]
    [InlineData("INDIRECT(\"First!A1\")")]
    [InlineData("First:Hidden!A1")]
    [InlineData("'[book.xlsx]First'!A1")]
    public async Task Rename_UnsafeReferencePreservesNameAndTranslatesCells(string formula)
    {
        var bytes = WithFormula(formula);
        var output = await ExcelService.Create().ExportAsync(new MemoryStream(bytes), ["New", "Changed"], new ExcelSelection(["7"]), default);
        Assert.Empty(output.Errors);
        Assert.Equal("partial", output.Metadata.Status);
        Assert.Contains(output.Metadata.Skipped, s => s.Code == "unsafe_sheet_reference" && s.UnitIndex == 0);
        Assert.Equal("First", Assert.Single(output.Metadata.SheetNameChanges!).FinalName);
        using var document = SpreadsheetDocument.Open(new MemoryStream(output.Content!), false);
        var first = document.WorkbookPart!.Workbook!.Sheets!.Elements<S.Sheet>().First();
        Assert.Equal("First", first.Name!.Value);
        Assert.Equal("Changed", ((WorksheetPart)document.WorkbookPart.GetPartById(first.Id!)).Worksheet!.Descendants<S.Text>().Single().Text);
        Assert.Equal(formula, HiddenSheet(document).Worksheet!.Descendants<S.CellFormula>().Single().Text);
    }

    /// <summary>
    /// Keeps 3D range names while resolving independent rename collisions against retained names.
    /// </summary>
    /// <param name="formula">Static 3D formula.</param>
    /// <returns>Task completing after independent rename and formula assertions.</returns>
    [Theory]
    [InlineData("SUM(First:Hidden!A1)")]
    [InlineData("SUM('First:Hidden'!A1)")]
    [InlineData("SUM(First:'Hidden'!A1)")]
    public async Task Rename_StaticUnsafeRangeStillAllowsIndependentSheet(string formula)
    {
        var bytes = WithFormula(formula);
        var service = ExcelService.Create();
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Equal(new[] { "First", "One", "Empty" }, imported.Texts);
        var output = await service.ExportAsync(new MemoryStream(bytes), ["Changed", "Translated", "First"]);
        Assert.Empty(output.Errors);
        Assert.Contains(output.Metadata.Skipped, s => s.Code == "unsafe_sheet_reference" && s.UnitIndex == 0);
        Assert.DoesNotContain(output.Metadata.Skipped, s => s.Code == "unsafe_sheet_reference" && s.UnitIndex == 2);
        using var document = SpreadsheetDocument.Open(new MemoryStream(output.Content!), false);
        Assert.Equal(new[] { "First", "Hidden", "Secret", "First (2)", "Chart" }, document.WorkbookPart!.Workbook!.Sheets!.Elements<S.Sheet>().Select(s => s.Name!.Value));
        Assert.Equal(formula, HiddenSheet(document).Worksheet!.Descendants<S.CellFormula>().Single().Text);
    }

    /// <summary>
    /// Checks collisions reserve unselected names and empty translated names retain source.
    /// </summary>
    /// <param name="requested">Raw requested name.</param>
    /// <param name="expected">Actual output name.</param>
    /// <returns>Task completing after name and metadata assertions.</returns>
    [Theory]
    [InlineData("hidden", "hidden (2)")]
    [InlineData("'''", "Sheet")]
    [InlineData("", "First")]
    [InlineData("  ", "First")]
    public async Task Rename_ResolvesCollisionsAndEmptyNames(string requested, string expected)
    {
        var bytes = OfficeFixtureFactory.CreateSelectionWorkbook();
        var output = await ExcelService.Create().ExportAsync(new MemoryStream(bytes), [requested, "One"], new ExcelSelection(["7"]), default);
        Assert.Empty(output.Errors);
        Assert.Equal(expected, Assert.Single(output.Metadata.SheetNameChanges!).FinalName);
        using var document = SpreadsheetDocument.Open(new MemoryStream(output.Content!), false);
        Assert.Equal(expected, document.WorkbookPart!.Workbook!.Sheets!.Elements<S.Sheet>().First().Name!.Value);
        if (string.IsNullOrWhiteSpace(requested)) Assert.Equal(bytes, output.Content);
    }

    /// <summary>
    /// Creates references in an unselected hidden worksheet.
    /// </summary>
    /// <param name="formula">Formula placed in hidden worksheet.</param>
    /// <returns>Source workbook with formula, defined name and hyperlink.</returns>
    private static byte[] WithFormula(string formula)
    {
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreateSelectionWorkbook());
        using (var document = SpreadsheetDocument.Open(buffer, true))
        {
            var hidden = HiddenSheet(document);
            hidden.Worksheet!.Descendants<S.Row>().Single().Append(new S.Cell(new S.CellFormula(formula)) { CellReference = "B1" });
            hidden.Worksheet.Append(new S.Hyperlinks(new S.Hyperlink { Reference = "A1", Location = "First!A1" }));
            document.WorkbookPart!.Workbook!.Append(new S.DefinedNames(new S.DefinedName("First!$A$1") { Name = "SourceCell" }));
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// Resolves hidden source sheet by native ID independently of renamed display name.
    /// </summary>
    /// <param name="document">Workbook to inspect.</param>
    /// <returns>Worksheet with native ID 21.</returns>
    private static WorksheetPart HiddenSheet(SpreadsheetDocument document)
    {
        var sheet = document.WorkbookPart!.Workbook!.Sheets!.Elements<S.Sheet>().Single(s => s.SheetId!.Value == 21);
        return (WorksheetPart)document.WorkbookPart.GetPartById(sheet.Id!);
    }
}
