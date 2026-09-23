using System.Text;
using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Markdown;
using FileHandler.Api.Modules.Word;
using FileHandler.Tests.Modules.Office;
using Microsoft.Extensions.Options;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace FileHandler.Tests.Common;

/// <summary>
/// Verifies compact public tokens retain fixed source objects during export.
/// </summary>
public sealed class CompactTokenTests
{

    /// <summary>
    /// Hides bookmark and cached page-break edges while retaining their exact XML.
    /// </summary>
    /// <param name="reserved">Whether editable text contains reserved token syntax.</param>
    /// <returns>Task completing after plain or escaped import and preservation assertions.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Word_EdgeObjectsSurviveTranslation(bool reserved)
    {
        var original = reserved ? "Use <ox:b0/>" : "改訂履歴";
        var source = OfficeFixtureFactory.CreateWordDocumentWithElements(new W.Paragraph(
            new W.BookmarkStart { Id = "0", Name = "history" },
            new W.Run(new W.LastRenderedPageBreak(), new W.Text(original)),
            new W.BookmarkEnd { Id = "0" }));
        var service = WordService.Create();
        var imported = await service.ImportAsync(new MemoryStream(source));
        Assert.Equal(reserved ? @"<ox:r0>Use \<ox:b0/></ox:r0>" : original, Assert.Single(imported.Texts));
        var identity = await service.ExportAsync(new MemoryStream(source), imported.Texts);
        Assert.Equal(source, identity.Content);
        var output = await service.ExportAsync(new MemoryStream(source), [reserved ? "<ox:r0>History</ox:r0>" : "History"]);
        Assert.Empty(output.Errors);
        Assert.Empty(output.Metadata.Skipped);
        using var before = WordprocessingDocument.Open(new MemoryStream(source), false);
        using var after = WordprocessingDocument.Open(new MemoryStream(output.Content!), false);
        var expected = before.MainDocumentPart!.Document!.OuterXml.Replace(original.Replace("<", "&lt;").Replace(">", "&gt;"), "History", StringComparison.Ordinal);
        Assert.Equal(expected, after.MainDocumentPart!.Document!.OuterXml);
    }

    /// <summary>
    /// Collapses fixed groups while preserving hidden fragments during run reordering.
    /// </summary>
    /// <returns>Task completing after accepted reorder and rejected boundary crossing assertions.</returns>
    [Fact]
    public async Task Word_CollapsedBoundariesPreservePositionsWhenReordering()
    {
        var source = OfficeFixtureFactory.CreateWordDocumentWithElements(new W.Paragraph(
            new W.BookmarkStart { Id = "0", Name = "all" },
            new W.Run(new W.LastRenderedPageBreak(), new W.Text("red")),
            new W.Run(new W.RunProperties(new W.Bold()), new W.Text(" car")),
            new W.Run(new W.TabChar(), new W.Break()),
            new W.Run(new W.Text("blue")), new W.Run(new W.Text(" bike")),
            new W.Run(new W.RunProperties(new W.Italic()), new W.Text(" now")),
            new W.BookmarkEnd { Id = "0" }));
        var service = WordService.Create();
        var imported = await service.ImportAsync(new MemoryStream(source));
        Assert.Equal("<ox:r0>red</ox:r0><ox:r1> car</ox:r1><ox:b2/><ox:r2>blue bike</ox:r2><ox:r3> now</ox:r3>", Assert.Single(imported.Texts));
        var translated = "<ox:r1>xe </ox:r1><ox:r0>đỏ</ox:r0><ox:b2/><ox:r3>nay </ox:r3><ox:r2>xe xanh</ox:r2>";
        var output = await service.ExportAsync(new MemoryStream(source), [translated]);
        Assert.Empty(output.Errors);
        Assert.Empty(output.Metadata.Skipped);
        using var after = WordprocessingDocument.Open(new MemoryStream(output.Content!), false);
        var paragraph = after.MainDocumentPart!.Document!.Body!.Elements<W.Paragraph>().Single();
        Assert.IsType<W.BookmarkStart>(paragraph.FirstChild);
        Assert.IsType<W.BookmarkEnd>(paragraph.LastChild);
        var contents = paragraph.Elements<W.Run>().SelectMany(r => r.ChildElements).Where(e => e is not W.RunProperties).ToArray();
        Assert.Equal(new[] { "lastRenderedPageBreak", "t", "t", "tab", "br", "t", "t", "t" }, contents.Select(e => e.LocalName));
        Assert.Equal(new[] { "xe ", "đỏ", "nay ", "xe xanh", "" }, contents.OfType<W.Text>().Select(t => t.Text));
        var crossed = translated.Replace("<ox:r0>đỏ</ox:r0><ox:b2/>", "<ox:b2/><ox:r0>đỏ</ox:r0>", StringComparison.Ordinal);
        var rejected = await service.ExportAsync(new MemoryStream(source), [crossed]);
        Assert.Equal(source, rejected.Content);
        Assert.Contains(rejected.Metadata.Skipped, s => s.Code == SkipCodes.OfficeTokenMismatch);
    }

    /// <summary>
    /// Restores hidden HTML, entities and boundary groups from compact Markdown units.
    /// </summary>
    /// <param name="sourceText">Original Markdown source.</param>
    /// <param name="wire">Expected public source unit.</param>
    /// <param name="translation">Compact translated unit.</param>
    /// <param name="expected">Expected exported Markdown.</param>
    /// <returns>Task completing after import, identity and translated export assertions.</returns>
    [Theory]
    [InlineData("<span>Hello</span>", "Hello", "Bonjour", "<span>Bonjour</span>")]
    [InlineData("&amp; Hello &amp;", " Hello ", " Bonjour ", "&amp; Bonjour &amp;")]
    [InlineData("<span>**red** car</span>", "<ox:r0>red</ox:r0><ox:r1> car</ox:r1>", "<ox:r1>xe </ox:r1><ox:r0>đỏ</ox:r0>", "<span>xe **đỏ**</span>")]
    [InlineData("**red** car &amp; &copy; blue", "<ox:r0>red</ox:r0><ox:r1> car </ox:r1><ox:b0/><ox:r2> blue</ox:r2>", "<ox:r1>xe </ox:r1><ox:r0>đỏ </ox:r0><ox:b0/><ox:r2> xanh</ox:r2>", "xe **đỏ** &amp; &copy; xanh")]
    public async Task Markdown_HiddenObjectsRoundTrip(string sourceText, string wire, string translation, string expected)
    {
        var source = Encoding.UTF8.GetBytes(sourceText);
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var imported = await service.ImportAsync(new MemoryStream(source));
        Assert.Equal(wire, Assert.Single(imported.Texts));
        var identity = await service.ExportAsync(new MemoryStream(source), imported.Texts);
        Assert.Equal(source, identity.Content);
        var output = await service.ExportAsync(new MemoryStream(source), [translation]);
        Assert.Empty(output.Errors);
        Assert.Empty(output.Metadata.Skipped);
        Assert.Equal(expected, Encoding.UTF8.GetString(output.Content!));
    }
}
