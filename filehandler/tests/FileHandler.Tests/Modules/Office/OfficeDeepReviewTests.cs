using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;
using FileHandler.Api.Modules.Word;
using FileHandler.Api.Modules.Excel;
using FileHandler.Api.Modules.PowerPoint;
using Microsoft.Extensions.Options;
using W = DocumentFormat.OpenXml.Wordprocessing;
using S = DocumentFormat.OpenXml.Spreadsheet;
using A = DocumentFormat.OpenXml.Drawing;

namespace FileHandler.Tests.Modules.Office;

/// <summary>
/// Regressions for namespace quotas, merge ownership and bounded extraction.
/// </summary>
public sealed class OfficeDeepReviewTests
{

    /// <summary>
    /// Counts relationships independently of namespace prefix.
    /// </summary>
    /// <param name="prefix">Equivalent namespace prefix spelling.</param>
    /// <returns>Task completing after quota assertions.</returns>
    [Theory]
    [InlineData("")]
    [InlineData("r:")]
    public async Task RelationshipQuota_IsNamespaceAware(string prefix)
    {
        var ns = prefix.Length == 0 ? "xmlns" : "xmlns:r";
        var xml = $"<{prefix}Relationships {ns}=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            string.Concat(Enumerable.Range(0, 3).Select(i => $"<{prefix}Relationship Id=\"h{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink\" Target=\"https://example.invalid/{i}\" TargetMode=\"External\"/>")) +
            $"</{prefix}Relationships>";
        var bytes = AddEntry(OfficeFixtureFactory.CreateWordDocument("Visible"), "word/_rels/document.xml.rels", xml);
        var service = WordService.Create(officeOptions: Options.Create(new OfficeProcessingOptions { MaxRelationships = 2 }));
        var result = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Contains(result.Errors, e => e.Code == "office_package_limit_exceeded");
    }

    /// <summary>
    /// Preserves Word continuation text while translating independent paragraphs.
    /// </summary>
    /// <param name="horizontal">Whether merge uses legacy horizontal representation.</param>
    /// <returns>Task completing after preservation assertions.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Word_MergedFollowerText_IsSkipped(bool horizontal)
    {
        var properties = new W.TableCellProperties();
        if (horizontal) properties.Append(new W.HorizontalMerge());
        else properties.Append(new W.VerticalMerge());
        var cell = new W.TableCell(properties, new W.Paragraph(new W.Run(new W.Text("Hidden"))));
        var bytes = OfficeFixtureFactory.CreateWordDocumentWithElements(OfficeFixtureFactory.MakeWordTable(cell), OfficeFixtureFactory.WordParagraph("Visible"));
        var result = await WordService.Create().ImportAsync(new MemoryStream(bytes));
        Assert.Empty(result.Errors);
        Assert.Equal(new[] { "Visible" }, result.Texts);
        Assert.Contains(result.Metadata.Skipped, e => e.Code == "merged_follower_text");
        var exported = await WordService.Create().ExportAsync(new MemoryStream(bytes), ["Changed"]);
        Assert.Empty(exported.Errors);
        using var output = WordprocessingDocument.Open(new MemoryStream(exported.Content!), false);
        Assert.Equal("HiddenChanged", output.MainDocumentPart!.Document!.Body!.InnerText);
    }

    /// <summary>
    /// Preserves DrawingML continuation text while translating owner cells.
    /// </summary>
    /// <param name="horizontal">Whether continuation is horizontal.</param>
    /// <returns>Task completing after preservation assertions.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PowerPoint_MergedFollowerText_IsSkipped(bool horizontal)
    {
        var cell = OfficeFixtureFactory.DrawingCell(new A.Paragraph(new A.Run(new A.Text("Hidden"))));
        if (horizontal) cell.HorizontalMerge = true;
        else cell.VerticalMerge = true;
        var bytes = OfficeFixtureFactory.CreatePowerPointWithTable(new A.TableRow(cell, OfficeFixtureFactory.DrawingCell(new A.Paragraph(new A.Run(new A.Text("Visible"))))) { Height = 400000 });
        var result = await PowerPointService.Create().ImportAsync(new MemoryStream(bytes));
        Assert.Empty(result.Errors);
        Assert.Equal(new[] { "Visible" }, result.Texts);
        Assert.Contains(result.Metadata.Skipped, e => e.Code == "merged_follower_text");
        var exported = await PowerPointService.Create().ExportAsync(new MemoryStream(bytes), ["Changed"]);
        Assert.Empty(exported.Errors);
        using var output = PresentationDocument.Open(new MemoryStream(exported.Content!), false);
        Assert.Equal(new[] { "Hidden", "Changed" }, output.PresentationPart!.SlideParts.Single().Slide!.Descendants<A.Text>().Select(t => t.Text));
    }

    /// <summary>
    /// Leaves implicit addresses in untouched hidden worksheets intact.
    /// </summary>
    /// <returns>Task completing after exact hidden XML comparison.</returns>
    [Fact]
    public async Task Excel_HiddenImplicitCell_ExportsWithoutTouchingHiddenPart()
    {
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreateExcelWithSharedStrings(["Original"], [[0]]));
        string hiddenUri;
        using (var doc = SpreadsheetDocument.Open(buffer, true))
        {
            var wb = doc.WorkbookPart!;
            var hidden = wb.AddNewPart<WorksheetPart>();
            hiddenUri = hidden.Uri.ToString().TrimStart('/');
            hidden.Worksheet = new S.Worksheet(new S.SheetData(new S.Row(new S.Cell
            {
                DataType = S.CellValues.SharedString,
                CellValue = new("0")
            })
            { RowIndex = 1 }));
            wb.Workbook!.Sheets!.Append(new S.Sheet { Id = wb.GetIdOfPart(hidden), SheetId = 2, Name = "Hidden", State = S.SheetStateValues.VeryHidden });
        }
        var source = buffer.ToArray();
        var result = await ExcelService.Create().ExportAsync(new MemoryStream(source), ["Sheet1", "Changed"]);
        Assert.Empty(result.Errors);
        var output = Assert.IsType<byte[]>(result.Content);
        Assert.Equal(ReadEntry(source, hiddenUri), ReadEntry(output, hiddenUri));
        using var exported = SpreadsheetDocument.Open(new MemoryStream(output), false);
        Assert.Equal("Original", exported.WorkbookPart!.SharedStringTablePart!.SharedStringTable!.Elements<S.SharedStringItem>().First().InnerText);
    }

    /// <summary>
    /// Enforces file unit limit inside extraction without relying on service post-check.
    /// </summary>
    /// <returns>Task completing after extraction budget assertion.</returns>
    [Fact]
    public async Task Word_ExtractorStopsAtFileUnitLimit()
    {
        var options = new OfficeProcessingOptions();
        var read = await new OfficePackageReader(options).ReadAsync(new MemoryStream(OfficeFixtureFactory.CreateWordDocument("A", "B")), OfficeFormat.Word, default);
        using var source = read.Source!;
        var extractor = new WordExtractor(new OfficeTextCodec(options, new FileHandlingOptions { MaxUnits = 1 }), new WordTableReader(), options);
        var error = Assert.Throws<FileLimitException>(() => extractor.Analyze(source, new OfficePackageInspector(options).Inspect(source, default), default));
        Assert.Equal("too_many_units", error.Code);
    }

    /// <summary>
    /// Bounds repeated shared-string binding work even when runs coalesce into one token.
    /// </summary>
    /// <returns>Task completing after binding budget rejection.</returns>
    [Fact]
    public async Task Excel_RepeatedRichString_EnforcesBindingBudget()
    {
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreateExcelWithSharedStrings(["placeholder"], [[0], [0]]));
        using (var doc = SpreadsheetDocument.Open(buffer, true))
        {
            var item = doc.WorkbookPart!.SharedStringTablePart!.SharedStringTable!.Elements<S.SharedStringItem>().Single();
            item.RemoveAllChildren();
            for (var i = 0; i < 10; i++) item.Append(new S.Run(new S.Text("a")));
        }
        var service = ExcelService.Create(officeOptions: Options.Create(new OfficeProcessingOptions { MaxBindings = 15 }));
        var result = await service.ImportAsync(new MemoryStream(buffer.ToArray()));
        Assert.Contains(result.Errors, e => e.Code == "office_plan_limit_exceeded");
    }

    /// <summary>
    /// Rejects unsafe entry paths without extracting files.
    /// </summary>
    /// <param name="path">Unsafe ZIP entry path.</param>
    /// <returns>Task completing after package rejection.</returns>
    [Theory]
    [InlineData("../evil.xml")]
    [InlineData("C:/evil.xml")]
    [InlineData("%2e%2e/evil.xml")]
    [InlineData("word/%2fescape.xml")]
    public async Task Reader_RejectsUnsafeEntryPath(string path)
    {
        var bytes = AddEntry(OfficeFixtureFactory.CreateWordDocument("A"), path, "<safe/>");
        var read = await new OfficePackageReader(new()).ReadAsync(new MemoryStream(bytes), OfficeFormat.Word, default);
        Assert.Contains(read.Errors, e => e.Code == "invalid_office_package");
    }

    /// <summary>
    /// Bounds attribute-heavy XML before SDK DOM loading.
    /// </summary>
    /// <returns>Task completing after attribute budget rejection.</returns>
    [Fact]
    public async Task Reader_RejectsExcessiveAttributes()
    {
        var xml = "<root " + string.Join(" ", Enumerable.Range(0, 20).Select(i => $"a{i}=\"v\"")) + "/>";
        var bytes = AddEntry(OfficeFixtureFactory.CreateWordDocument("A"), "extra.xml", xml);
        var read = await new OfficePackageReader(new() { MaxAttributesPerElement = 10 }).ReadAsync(new MemoryStream(bytes), OfficeFormat.Word, default);
        Assert.Contains(read.Errors, e => e.Code == "office_package_limit_exceeded");
    }

    /// <summary>
    /// Resolves disjoint protected intervals without treating data rows as headers.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void TableIndex_ProtectsOnlyHeaderAndTotals()
    {
        var index = new ExcelProtectedCellIndex([new("First", "/sheet.xml", "A1:C10", 1, 1, []), new("Second", "/sheet.xml", "E1:F10", 1, 0, [])]);
        Assert.True(index.Contains(2, 1));
        Assert.False(index.Contains(4, 1));
        Assert.True(index.Contains(5, 1));
        Assert.True(index.Contains(2, 10));
        Assert.False(index.Contains(5, 10));
        Assert.False(index.Contains(2, 5));
    }

    /// <summary>
    /// Adds synthetic XML to an existing fixture.
    /// </summary>
    /// <param name="bytes">Original package.</param>
    /// <param name="path">New ZIP path.</param>
    /// <param name="xml">Synthetic payload.</param>
    /// <returns>Updated fixture bytes.</returns>
    private static byte[] AddEntry(byte[] bytes, string path, string xml)
    {
        using var buffer = new MemoryStream();
        buffer.Write(bytes);
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, true))
        using (var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false))) writer.Write(xml);
        return buffer.ToArray();
    }

    /// <summary>
    /// Reads one package part for preservation checks.
    /// </summary>
    /// <param name="bytes">Package payload.</param>
    /// <param name="path">ZIP path.</param>
    /// <returns>Original XML string.</returns>
    private static string ReadEntry(byte[] bytes, string path)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry(path)!.Open());
        return reader.ReadToEnd();
    }
}
