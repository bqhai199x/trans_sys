using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Excel;
using FileHandler.Api.Modules.PowerPoint;
using FileHandler.Api.Modules.Word;
using FileHandler.Api.Modules.Office;
using Microsoft.Extensions.Options;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace FileHandler.Tests.Modules.Office;

/// <summary>
/// Regressions for review findings involving data loss and quota asymmetry.
/// </summary>
public sealed class OfficeReviewRegressionTests
{

    /// <summary>
    /// Verifies partial bindings preserve raw control characters within one scalar.
    /// </summary>
    /// <returns>Task completing after assertions.</returns>
    [Fact]
    public async Task Word_RawControls_ProtectsUnboundSpans()
    {
        var bytes = OfficeFixtureFactory.CreateWordDocument("A\tB\nC");
        var service = WordService.Create();
        var import = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Empty(import.Errors);
        Assert.Equal("<ox:r0>A</ox:r0><ox:k0/><ox:r1>B</ox:r1><ox:k1/><ox:r2>C</ox:r2>", Assert.Single(import.Texts));
        var output = await Export(service, bytes, ["<ox:r0>X</ox:r0><ox:k0/><ox:r1>Y</ox:r1><ox:k1/><ox:r2>Z</ox:r2>"]);
        using var doc = WordprocessingDocument.Open(new MemoryStream(output), false);
        Assert.Equal("X\tY\nZ", doc.MainDocumentPart!.Document!.Body!.InnerText);
    }

    /// <summary>
    /// Verifies unreferenced header parts do not become translation units.
    /// </summary>
    /// <returns>Task completing after assertions.</returns>
    [Fact]
    public async Task Word_OrphanHeader_IsExcluded()
    {
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreateWordDocument("Body"));
        using (var doc = WordprocessingDocument.Open(buffer, true))
            doc.MainDocumentPart!.AddNewPart<HeaderPart>().Header = new W.Header(OfficeFixtureFactory.WordParagraph("Orphan"));
        var import = await WordService.Create().ImportAsync(new MemoryStream(buffer.ToArray()));
        Assert.Empty(import.Errors);
        Assert.Equal(new[] { "Body" }, import.Texts);
    }

    /// <summary>
    /// Verifies merged follower text is preserved while owner cell translates.
    /// </summary>
    /// <returns>Task completing after assertions.</returns>
    [Fact]
    public async Task Excel_MergedFollowerText_IsSkipped()
    {
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreateExcelWithInlineStrings([["Owner", "Follower"]]));
        using (var doc = SpreadsheetDocument.Open(buffer, true))
            doc.WorkbookPart!.WorksheetParts.Single().Worksheet!.Append(new S.MergeCells(new S.MergeCell { Reference = "A1:B1" }));
        var import = await ExcelService.Create().ImportAsync(new MemoryStream(buffer.ToArray()));
        Assert.Empty(import.Errors);
        Assert.Equal(new[] { "Sheet1", "Owner" }, import.Texts);
        Assert.Contains(import.Metadata.Skipped, e => e.Code == "merged_follower_text" && e.Location.CellReference == "B1");
        var exported = await ExcelService.Create().ExportAsync(new MemoryStream(buffer.ToArray()), ["Sheet1", "Changed"]);
        Assert.Empty(exported.Errors);
        using var output = SpreadsheetDocument.Open(new MemoryStream(exported.Content!), false);
        Assert.Equal(new[] { "Changed", "Follower" }, output.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<S.Text>().Select(t => t.Text));
    }

    /// <summary>
    /// Verifies a valid-schema text edit outside prepared bindings is rejected.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Word_EditMask_RejectsUnboundTextMutation()
    {
        var bytes = OfficeFixtureFactory.CreateWordDocument("A", "B");
        using var source = new OfficeSource(bytes, "source", OfficeFormat.Word, new());
        using var buffer = new MemoryStream();
        buffer.Write(bytes);
        string uri;
        string key;
        using (var doc = WordprocessingDocument.Open(buffer, true))
        {
            uri = doc.MainDocumentPart!.Uri.ToString();
            var texts = doc.MainDocumentPart.Document!.Descendants<W.Text>().ToArray();
            key = OfficeTextBindings.Key(OfficeTextBindings.Path(texts[0]));
            texts[0].Text = "X";
            texts[1].Text = "Unauthorized";
        }
        var edits = new Dictionary<string, OfficeScalarEdit>
        {
            [key] = new(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("A"))), "X")
        };
        var masks = new Dictionary<string, OfficeEditMask> { [uri] = new(uri, ["//w:t"], ScalarEdits: edits) };
        var changed = buffer.ToArray();
        var result = new OfficePackageValidator(new()).ValidateOutput(source, new(changed, "output", changed.Length, "test"), masks, default);
        Assert.False(result.IsValid);
    }

    /// <summary>
    /// Verifies coalesced Word runs are cleared only within their own slot.
    /// </summary>
    /// <returns>Task completing after assertions.</returns>
    [Fact]
    public async Task Word_GroupedRuns_ApplyAllBindings()
    {
        var bytes = OfficeFixtureFactory.CreateWordDocumentWithElements(new W.Paragraph(
            new W.Run(new W.Text("A")), new W.Run(new W.Text("B")),
            new W.Run(new W.RunProperties(new W.Bold()), new W.Text("C"))));
        var output = await Export(WordService.Create(), bytes, ["<ox:r0>X</ox:r0><ox:r1>Y</ox:r1>"]);
        using var doc = WordprocessingDocument.Open(new MemoryStream(output), false);
        Assert.Equal(new[] { "X", "", "Y" }, doc.MainDocumentPart!.Document!.Descendants<W.Text>().Select(t => t.Text));
        Assert.Single(doc.MainDocumentPart.Document.Descendants<W.Bold>());
    }

    /// <summary>
    /// Verifies simple field cached text remains protected before editable text.
    /// </summary>
    /// <returns>Task completing after assertions.</returns>
    [Fact]
    public async Task Word_LeadingField_RetainsOrderAndValue()
    {
        var bytes = OfficeFixtureFactory.CreateWordDocumentWithElements(new W.Paragraph(
            new W.SimpleField(new W.Run(new W.Text("FIELD"))) { Instruction = "DATE" },
            new W.Run(new W.Text("Body"))));
        var service = WordService.Create();
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Empty(imported.Errors);
        Assert.Equal("<ox:k0/><ox:r0>Body</ox:r0>", Assert.Single(imported.Texts));
        var output = await Export(service, bytes, ["<ox:k0/><ox:r0>Changed</ox:r0>"]);
        using var doc = WordprocessingDocument.Open(new MemoryStream(output), false);
        Assert.Equal("FIELDChanged", doc.MainDocumentPart!.Document!.Body!.InnerText);
        var reordered = await service.ExportAsync(new MemoryStream(bytes), ["<ox:r0>Changed</ox:r0><ox:k0/>"]);
        Assert.Equal(bytes, reordered.Content);
        Assert.Empty(reordered.Errors);
        Assert.Contains(reordered.Metadata.Skipped, e => e.Code == "office_token_mismatch");
    }

    /// <summary>
    /// Verifies a standalone styled space is protected and identity remains exact.
    /// </summary>
    /// <returns>Task completing after assertions.</returns>
    [Fact]
    public async Task Word_StyledWhitespace_IdentityRoundTrips()
    {
        var bytes = OfficeFixtureFactory.CreateWordDocumentWithElements(new W.Paragraph(
            new W.Run(new W.Text("A")), new W.Run(new W.RunProperties(new W.Bold()),
                new W.Text(" ") { Space = SpaceProcessingModeValues.Preserve }), new W.Run(new W.Text("B"))));
        var service = WordService.Create();
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Empty(imported.Errors);
        Assert.Equal(bytes, await Export(service, bytes, imported.Texts));
    }

    /// <summary>
    /// Verifies changing body leaves header and footer parts untouched.
    /// </summary>
    /// <returns>Task completing after assertions.</returns>
    [Fact]
    public async Task Word_PartialStoryChange_Succeeds()
    {
        var bytes = OfficeFixtureFactory.CreateWordWithStories("Body", "Header", "Footer");
        var output = await Export(WordService.Create(), bytes, ["Changed", "Header", "Footer"]);
        using var doc = WordprocessingDocument.Open(new MemoryStream(output), false);
        Assert.Equal("Changed", doc.MainDocumentPart!.Document!.Body!.InnerText);
        Assert.Equal("Header", doc.MainDocumentPart.HeaderParts.Single().Header!.InnerText);
    }

    /// <summary>
    /// Verifies each paragraph within one shape has an independent physical target.
    /// </summary>
    /// <returns>Task completing after assertions.</returns>
    [Fact]
    public async Task PowerPoint_MultipleParagraphs_StayIndependent()
    {
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreatePowerPointPresentation("First"));
        using (var doc = PresentationDocument.Open(buffer, true))
            doc.PresentationPart!.SlideParts.Single().Slide!.Descendants<P.Shape>().Single().TextBody!
                .Append(new A.Paragraph(new A.Run(new A.Text("Second"))));
        var output = await Export(PowerPointService.Create(), buffer.ToArray(), ["One", "Two"]);
        using var result = PresentationDocument.Open(new MemoryStream(output), false);
        Assert.Equal(new[] { "One", "Two" }, result.PresentationPart!.SlideParts.Single().Slide!.Descendants<A.Paragraph>().Select(p => p.InnerText));
    }

    /// <summary>
    /// Verifies later rich slots change without dropping runs or modifying shared originals.
    /// </summary>
    /// <param name="shared">True for shared string storage; false for inline storage.</param>
    /// <returns>Task completing after assertions.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Excel_RichSecondSlot_PreservesRuns(bool shared)
    {
        using var buffer = new MemoryStream();
        buffer.Write(shared ? OfficeFixtureFactory.CreateExcelWithSharedStrings(["AB"], [[0, 0]]) :
            OfficeFixtureFactory.CreateExcelWithInlineStrings([["AB"]]));
        using (var doc = SpreadsheetDocument.Open(buffer, true))
        {
            OpenXmlElement payload = shared ? doc.WorkbookPart!.SharedStringTablePart!.SharedStringTable!.Elements<S.SharedStringItem>().Single() :
                doc.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<S.Cell>().Single().InlineString!;
            payload.RemoveAllChildren();
            payload.Append(new S.Run(new S.Text("A")), new S.Run(new S.RunProperties(new S.Bold()), new S.Text("B")));
        }
        var bytes = buffer.ToArray();
        var service = ExcelService.Create();
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Empty(imported.Errors);
        var translations = imported.Texts.ToArray();
        translations[1] = translations[1].Replace(">B<", ">Changed<", StringComparison.Ordinal);
        var output = await Export(service, bytes, translations);
        using var result = SpreadsheetDocument.Open(new MemoryStream(output), false);
        var cell = result.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<S.Cell>().First();
        var strings = result.WorkbookPart.SharedStringTablePart?.SharedStringTable?.Elements<S.SharedStringItem>().ToArray();
        OpenXmlElement translated = shared ? strings![int.Parse(cell.CellValue!.Text)] : cell.InlineString!;
        Assert.Equal("AChanged", translated.InnerText);
        Assert.Equal(2, translated.Elements<S.Run>().Count());
        Assert.Single(translated.Descendants<S.Bold>());
        if (shared) Assert.Equal("AB", strings![0].InnerText);
    }

    /// <summary>
    /// Verifies export applies same unit quota as import before identity return.
    /// </summary>
    /// <returns>Task completing after assertions.</returns>
    [Fact]
    public async Task Word_IdentityExport_EnforcesUnitLimit()
    {
        var service = WordService.Create(Options.Create(new FileHandlingOptions { MaxUnits = 1 }));
        var result = await service.ExportAsync(new MemoryStream(OfficeFixtureFactory.CreateWordDocument("A", "B")), ["A", "B"]);
        Assert.Contains(result.Errors, e => e.Code == "too_many_units");
        Assert.Null(result.Content);
    }

    /// <summary>
    /// Exports and asserts successful atomic publication.
    /// </summary>
    /// <param name="service">Format handler.</param>
    /// <param name="source">Source bytes.</param>
    /// <param name="translations">Caller translations.</param>
    /// <returns>Validated output bytes.</returns>
    private static async Task<byte[]> Export(IFileHandler service, byte[] source, IReadOnlyList<string> translations)
    {
        var result = await service.ExportAsync(new MemoryStream(source), translations);
        Assert.Empty(result.Errors);
        return Assert.IsType<byte[]>(result.Content);
    }
}
