using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Modules.Word;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace FileHandler.Tests.Modules.Office;

/// <summary>
/// Checks hint-only run coalescing against effective font declarations.
/// </summary>
public sealed class WordFontHintTests
{

    /// <summary>
    /// Merges sample fragments only when font resolution proves hint equivalence.
    /// </summary>
    /// <param name="scenario">Font inheritance or unresolved context under test.</param>
    /// <param name="merged">Whether one plain unit is expected.</param>
    /// <returns>Task completing after extraction and preservation assertions.</returns>
    [Theory]
    [InlineData("paragraph", true)]
    [InlineData("defaults", true)]
    [InlineData("character", true)]
    [InlineData("different", false)]
    [InlineData("theme", false)]
    [InlineData("cycle", false)]
    [InlineData("unknown", false)]
    [InlineData("complexHint", false)]
    [InlineData("tableUnknown", false)]
    [InlineData("characterDifferent", false)]
    [InlineData("defaultCharacterDifferent", false)]
    [InlineData("tableComplexHint", false)]
    [InlineData("tableResolved", true)]
    [InlineData("bold", false)]
    public async Task EffectiveFonts_ControlHintCoalescing(string scenario, bool merged)
    {
        var source = CreateDocument(scenario, ["V", "er2", ".0", "対応"]);
        var service = WordService.Create();
        var imported = await service.ImportAsync(new MemoryStream(source));
        Assert.Empty(imported.Errors);
        Assert.Equal(merged ? "Ver2.0対応" : "<ox:r0>V</ox:r0><ox:r1>er2</ox:r1><ox:r2>.0</ox:r2><ox:r3>対応</ox:r3>", Assert.Single(imported.Texts));
        var identity = await service.ExportAsync(new MemoryStream(source), imported.Texts);
        Assert.Equal(source, identity.Content);
        if (!merged) return;
        var output = await service.ExportAsync(new MemoryStream(source), ["Hỗ trợ Ver2.0"]);
        Assert.Empty(output.Errors);
        Assert.Empty(output.Metadata.Skipped);
        using var before = WordprocessingDocument.Open(new MemoryStream(source), false);
        using var after = WordprocessingDocument.Open(new MemoryStream(output.Content!), false);
        Assert.Equal("Hỗ trợ Ver2.0", after.MainDocumentPart!.Document!.InnerText);
        Assert.Equal(before.MainDocumentPart!.Document!.Descendants<W.RunProperties>().Select(p => p.OuterXml),
            after.MainDocumentPart.Document.Descendants<W.RunProperties>().Select(p => p.OuterXml));
    }

    /// <summary>
    /// Coalesces Japanese text and quotation fragments matching sample style context.
    /// </summary>
    /// <returns>Task completing after plain unit assertion.</returns>
    [Fact]
    public async Task JapaneseQuoteFragments_BecomeOnePlainUnit()
    {
        var source = CreateDocument("paragraph", ["解析トライアル結果表示画面に登録ボタンを追加、", "”", "閉じる", "”", "ボタンを", "”", "戻る", "”", "に修正"]);
        var imported = await WordService.Create().ImportAsync(new MemoryStream(source));
        Assert.Equal("解析トライアル結果表示画面に登録ボタンを追加、”閉じる”ボタンを”戻る”に修正", Assert.Single(imported.Texts));
    }

    /// <summary>
    /// Creates hint-varying runs with document defaults and inherited style declarations.
    /// </summary>
    /// <param name="scenario">Font resolution case.</param>
    /// <param name="fragments">Text assigned to consecutive physical runs.</param>
    /// <returns>Complete Word package.</returns>
    private static byte[] CreateDocument(string scenario, string[] fragments)
    {
        var paragraph = new W.Paragraph(new W.ParagraphProperties(new W.ParagraphStyleId { Val = "sample" }));
        foreach (var (text, index) in fragments.Select((text, index) => (text, index)))
        {
            var fonts = new W.RunFonts { HighAnsi = "ＭＳ Ｐ明朝" };
            if (index % 2 == 1) fonts.Hint = W.FontTypeHintValues.EastAsia;
            var properties = new W.RunProperties();
            if (scenario is "character" or "characterDifferent") properties.Append(new W.RunStyle { Val = "character" });
            properties.Append(fonts);
            if (scenario == "bold" && index % 2 == 1) properties.Append(new W.Bold());
            paragraph.Append(new W.Run(properties, new W.Text(text)));
        }
        OpenXmlElement bodyContent = scenario.StartsWith("table", StringComparison.Ordinal)
            ? new W.Table(new W.TableProperties(new W.TableStyle { Val = "table" }), new W.TableGrid(new W.GridColumn()), new W.TableRow(new W.TableCell(paragraph))) : paragraph;
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreateWordDocumentWithElements(bodyContent));
        using (var document = WordprocessingDocument.Open(buffer, true))
        {
            var defaults = new W.RunFonts { AsciiTheme = W.ThemeFontValues.MinorHighAnsi, HighAnsiTheme = W.ThemeFontValues.MinorHighAnsi, EastAsiaTheme = W.ThemeFontValues.MinorEastAsia };
            var explicitFonts = new W.RunFonts { Ascii = "ＭＳ Ｐ明朝", HighAnsi = "Century", EastAsia = scenario == "different" ? "Other Font" : "ＭＳ Ｐ明朝" };
            if (scenario == "complexHint") explicitFonts.Hint = W.FontTypeHintValues.ComplexScript;
            var parent = new W.Style(new W.StyleRunProperties(explicitFonts)) { StyleId = "base", Type = W.StyleValues.Paragraph };
            var sample = new W.Style(new W.BasedOn { Val = scenario == "unknown" ? "missing" : "base" }) { StyleId = "sample", Type = W.StyleValues.Paragraph };
            if (scenario == "cycle") parent.PrependChild(new W.BasedOn { Val = "sample" });
            if (scenario is "theme" or "tableUnknown" or "character") parent.RemoveAllChildren();
            if (scenario is "defaults" or "tableUnknown") defaults = (W.RunFonts)explicitFonts.CloneNode(true);
            if (scenario == "defaults") parent.RemoveAllChildren();
            var styles = new W.Styles(new W.DocDefaults(new W.RunPropertiesDefault(new W.RunPropertiesBaseStyle(defaults))), parent, sample);
            if (scenario is "character" or "characterDifferent" or "defaultCharacterDifferent")
                styles.Append(new W.Style(new W.StyleRunProperties(new W.RunFonts { Ascii = "ＭＳ Ｐ明朝", EastAsia = scenario == "character" ? "ＭＳ Ｐ明朝" : "Other Font" }))
                { StyleId = "character", Type = W.StyleValues.Character, Default = scenario == "defaultCharacterDifferent" });
            if (scenario is "tableComplexHint" or "tableResolved")
                styles.Append(new W.Style(new W.StyleRunProperties(new W.RunFonts
                { Hint = scenario == "tableComplexHint" ? W.FontTypeHintValues.ComplexScript : W.FontTypeHintValues.Default }))
                { StyleId = "table", Type = W.StyleValues.Table });
            document.MainDocumentPart!.AddNewPart<StyleDefinitionsPart>().Styles = styles;
        }
        return buffer.ToArray();
    }
}
