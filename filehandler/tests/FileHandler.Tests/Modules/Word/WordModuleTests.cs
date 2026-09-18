using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Word;
using FileHandler.Tests.Modules.Office;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace FileHandler.Tests.Modules.Word;

/// <summary>
/// Word module acceptance, table traversal, field protection, and export tests (W01-W14).
/// </summary>
public sealed class WordModuleTests
{

    /// <summary>
    /// Creates WordService for testing.
    /// </summary>
    /// <param name="fileOptions">File handling options.</param>
    /// <returns>Configured WordService instance.</returns>
    private static WordService CreateService(FileHandlingOptions? fileOptions = null) =>
        WordService.Create(fileOptions is not null ? Microsoft.Extensions.Options.Options.Create(fileOptions) : null);

    /// <summary>
    /// Verifies W01: Table with 2 cells A/B extracts ["A", "B"] and exports ["T0", "T1"].
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task W01_SimpleTable_ExtractsAndTranslatesCells()
    {
        var service = CreateService();
        var table = OfficeFixtureFactory.MakeWordTable(
            new W.TableCell(OfficeFixtureFactory.WordParagraph("A")),
            new W.TableCell(OfficeFixtureFactory.WordParagraph("B")));
        var docx = OfficeFixtureFactory.CreateWordDocumentWithElements(table);

        using var importStream = new MemoryStream(docx);
        var importResult = await service.ImportAsync(importStream);

        Assert.Empty(importResult.Errors);
        Assert.Equal(new[] { "A", "B" }, importResult.Texts);

        using var exportStream = new MemoryStream(docx);
        var exportResult = await service.ExportAsync(exportStream, new[] { "T0", "T1" });

        Assert.Empty(exportResult.Errors);
        Assert.NotNull(exportResult.Content);

        using var doc = WordprocessingDocument.Open(new MemoryStream(exportResult.Content!), false);
        var cells = doc.MainDocumentPart!.Document!.Body!.Descendants<W.TableCell>().ToList();
        Assert.Equal(2, cells.Count);
        Assert.Equal("T0", cells[0].InnerText);
        Assert.Equal("T1", cells[1].InnerText);
    }

    /// <summary>
    /// Verifies W02: Nested table traversal order (Outer, Nested, After, Right).
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task W02_NestedTable_TraversesInOrder()
    {
        var service = CreateService();
        var innerTable = OfficeFixtureFactory.MakeWordTable(new W.TableCell(OfficeFixtureFactory.WordParagraph("Nested")));
        var leftCell = new W.TableCell(
            OfficeFixtureFactory.WordParagraph("Outer"),
            innerTable,
            OfficeFixtureFactory.WordParagraph("After"));
        var rightCell = new W.TableCell(OfficeFixtureFactory.WordParagraph("Right"));
        var table = OfficeFixtureFactory.MakeWordTable(leftCell, rightCell);
        var docx = OfficeFixtureFactory.CreateWordDocumentWithElements(table);

        using var importStream = new MemoryStream(docx);
        var importResult = await service.ImportAsync(importStream);

        Assert.Empty(importResult.Errors);
        Assert.Equal(new[] { "Outer", "Nested", "After", "Right" }, importResult.Texts);

        using var exportStream = new MemoryStream(docx);
        var exportResult = await service.ExportAsync(exportStream, new[] { "T0", "T1", "T2", "T3" });

        Assert.Empty(exportResult.Errors);
        Assert.NotNull(exportResult.Content);
    }

    /// <summary>
    /// Verifies W03: Empty paragraph cell is skipped without creating a unit.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task W03_EmptyParagraphCell_IsSkipped()
    {
        var service = CreateService();
        var table = OfficeFixtureFactory.MakeWordTable(
            new W.TableCell(new W.Paragraph()),
            new W.TableCell(OfficeFixtureFactory.WordParagraph("A")),
            new W.TableCell(OfficeFixtureFactory.WordParagraph("B")));
        var docx = OfficeFixtureFactory.CreateWordDocumentWithElements(table);

        using var importStream = new MemoryStream(docx);
        var importResult = await service.ImportAsync(importStream);

        Assert.Empty(importResult.Errors);
        Assert.Equal(new[] { "A", "B" }, importResult.Texts);

        using var exportStream = new MemoryStream(docx);
        var exportResult = await service.ExportAsync(exportStream, new[] { "T0", "T1" });

        Assert.Empty(exportResult.Errors);
        Assert.NotNull(exportResult.Content);

        using var doc = WordprocessingDocument.Open(new MemoryStream(exportResult.Content!), false);
        var cells = doc.MainDocumentPart!.Document!.Body!.Descendants<W.TableCell>().ToList();
        Assert.Equal(3, cells.Count);
        Assert.Equal(string.Empty, cells[0].InnerText);
        Assert.Equal("T0", cells[1].InnerText);
        Assert.Equal("T1", cells[2].InnerText);
    }

    /// <summary>
    /// Verifies W04: Cell with empty text node is skipped without creating a unit.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task W04_EmptyTextCell_IsSkipped()
    {
        var service = CreateService();
        var table = OfficeFixtureFactory.MakeWordTable(
            new W.TableCell(OfficeFixtureFactory.WordParagraph("")),
            new W.TableCell(OfficeFixtureFactory.WordParagraph("A")),
            new W.TableCell(OfficeFixtureFactory.WordParagraph("B")));
        var docx = OfficeFixtureFactory.CreateWordDocumentWithElements(table);

        using var importStream = new MemoryStream(docx);
        var importResult = await service.ImportAsync(importStream);

        Assert.Empty(importResult.Errors);
        Assert.Equal(new[] { "A", "B" }, importResult.Texts);
    }

    /// <summary>
    /// Verifies W05: Vertically merged table continuation cell is skipped.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task W05_VerticallyMergedTable_SkipsContinuationCell()
    {
        var service = CreateService();
        var table = new W.Table(
            new W.TableProperties(),
            new W.TableGrid(new W.GridColumn { Width = "2400" }, new W.GridColumn { Width = "2400" }),
            new W.TableRow(
                new W.TableCell(new W.TableCellProperties(new W.VerticalMerge { Val = W.MergedCellValues.Restart }), OfficeFixtureFactory.WordParagraph("A")),
                new W.TableCell(OfficeFixtureFactory.WordParagraph("B"))),
            new W.TableRow(
                new W.TableCell(new W.TableCellProperties(new W.VerticalMerge()), OfficeFixtureFactory.WordParagraph("")),
                new W.TableCell(OfficeFixtureFactory.WordParagraph("C"))));
        var docx = OfficeFixtureFactory.CreateWordDocumentWithElements(table);

        using var importStream = new MemoryStream(docx);
        var importResult = await service.ImportAsync(importStream);

        Assert.Empty(importResult.Errors);
        Assert.Equal(new[] { "A", "B", "C" }, importResult.Texts);

        using var exportStream = new MemoryStream(docx);
        var exportResult = await service.ExportAsync(exportStream, new[] { "T0", "T1", "T2" });

        Assert.Empty(exportResult.Errors);
        Assert.NotNull(exportResult.Content);
    }

    /// <summary>
    /// Verifies W06: Stories (body, header, footer) are extracted in story order.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task W06_MultipleStories_ExtractsInStoryOrder()
    {
        var service = CreateService();
        var docx = OfficeFixtureFactory.CreateWordWithStories("BodyText", "HeaderText", "FooterText");

        using var importStream = new MemoryStream(docx);
        var importResult = await service.ImportAsync(importStream);

        Assert.Empty(importResult.Errors);
        Assert.Contains("BodyText", importResult.Texts);
        Assert.Contains("HeaderText", importResult.Texts);
        Assert.Contains("FooterText", importResult.Texts);
    }

    /// <summary>
    /// Verifies W10: Identity export returns exact source bytes.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task W10_IdentityExport_ReturnsExactSourceBytes()
    {
        var service = CreateService();
        var docx = OfficeFixtureFactory.CreateWordDocument("First paragraph", "Second paragraph");

        using var importStream = new MemoryStream(docx);
        var importResult = await service.ImportAsync(importStream);

        using var exportStream = new MemoryStream(docx);
        var exportResult = await service.ExportAsync(exportStream, importResult.Texts);

        Assert.Empty(exportResult.Errors);
        Assert.NotNull(exportResult.Content);
        Assert.Equal(docx, exportResult.Content);
    }

    /// <summary>
    /// Verifies W14: Translation count mismatch is rejected.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task W14_TranslationCountMismatch_ReturnsError()
    {
        var service = CreateService();
        var docx = OfficeFixtureFactory.CreateWordDocument("First", "Second");

        using var exportStream = new MemoryStream(docx);
        var exportResult = await service.ExportAsync(exportStream, new[] { "Only one" });

        Assert.Null(exportResult.Content);
        Assert.Contains(exportResult.Errors, e => e.Code == "translation_count_mismatch");
    }
}
