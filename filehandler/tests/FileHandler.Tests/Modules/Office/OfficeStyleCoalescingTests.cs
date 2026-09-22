using static FileHandler.Tests.Modules.Office.OfficeTranslationTestData;
using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;
using FileHandler.Api.Modules.Word;
using FileHandler.Api.Modules.Excel;
using FileHandler.Api.Modules.PowerPoint;
using W = DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace FileHandler.Tests.Modules.Office;

/// <summary>
/// Verifies style-based coalescing across physical Office runs.
/// </summary>
public sealed class OfficeStyleCoalescingTests
{

    /// <summary>
    /// Alternating Latin and Japanese date fragments.
    /// </summary>
    private static readonly string[] DateParts = ["2026", "年", "3", "月", "31", "日"];

    /// <summary>
    /// Checks explicit default formatting against inherited paragraph formatting.
    /// </summary>
    /// <param name="inheritedBold">Whether omitted bold inherits enabled formatting.</param>
    /// <returns>Task completing after import and round-trip assertions.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrackTitle_DefaultPropertiesRespectInheritance(bool inheritedBold)
    {
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreatePowerPointPresentation("Track"));
        using (var document = PresentationDocument.Open(buffer, true))
        {
            var paragraph = document.PresentationPart!.SlideParts.Single().Slide!.Descendants<A.Paragraph>().First();
            paragraph.RemoveAllChildren();
            paragraph.Append(new A.ParagraphProperties(new A.DefaultRunProperties { Bold = inheritedBold }));
            foreach (var (text, index) in new[] { "Track", "2：JAVA21/25互換", "対応" }.Select((text, index) => (text, index)))
            {
                var properties = index == 1
                    ? new A.RunProperties("""
                        <a:rPr xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" b="0" i="0" u="none" strike="noStrike" cap="none" spc="0" normalizeH="0" baseline="0" noProof="0">
                          <a:ln><a:noFill/></a:ln><a:effectLst/><a:uLnTx/><a:uFillTx/><a:cs typeface="+mn-cs"/>
                        </a:rPr>
                        """)
                    : new A.RunProperties();
                properties.Language = index == 0 ? "en-US" : "ja-JP";
                paragraph.Append(new A.Run(properties, new A.Text(text)));
            }
        }
        var source = buffer.ToArray();
        var service = Service("pptx");
        var imported = await service.ImportAsync(new MemoryStream(source), true, default);
        Assert.Empty(imported.Errors);
        Assert.Equal(inheritedBold
            ? "<ox:r0>Track</ox:r0><ox:r1>2：JAVA21/25互換</ox:r1><ox:r2>対応</ox:r2>"
            : "Track2：JAVA21/25互換対応", Assert.Single(ContentTexts(imported)));
        var translation = inheritedBold
            ? "<ox:r0>Track</ox:r0><ox:r1>2: Hỗ trợ tương thích JAVA21/25</ox:r1><ox:r2></ox:r2>"
            : "Track2: Hỗ trợ tương thích JAVA21/25";
        var exported = await ExportContentAsync(service, source, [translation]);
        Assert.Empty(exported.Errors);
        using var result = PresentationDocument.Open(new MemoryStream(exported.Content!), false);
        Assert.Equal("Track2: Hỗ trợ tương thích JAVA21/25",
            string.Concat(result.PresentationPart!.SlideParts.Single().Slide!.Descendants<A.Text>().Select(t => t.Text)));
    }

    /// <summary>
    /// Checks empty slots clear original text while wholly empty units retain source.
    /// </summary>
    /// <param name="format">Office filename extension.</param>
    /// <returns>Task completing after export and validation assertions.</returns>
    [Theory]
    [InlineData("docx")]
    [InlineData("pptx")]
    [InlineData("xlsx")]
    public async Task EmptySlot_ClearsSourceButRequiresNonemptyUnit(string format)
    {
        var source = CreateRuns(format, ["Track", "2：JAVA21/25互換", "対応"], true);
        var service = Service(format);
        var exported = await ExportContentAsync(service, source, ["<ox:r0>Track</ox:r0><ox:r1>2: Hỗ trợ tương thích JAVA21/25</ox:r1><ox:r2></ox:r2>"]);
        Assert.Empty(exported.Errors);
        var reimported = await service.ImportAsync(new MemoryStream(exported.Content!), true, default);
        Assert.Empty(reimported.Errors);
        Assert.Equal("<ox:r0>Track</ox:r0><ox:r1>2: Hỗ trợ tương thích JAVA21/25</ox:r1>", Assert.Single(ContentTexts(reimported)));
        var empty = await ExportContentAsync(service, source, ["<ox:r0></ox:r0><ox:r1> </ox:r1><ox:r2></ox:r2>"]);
        Assert.Equal(source, empty.Content);
        Assert.Contains(empty.Metadata.Skipped, e => e.Code == "empty_translation");
        var missing = await ExportContentAsync(service, source, ["<ox:r0>Track</ox:r0><ox:r1>2</ox:r1>"]);
        Assert.Equal(source, missing.Content);
        Assert.Contains(missing.Metadata.Skipped, e => e.Code == "office_token_mismatch");
    }

    /// <summary>
    /// Checks merged dates, exact identity export and translated scalar replacement.
    /// </summary>
    /// <param name="format">Office filename extension.</param>
    /// <returns>Task completing after round-trip assertions.</returns>
    [Theory]
    [InlineData("docx")]
    [InlineData("pptx")]
    [InlineData("xlsx")]
    public async Task MixedLanguageDate_MergesAndRoundTrips(string format)
    {
        var bytes = CreateDate(format);
        var service = Service(format);
        var imported = await service.ImportAsync(new MemoryStream(bytes), true, default);
        Assert.Empty(imported.Errors);
        Assert.Equal("2026年3月31日", Assert.Single(ContentTexts(imported)));
        var identity = await service.ExportAsync(new MemoryStream(bytes), imported.Texts);
        Assert.Empty(identity.Errors);
        Assert.Equal(bytes, identity.Content);
        const string translation = "ngày 31 tháng 3 năm 2026";
        var exported = await ExportContentAsync(service, bytes, [translation]);
        Assert.Empty(exported.Errors);
        var reimported = await service.ImportAsync(new MemoryStream(exported.Content!), true, default);
        Assert.Empty(reimported.Errors);
        Assert.Equal(translation, Assert.Single(ContentTexts(reimported)));
    }

    /// <summary>
    /// Ensures visible style differences retain independent translation slots.
    /// </summary>
    /// <param name="format">Office filename extension.</param>
    /// <returns>Task completing after token assertions.</returns>
    [Theory]
    [InlineData("docx")]
    [InlineData("pptx")]
    [InlineData("xlsx")]
    public async Task BoldDateFragment_RemainsSeparate(string format)
    {
        var imported = await Service(format).ImportAsync(new MemoryStream(CreateDate(format, true)), true, default);
        Assert.Empty(imported.Errors);
        Assert.Equal("<ox:r0>2026</ox:r0><ox:r1>年</ox:r1><ox:r2>3月31日</ox:r2>", Assert.Single(ContentTexts(imported)));
    }

    /// <summary>
    /// Verifies same-style space runs join mixed-language titles and retain exact bindings.
    /// </summary>
    /// <param name="format">Office extension.</param>
    /// <returns>Task completing after import and export assertions.</returns>
    [Theory]
    [InlineData("docx")]
    [InlineData("pptx")]
    [InlineData("xlsx")]
    public async Task SeparateSpaceRun_MergesTitleAndRoundTrips(string format)
    {
        var source = CreateRuns(format, [".NET", " ", "Framework/JAVA", "換装について"]);
        var service = Service(format);
        var imported = await service.ImportAsync(new MemoryStream(source), true, default);
        Assert.Empty(imported.Errors);
        Assert.Equal(".NET Framework/JAVA換装について", Assert.Single(ContentTexts(imported)));
        var identity = await service.ExportAsync(new MemoryStream(source), imported.Texts);
        Assert.Empty(identity.Errors);
        Assert.Equal(source, identity.Content);
        const string translation = "Về việc chuyển đổi .NET Framework/JAVA";
        var exported = await ExportContentAsync(service, source, [translation]);
        Assert.Empty(exported.Errors);
        var reimported = await service.ImportAsync(new MemoryStream(exported.Content!), true, default);
        Assert.Empty(reimported.Errors);
        Assert.Equal(translation, Assert.Single(ContentTexts(reimported)));
    }

    /// <summary>
    /// Verifies differently styled space runs remain protected during translation.
    /// </summary>
    /// <param name="format">Office extension.</param>
    /// <returns>Task completing after protected-space assertions.</returns>
    [Theory]
    [InlineData("docx")]
    [InlineData("pptx")]
    [InlineData("xlsx")]
    public async Task DifferentStyleSpace_RemainsProtected(string format)
    {
        var source = CreateRuns(format, ["A", " ", "B"], true);
        var service = Service(format);
        var imported = await service.ImportAsync(new MemoryStream(source), true, default);
        Assert.Empty(imported.Errors);
        Assert.Equal("<ox:r0>A</ox:r0><ox:k0/><ox:r1>B</ox:r1>", Assert.Single(ContentTexts(imported)));
        var exported = await ExportContentAsync(service, source, ["<ox:r0>X</ox:r0><ox:k0/><ox:r1>Y</ox:r1>"]);
        Assert.Empty(exported.Errors);
        var reimported = await service.ImportAsync(new MemoryStream(exported.Content!), true, default);
        Assert.Empty(reimported.Errors);
        Assert.Equal("<ox:r0>X</ox:r0><ox:k0/><ox:r1>Y</ox:r1>", Assert.Single(ContentTexts(reimported)));
    }

    /// <summary>
    /// Verifies same-style spaces do not absorb raw spreadsheet control-only runs.
    /// </summary>
    /// <param name="control">Protected raw control.</param>
    /// <returns>Task completing after token assertions.</returns>
    [Theory]
    [InlineData("\t")]
    [InlineData("\n")]
    public async Task SpreadsheetControlRun_RemainsProtected(string control)
    {
        var imported = await Service("xlsx").ImportAsync(new MemoryStream(CreateRuns("xlsx", ["A", control, "B"])), true, default);
        Assert.Empty(imported.Errors);
        Assert.Equal("<ox:r0>A</ox:r0><ox:k0/><ox:r1>B</ox:r1>", Assert.Single(ContentTexts(imported)));
    }

    /// <summary>
    /// Verifies canonical comparison ignores prefixes, property order and boolean spelling.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void EquivalentProperties_HaveSameFingerprint()
    {
        var left = new W.RunProperties("<w:rPr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:b/><w:i w:val=\"true\"/><w:lang w:val=\"ja-JP\"/></w:rPr>");
        var right = new W.RunProperties("<x:rPr xmlns:x=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><x:i x:val=\"1\"/><x:b x:val=\"on\"/><x:noProof/></x:rPr>");
        Assert.Equal(OfficeStyleFingerprint.Create(left), OfficeStyleFingerprint.Create(right));
        Assert.Equal(OfficeStyleFingerprint.Create(null), OfficeStyleFingerprint.Create(new W.RunProperties(new W.Languages { Val = "ja-JP" })));
        Assert.NotEqual(OfficeStyleFingerprint.Create(left), OfficeStyleFingerprint.Create(new W.RunProperties(new W.Color { Val = "FF0000" })));
    }

    /// <summary>
    /// Creates format-specific handler using production defaults.
    /// </summary>
    /// <param name="format">Office extension.</param>
    /// <returns>Handler for requested format.</returns>
    internal static IFileHandler Service(string format) => format switch
    {
        "docx" => WordService.Create(), "pptx" => PowerPointService.Create(), "xlsx" => ExcelService.Create(),
        _ => throw new ArgumentException("Unknown format.", nameof(format))
    };

    /// <summary>
    /// Creates minimal date fixture with metadata-only run differences.
    /// </summary>
    /// <param name="format">Office extension.</param>
    /// <param name="boldYearMarker">Whether year marker has a distinct visible style.</param>
    /// <returns>Complete Office package bytes.</returns>
    internal static byte[] CreateDate(string format, bool boldYearMarker = false) => CreateRuns(format, DateParts, boldYearMarker);

    /// <summary>
    /// Creates rich text split into physical runs with alternating language metadata.
    /// </summary>
    /// <param name="format">Office extension.</param>
    /// <param name="parts">Text values for physical runs.</param>
    /// <param name="boldYearMarker">Whether second run has distinct bold formatting.</param>
    /// <returns>Complete Office package bytes.</returns>
    private static byte[] CreateRuns(string format, string[] parts, bool boldYearMarker = false)
    {
        if (format == "docx")
            return OfficeFixtureFactory.CreateWordDocumentWithElements(new W.Paragraph(parts.Select((text, i) =>
            {
                var properties = new W.RunProperties();
                if (boldYearMarker && i == 1) properties.Append(new W.Bold());
                properties.Append(new W.Languages { Val = i % 2 == 0 ? "en-US" : "ja-JP" });
                return new W.Run(properties, new W.Text(text) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve });
            })));
        using var buffer = new MemoryStream();
        if (format == "pptx")
        {
            buffer.Write(OfficeFixtureFactory.CreatePowerPointPresentation("Date"));
            using (var document = PresentationDocument.Open(buffer, true))
            {
                var paragraph = document.PresentationPart!.SlideParts.Single().Slide!.Descendants<A.Paragraph>().First();
                paragraph.RemoveAllChildren();
                foreach (var (text, i) in parts.Select((text, i) => (text, i)))
                {
                    var properties = new A.RunProperties
                    {
                        Language = i % 2 == 0 ? "en-US" : "ja-JP", AlternativeLanguage = i % 2 == 0 ? "ja-JP" : "en-US",
                        Dirty = i % 2 == 0
                    };
                    if (boldYearMarker && i == 1) properties.Bold = true;
                    properties.Append(new A.LatinFont { Typeface = "Meiryo UI" }, new A.EastAsianFont { Typeface = "Meiryo UI" });
                    paragraph.Append(new A.Run(properties, new A.Text(text)));
                }
            }
        }
        else
        {
            buffer.Write(OfficeFixtureFactory.CreateExcelWithInlineStrings([["Date"]]));
            using var document = SpreadsheetDocument.Open(buffer, true);
            var inline = document.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<S.InlineString>().Single();
            inline.RemoveAllChildren();
            foreach (var (text, i) in parts.Select((text, i) => (text, i)))
            {
                var properties = new S.RunProperties();
                if (boldYearMarker && i == 1) properties.Append(new S.Bold());
                properties.Append(new S.RunFont { Val = "Meiryo UI" });
                inline.Append(new S.Run(properties, new S.Text(text) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }));
            }
        }
        return buffer.ToArray();
    }
}
