using System.Text;
using System.Text.Json;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Markdown;
using FileHandler.Api.Modules.PlainText;
using FileHandler.Api.Modules.Word;
using FileHandler.Tests.Modules.Office;
using Microsoft.Extensions.Options;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace FileHandler.Tests.Common;

/// <summary>
/// End-to-end preservation with metadata-free run, anchor and boundary tokens.
/// </summary>
public sealed class SelfDescribingTokenTests
{

    /// <summary>
    /// Keeps custom HTML fixed when tag names resemble an autolink scheme.
    /// </summary>
    /// <param name="tag">Custom HTML tag name.</param>
    /// <returns>Task completing after accepted translation and rejected boundary crossing.</returns>
    [Theory]
    [InlineData("http-status")]
    [InlineData("https-widget")]
    public async Task Markdown_CustomHtmlRemainsFixed(string tag)
    {
        var source = Encoding.UTF8.GetBytes($"Status: <{tag}>ok</{tag}> now");
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var imported = await service.ImportAsync(new MemoryStream(source));
        var wire = Assert.Single(imported.Texts);
        Assert.Equal("<ox:r0>Status: </ox:r0><ox:b0/><ox:r1>ok</ox:r1><ox:b1/><ox:r2> now</ox:r2>", wire);
        var translated = wire.Replace("ok", "OK", StringComparison.Ordinal);
        var exported = await service.ExportAsync(new MemoryStream(source), [translated]);
        Assert.Empty(exported.Errors);
        Assert.Empty(exported.Metadata.Skipped);
        Assert.Equal($"Status: <{tag}>OK</{tag}> now", Encoding.UTF8.GetString(exported.Content!));
        var crossing = translated.Replace("<ox:b0/><ox:r1>OK</ox:r1>", "<ox:r1>OK</ox:r1><ox:b0/>", StringComparison.Ordinal);
        var rejected = await service.ExportAsync(new MemoryStream(source), [crossing]);
        Assert.Equal(source, rejected.Content);
        Assert.Contains(rejected.Metadata.Skipped, skip => skip.Code == SkipCodes.InvalidMarkerSyntax);
    }

    /// <summary>
    /// Allows parsed code, images and autolinks to move within their original boundary region.
    /// </summary>
    /// <param name="protectedSource">Movable Markdown object.</param>
    /// <returns>Task completing after token classification and rendered order assertions.</returns>
    [Theory]
    [InlineData("<http://example.com>")]
    [InlineData("<https://example.com>")]
    [InlineData("<mailto:test@example.com>")]
    [InlineData("<test@example.com>")]
    [InlineData("`code`")]
    [InlineData("![](image.png)")]
    public async Task Markdown_ParsedProtectedObjectsRemainMovable(string protectedSource)
    {
        var source = Encoding.UTF8.GetBytes("Before " + protectedSource + " after");
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var imported = await service.ImportAsync(new MemoryStream(source));
        Assert.Equal("<ox:r0>Before </ox:r0><ox:k0/><ox:r1> after</ox:r1>", Assert.Single(imported.Texts));
        var exported = await service.ExportAsync(new MemoryStream(source), ["<ox:r1>After </ox:r1><ox:r0>before </ox:r0><ox:k0/>"]);
        Assert.Empty(exported.Errors);
        Assert.Empty(exported.Metadata.Skipped);
        Assert.Equal("After before " + protectedSource, Encoding.UTF8.GetString(exported.Content!));
    }

    /// <summary>
    /// Exposes owner transitions as boundaries and permits reordering only inside hyperlink.
    /// </summary>
    /// <returns>Task completing after successful and rejected export checks.</returns>
    [Fact]
    public async Task Markdown_HyperlinkBoundariesAreVisible()
    {
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var source = Encoding.UTF8.GetBytes("Read [**red** car](https://example.com) now.");
        var imported = await service.ImportAsync(new MemoryStream(source));
        Assert.Equal("<ox:r0>Read </ox:r0><ox:b0/><ox:r1>red</ox:r1><ox:r2> car</ox:r2><ox:b1/><ox:r3> now.</ox:r3>", Assert.Single(imported.Texts));
        var json = JsonSerializer.Serialize(imported, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("tokenMetadata", json);
        var translated = "<ox:r0>Read </ox:r0><ox:b0/><ox:r2>xe </ox:r2><ox:r1>đỏ</ox:r1><ox:b1/><ox:r3> now.</ox:r3>";
        var output = await service.ExportAsync(new MemoryStream(source), [translated]);
        Assert.Empty(output.Errors);
        Assert.Empty(output.Metadata.Skipped);
        Assert.Equal("Read [xe **đỏ**](https://example.com) now.", Encoding.UTF8.GetString(output.Content!));
        var crossed = translated.Replace("<ox:r0>Read </ox:r0><ox:b0/>", "<ox:b0/><ox:r0>Read </ox:r0>", StringComparison.Ordinal);
        var rejected = await service.ExportAsync(new MemoryStream(source), [crossed]);
        Assert.Equal(source, rejected.Content);
        Assert.Contains(rejected.Metadata.Skipped, s => s.Code == SkipCodes.InvalidMarkerSyntax);
    }

    /// <summary>
    /// Preserves literal reserved prefixes through metadata-free import and export.
    /// </summary>
    /// <param name="markdown">Whether source uses Markdown escaping.</param>
    /// <returns>Task completing after encoded import and identity export assertions.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiteralTokenText_RoundTrips(bool markdown)
    {
        IFileHandler service = markdown ? MarkdownService.Create(Options.Create(new FileHandlingOptions()))
            : new PlainTextService(Options.Create(new FileHandlingOptions()));
        var source = Encoding.UTF8.GetBytes(markdown ? @"Use \<ox:b0/> literally." : "Use <ox:b0/> literally.");
        var imported = await service.ImportAsync(new MemoryStream(source));
        Assert.Equal(@"<ox:r0>Use \<ox:b0/> literally.</ox:r0>", Assert.Single(imported.Texts));
        var output = await service.ExportAsync(new MemoryStream(source), imported.Texts);
        Assert.Empty(output.Errors);
        Assert.Equal(source, output.Content);
    }

    /// <summary>
    /// Keeps Word tab boundaries fixed and retains source on crossing attempts.
    /// </summary>
    /// <returns>Task completing after import, accepted translation and fallback checks.</returns>
    [Fact]
    public async Task Word_TabIsFixedBoundary()
    {
        var source = OfficeFixtureFactory.CreateWordDocumentWithElements(new W.Paragraph(
            new W.Run(new W.Text("Name:")), new W.Run(new W.TabChar()), new W.Run(new W.Text("John"))));
        var service = WordService.Create();
        var imported = await service.ImportAsync(new MemoryStream(source));
        Assert.Equal("<ox:r0>Name:</ox:r0><ox:b0/><ox:r1>John</ox:r1>", Assert.Single(imported.Texts));
        var output = await service.ExportAsync(new MemoryStream(source), ["<ox:r1>John</ox:r1><ox:b0/><ox:r0>Tên:</ox:r0>"]);
        Assert.Contains(output.Metadata.Skipped, s => s.Code == SkipCodes.OfficeTokenMismatch);
        Assert.Equal(source, output.Content);
    }

    /// <summary>
    /// Inserts synthetic Word hyperlink boundaries while restoring physical runs within owner.
    /// </summary>
    /// <returns>Task completing after accepted run reordering and reimport checks.</returns>
    [Fact]
    public async Task Word_HyperlinkReordersOnlyInsideBoundaries()
    {
        var source = OfficeFixtureFactory.CreateWordDocumentWithElements(new W.Paragraph(
            new W.Run(new W.Text("Read ")),
            new W.Hyperlink(new W.Run(new W.RunProperties(new W.Bold()), new W.Text("red")), new W.Run(new W.Text(" car"))) { Anchor = "target" },
            new W.Run(new W.Text(" now"))));
        var service = WordService.Create();
        var imported = await service.ImportAsync(new MemoryStream(source));
        Assert.Equal("<ox:r0>Read </ox:r0><ox:b0/><ox:r1>red</ox:r1><ox:r2> car</ox:r2><ox:b1/><ox:r3> now</ox:r3>", Assert.Single(imported.Texts));
        var output = await service.ExportAsync(new MemoryStream(source), ["<ox:r0>Read </ox:r0><ox:b0/><ox:r2>xe </ox:r2><ox:r1>đỏ</ox:r1><ox:b1/><ox:r3> now</ox:r3>"]);
        Assert.Empty(output.Errors);
        Assert.DoesNotContain(output.Metadata.Skipped, s => s.Severity == SkipSeverity.Warning);
        using var document = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(new MemoryStream(output.Content!), false);
        var link = Assert.Single(document.MainDocumentPart!.Document!.Descendants<W.Hyperlink>());
        Assert.Equal("xe đỏ", link.InnerText);
        Assert.Equal("đỏ", link.Elements<W.Run>().Single(r => r.RunProperties?.GetFirstChild<W.Bold>() is not null).InnerText);
    }
}
