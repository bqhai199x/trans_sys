using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;
using FileHandler.Api.Modules.PowerPoint;
using FileHandler.Api.Modules.Word;
using A = DocumentFormat.OpenXml.Drawing;
using V = DocumentFormat.OpenXml.Vml;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace FileHandler.Tests.Modules.Office;

/// <summary>
/// Verifies reconstruction preserves shared scalars and nested translation units.
/// </summary>
public sealed class OfficeReorderingRegressionTests
{

    /// <summary>
    /// Keeps raw controls in one physical scalar when another region changes run order.
    /// </summary>
    /// <param name="control">Fixed raw control sequence inside source text.</param>
    /// <param name="powerPoint">Whether source uses DrawingML instead of WordprocessingML.</param>
    /// <returns>Task completing after export, scalar and formatting assertions.</returns>
    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", false)]
    [InlineData("\t", false)]
    [InlineData("\n", true)]
    [InlineData("\r\n", true)]
    [InlineData("\t", true)]
    public async Task RawControls_PreserveSharedScalarDuringReorder(string control, bool powerPoint)
    {
        byte[] source;
        IFileHandler service;
        if (powerPoint)
        {
            using var buffer = new MemoryStream();
            buffer.Write(OfficeFixtureFactory.CreatePowerPointPresentation("placeholder"));
            using (var document = PresentationDocument.Open(buffer, true))
            {
                var paragraph = document.PresentationPart!.SlideParts.Single().Slide!.Descendants<A.Paragraph>().Single();
                paragraph.RemoveAllChildren();
                paragraph.Append(new A.Run(new A.Text("a" + control + "b")),
                    new A.Run(new A.RunProperties { Bold = true }, new A.Text("red")),
                    new A.Run(new A.Text(" car")));
            }
            source = buffer.ToArray();
            service = PowerPointService.Create();
        }
        else
        {
            source = OfficeFixtureFactory.CreateWordDocumentWithElements(new W.Paragraph(
                new W.Run(new W.Text("a" + control + "b")),
                new W.Run(new W.RunProperties(new W.Bold()), new W.Text("red")),
                new W.Run(new W.Text(" car"))));
            service = WordService.Create();
        }
        var imported = await service.ImportAsync(new MemoryStream(source));
        Assert.Empty(imported.Errors);
        var wire = Assert.Single(imported.Texts);
        var translation = wire.Replace("<ox:r0>a</ox:r0>", "<ox:r0>A</ox:r0>", StringComparison.Ordinal)
            .Replace("<ox:r1>b</ox:r1>", "<ox:r1>B</ox:r1>", StringComparison.Ordinal)
            .Replace("<ox:r2>red</ox:r2><ox:r3> car</ox:r3>", "<ox:r3>xe </ox:r3><ox:r2>do</ox:r2>", StringComparison.Ordinal);
        Assert.Null(TranslationTokenParser.Validate(wire, translation));
        var exported = await service.ExportAsync(new MemoryStream(source), [translation]);
        Assert.Empty(exported.Errors);
        Assert.Empty(exported.Metadata.Skipped);
        // XML parsing normalizes source CRLF sequences to LF before extraction.
        var expectedScalar = "A" + control.Replace("\r\n", "\n", StringComparison.Ordinal) + "B";
        using var output = new MemoryStream(exported.Content!);
        if (powerPoint)
        {
            using var document = PresentationDocument.Open(output, false);
            var runs = document.PresentationPart!.SlideParts.Single().Slide!.Descendants<A.Run>().ToArray();
            Assert.Equal(new[] { expectedScalar, "xe ", "do" }, runs.Select(run => run.InnerText));
            Assert.True(runs[^1].RunProperties!.Bold!.Value);
        }
        else
        {
            using var document = WordprocessingDocument.Open(output, false);
            var runs = document.MainDocumentPart!.Document!.Descendants<W.Run>().ToArray();
            Assert.Equal(new[] { expectedScalar, "xe ", "do" }, runs.Select(run => run.InnerText));
            Assert.NotNull(runs[^1].RunProperties!.GetFirstChild<W.Bold>());
        }
    }

    /// <summary>
    /// Preserves nested translations, formatting and tables while an owner reorders its children.
    /// </summary>
    /// <param name="reorderInside">Whether textbox content also changes run order.</param>
    /// <param name="tableInside">Whether translated paragraph belongs to a textbox table.</param>
    /// <param name="moveTextbox">Whether textbox anchor moves before outer text.</param>
    /// <returns>Task completing after nested export and reimport assertions.</returns>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task Word_ReordersOwnerAndTranslatesTextbox(bool reorderInside, bool tableInside, bool moveTextbox)
    {
        var inner = reorderInside
            ? new W.Paragraph(new W.Run(new W.RunProperties(new W.Bold()), new W.Text("blue")), new W.Run(new W.Text(" bike")))
            : OfficeFixtureFactory.WordParagraph("Inside");
        OpenXmlElement content = tableInside ? OfficeFixtureFactory.MakeWordTable(new W.TableCell(inner)) : inner;
        var textbox = new W.Picture(new V.Shape(new V.TextBox(new W.TextBoxContent(content,
            OfficeFixtureFactory.WordParagraph("Second")))) { Id = "box1", Style = "width:100pt;height:50pt" });
        var source = OfficeFixtureFactory.CreateWordDocumentWithElements(new W.Paragraph(
            new W.Run(new W.RunProperties(new W.Bold()), new W.Text("red")),
            new W.Run(new W.Text(" car")), new W.Run(textbox)), OfficeFixtureFactory.WordParagraph("Outside"));
        var service = WordService.Create();
        var imported = await service.ImportAsync(new MemoryStream(source));
        Assert.Empty(imported.Errors);
        Assert.Equal(4, imported.Texts.Count);
        var outer = "<ox:r1>xe </ox:r1><ox:r0>do</ox:r0>";
        var translations = new[]
        {
            moveTextbox ? "<ox:k0/>" + outer : outer + "<ox:k0/>",
            reorderInside ? "<ox:r1>xe </ox:r1><ox:r0>xanh</ox:r0>" : "Trong",
            "Hai", "Ngoai"
        };
        for (var index = 0; index < translations.Length; index++)
            Assert.Null(TranslationTokenParser.Validate(imported.Texts[index], translations[index]));
        var exported = await service.ExportAsync(new MemoryStream(source), translations);
        Assert.Empty(exported.Errors);
        Assert.Empty(exported.Metadata.Skipped);
        using var document = WordprocessingDocument.Open(new MemoryStream(exported.Content!), false);
        var nestedText = (reorderInside ? "xe xanh" : "Trong") + "Hai";
        Assert.Equal((moveTextbox ? nestedText + "xe do" : "xe do" + nestedText) + "Ngoai", document.MainDocumentPart!.Document!.InnerText);
        Assert.Equal(nestedText, Assert.Single(document.MainDocumentPart.Document.Descendants<W.TextBoxContent>()).InnerText);
        Assert.Equal(tableInside ? 1 : 0, document.MainDocumentPart.Document.Descendants<W.Table>().Count());
        Assert.Contains(document.MainDocumentPart.Document.Descendants<W.Run>(), run => run.InnerText == "do" && run.RunProperties?.GetFirstChild<W.Bold>() is not null);
        if (reorderInside)
            Assert.Contains(document.MainDocumentPart.Document.Descendants<W.Run>(), run => run.InnerText == "xanh" && run.RunProperties?.GetFirstChild<W.Bold>() is not null);
        var reimported = await service.ImportAsync(new MemoryStream(exported.Content!));
        Assert.Empty(reimported.Errors);
        Assert.Equal(4, reimported.Texts.Count);
    }

    /// <summary>
    /// Rejects unplanned mutations inside and outside a verified reconstruction region.
    /// </summary>
    /// <param name="inside">Whether unexpected text belongs to reconstructed paragraph.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Word_ReconstructionMaskRejectsUnexpectedMutation(bool inside)
    {
        var bytes = OfficeFixtureFactory.CreateWordDocumentWithElements(new W.Paragraph(
            new W.Run(new W.RunProperties(new W.Bold()), new W.Text("red")),
            new W.Run(new W.Text(" car"))), OfficeFixtureFactory.WordParagraph("Outside"));
        using var source = new OfficeSource(bytes, "source", OfficeFormat.Word, new());
        var codec = new OfficeTextCodec(new(), new());
        var plan = new WordExtractor(codec, new WordTableReader(), new()).Analyze(source,
            new OfficePackageInspector(new()).Inspect(source, default), default);
        var decoded = codec.ValidateAndDecode(plan.Units, ["<ox:r1>xe </ox:r1><ox:r0>do</ox:r0>", "Ngoai"], OfficeFormat.Word, default);
        Assert.Empty(decoded.Errors);
        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var root = document.MainDocumentPart!.Document!;
        var mask = new WordTranslationApplier().Prepare(plan, decoded.DecodedUnits!, default).EditMasks["/word/document.xml"];
        OfficeTextBindings.ApplyNested(root, plan.Units.Select((unit, index) => (unit, decoded.DecodedUnits![index])).ToArray(), mask, default);
        using var buffer = new MemoryStream();
        buffer.Write(bytes);
        using (var changed = WordprocessingDocument.Open(buffer, true))
        {
            changed.MainDocumentPart!.Document = (W.Document)root.CloneNode(true);
            var texts = changed.MainDocumentPart.Document.Descendants<W.Text>().ToArray();
            texts[inside ? 0 : texts.Length - 1].Text = "Unexpected";
        }
        var output = buffer.ToArray();
        var result = new OfficePackageValidator(new()).ValidateOutput(source, new(output, "output", output.Length, "test"),
            new Dictionary<string, OfficeEditMask> { ["/word/document.xml"] = mask }, default);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "office_output_invalid");
    }
}
