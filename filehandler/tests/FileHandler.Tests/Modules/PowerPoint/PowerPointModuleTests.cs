using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.PowerPoint;
using FileHandler.Tests.Modules.Office;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace FileHandler.Tests.Modules.PowerPoint;

/// <summary>
/// PowerPoint module acceptance, shape extraction, table reading, and export tests (P01-P10).
/// </summary>
public sealed class PowerPointModuleTests
{

    /// <summary>
    /// Creates PowerPointService for testing.
    /// </summary>
    /// <param name="fileOptions">File handling options.</param>
    /// <returns>Configured PowerPointService instance.</returns>
    private static PowerPointService CreateService(FileHandlingOptions? fileOptions = null) =>
        PowerPointService.Create(fileOptions is not null ? Microsoft.Extensions.Options.Options.Create(fileOptions) : null);

    /// <summary>
    /// Verifies P01: Table with empty cell and break inside cell paragraph extracts tokens and translates.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task P01_TableWithBreakAndEmptyCell_ExtractsTokens()
    {
        var service = CreateService();
        var row1 = new A.TableRow(
            OfficeFixtureFactory.DrawingCell(new A.Paragraph(new A.Run(new A.Text("A")))),
            OfficeFixtureFactory.DrawingCell(new A.Paragraph()));
        var row2 = new A.TableRow(
            OfficeFixtureFactory.DrawingCell(new A.Paragraph(
                new A.Run(new A.Text("B")),
                new A.Break(),
                new A.Run(new A.Text("C")))),
            OfficeFixtureFactory.DrawingCell(new A.Paragraph(new A.Run(new A.Text("D")))));

        var pptx = OfficeFixtureFactory.CreatePowerPointWithTable(row1, row2);

        using var importStream = new MemoryStream(pptx);
        var importResult = await service.ImportAsync(importStream);

        Assert.Empty(importResult.Errors);
        Assert.Equal(3, importResult.Texts.Count);
        Assert.Equal("A", importResult.Texts[0]);
        // Cell with break produces break token <ox:k0/>
        Assert.Contains("<ox:k0/>", importResult.Texts[1]);
        Assert.Equal("D", importResult.Texts[2]);
    }

    /// <summary>
    /// Verifies P02: Table cell with rich runs produces ox:rN tokens and preserves identity export.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task P02_RichCellIdentity_PreservesExactBytes()
    {
        var service = CreateService();
        var richParagraph = new A.Paragraph(
            new A.Run(new A.RunProperties { Bold = true }, new A.Text("Bold")),
            new A.Run(new A.Text(" plain")));
        var row = new A.TableRow(
            OfficeFixtureFactory.DrawingCell(richParagraph),
            OfficeFixtureFactory.DrawingCell(new A.Paragraph()));

        var pptx = OfficeFixtureFactory.CreatePowerPointWithTable(row);

        using var importStream = new MemoryStream(pptx);
        var importResult = await service.ImportAsync(importStream);

        Assert.Empty(importResult.Errors);
        Assert.Single(importResult.Texts);
        Assert.Contains("<ox:r0>Bold</ox:r0>", importResult.Texts[0]);

        using var exportStream = new MemoryStream(pptx);
        var exportResult = await service.ExportAsync(exportStream, importResult.Texts);

        Assert.Empty(exportResult.Errors);
        Assert.NotNull(exportResult.Content);
        Assert.Equal(pptx, exportResult.Content);
    }

    /// <summary>
    /// Verifies P03: Merged table cell with horizontal merge follower is skipped.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task P03_MergedTable_SkipsContinuationCell()
    {
        var service = CreateService();
        var mergedCell = OfficeFixtureFactory.DrawingCell(new A.Paragraph(new A.Run(new A.Text("Merged"))));
        mergedCell.GridSpan = 2;

        var continuationCell = OfficeFixtureFactory.DrawingCell(new A.Paragraph());
        continuationCell.HorizontalMerge = true;

        var row = new A.TableRow(mergedCell, continuationCell);
        var pptx = OfficeFixtureFactory.CreatePowerPointWithTable(row);

        using var importStream = new MemoryStream(pptx);
        var importResult = await service.ImportAsync(importStream);

        Assert.Empty(importResult.Errors);
        Assert.Single(importResult.Texts);
        Assert.Equal("Merged", importResult.Texts[0]);
    }

    /// <summary>
    /// Verifies P04: Slide shape text bodies are extracted and translated.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task P04_SlideShapes_ExtractsAndTranslates()
    {
        var service = CreateService();
        var pptx = OfficeFixtureFactory.CreatePowerPointPresentation("Slide 1 Title", "Slide 2 Title");

        using var importStream = new MemoryStream(pptx);
        var importResult = await service.ImportAsync(importStream);

        Assert.Empty(importResult.Errors);
        Assert.Equal(new[] { "Slide 1 Title", "Slide 2 Title" }, importResult.Texts);

        using var exportStream = new MemoryStream(pptx);
        var exportResult = await service.ExportAsync(exportStream, new[] { "Tieu de 1", "Tieu de 2" });

        Assert.Empty(exportResult.Errors);
        Assert.NotNull(exportResult.Content);

        using var doc = PresentationDocument.Open(new MemoryStream(exportResult.Content!), false);
        var textNodes = doc.PresentationPart!.SlideParts.SelectMany(sp => sp.Slide?.Descendants<A.Text>() ?? Enumerable.Empty<A.Text>()).ToList();
        Assert.Contains(textNodes, t => t.Text == "Tieu de 1");
        Assert.Contains(textNodes, t => t.Text == "Tieu de 2");
    }

    /// <summary>
    /// Verifies P08: Identity export returns exact source bytes.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task P08_IdentityExport_ReturnsExactSourceBytes()
    {
        var service = CreateService();
        var pptx = OfficeFixtureFactory.CreatePowerPointPresentation("Original Slide Text");

        using var importStream = new MemoryStream(pptx);
        var importResult = await service.ImportAsync(importStream);

        using var exportStream = new MemoryStream(pptx);
        var exportResult = await service.ExportAsync(exportStream, importResult.Texts);

        Assert.Empty(exportResult.Errors);
        Assert.NotNull(exportResult.Content);
        Assert.Equal(pptx, exportResult.Content);
    }
}
