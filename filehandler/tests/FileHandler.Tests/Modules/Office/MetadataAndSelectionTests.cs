using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Excel;
using FileHandler.Api.Modules.Markdown;
using FileHandler.Api.Modules.PlainText;
using FileHandler.Api.Modules.PowerPoint;
using FileHandler.Api.Modules.Word;
using Microsoft.Extensions.Options;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace FileHandler.Tests.Modules.Office;

/// <summary>
/// Verifies metadata projections, aligned partial processing and native selections.
/// </summary>
public sealed class MetadataAndSelectionTests
{

    /// <summary>
    /// Checks native inventory rejects malformed scalar values without surfacing unexpected failures.
    /// </summary>
    /// <param name="excel">Whether to mutate a sheet rather than a slide ID.</param>
    /// <returns>Task completing after discovery and import failure assertions.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MalformedNativeIds_ReturnSourceErrors(bool excel)
    {
        using var buffer = new MemoryStream();
        buffer.Write(excel ? OfficeFixtureFactory.CreateSelectionWorkbook() : OfficeFixtureFactory.CreateSelectionPresentation());
        using (OpenXmlPackage package = excel ? SpreadsheetDocument.Open(buffer, true) : PresentationDocument.Open(buffer, true))
        {
            if (package is SpreadsheetDocument workbook)
                workbook.WorkbookPart!.Workbook!.Sheets!.Elements<S.Sheet>().First().SetAttribute(new("", "sheetId", "", "bad"));
            else ((PresentationDocument)package).PresentationPart!.Presentation!.SlideIdList!.Elements<DocumentFormat.OpenXml.Presentation.SlideId>().First().SetAttribute(new("", "id", "", "bad"));
        }
        var bytes = buffer.ToArray();
        var errors = excel ? (await ExcelService.Create().GetSheetsAsync(new MemoryStream(bytes))).Errors : (await PowerPointService.Create().GetSlidesAsync(new MemoryStream(bytes))).Errors;
        Assert.Equal("invalid_office_package", Assert.Single(errors).Code);
        IFileHandler service = excel ? ExcelService.Create() : PowerPointService.Create();
        Assert.Equal("invalid_office_package", Assert.Single((await service.ImportAsync(new MemoryStream(bytes))).Errors).Code);
    }

    /// <summary>
    /// Checks preserved Excel cells cannot prevent independent content patches or lose their source payload.
    /// </summary>
    /// <param name="reason">Cell exclusion to construct.</param>
    /// <returns>Task completing after metadata and independent XML assertions.</returns>
    [Theory]
    [InlineData("implicit_cell_address")]
    [InlineData("phonetic_content")]
    [InlineData("non_text_cell")]
    [InlineData("whitespace_cell")]
    public async Task ExcelCellExclusions_PreserveCellAndTranslateNeighbor(string reason)
    {
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreateExcelWithInlineStrings([["Preserved", "Visible"]]));
        string preserved;
        using (var document = SpreadsheetDocument.Open(buffer, true))
        {
            var cell = document.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<S.Cell>().First();
            if (reason == "implicit_cell_address") cell.CellReference = null;
            if (reason == "phonetic_content") cell.InlineString!.Append(new S.PhoneticRun(new S.Text("Reading")) { BaseTextStartIndex = 0, EndingBaseIndex = 9 });
            if (reason == "non_text_cell") { cell.DataType = S.CellValues.Number; cell.InlineString = null; cell.CellValue = new("42"); }
            if (reason == "whitespace_cell") cell.InlineString!.GetFirstChild<S.Text>()!.Text = "   ";
            preserved = cell.OuterXml;
        }
        var bytes = buffer.ToArray();
        var service = ExcelService.Create();
        var imported = await service.ImportAsync(new MemoryStream(bytes), true, default);
        Assert.Empty(imported.Errors);
        Assert.Equal(new[] { "Sheet1", "Visible" }, imported.Texts);
        Assert.Contains(imported.Metadata.Skipped, s => s.Code == reason && s.UnitIndex is null && s.Count == 1);
        var output = await service.ExportAsync(new MemoryStream(bytes), ["Sheet1", "Translated"]);
        Assert.Empty(output.Errors);
        using var result = SpreadsheetDocument.Open(new MemoryStream(output.Content!), false);
        var cells = result.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<S.Cell>().ToArray();
        Assert.Equal(preserved, cells[0].OuterXml);
        Assert.Equal("Translated", cells[1].InnerText);
    }

    /// <summary>
    /// Checks duplicate coordinates fail before ambiguous mappings are returned.
    /// </summary>
    /// <returns>Task completing after fatal source assertions.</returns>
    [Fact]
    public async Task DuplicateCellAddresses_AreFatal()
    {
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreateExcelWithInlineStrings([["One", "Two"]]));
        using (var document = SpreadsheetDocument.Open(buffer, true))
            document.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<S.Cell>().Last().CellReference = "A1";
        var result = await ExcelService.Create().ImportAsync(new MemoryStream(buffer.ToArray()));
        Assert.Equal("invalid_office_package", Assert.Single(result.Errors).Code);
        Assert.Equal("failed", result.Metadata.Status);
    }

    /// <summary>
    /// Checks invalid typed source values remain fatal source errors rather than recoverable skips.
    /// </summary>
    /// <returns>Task completing after invalid source assertions.</returns>
    [Fact]
    public async Task InvalidSourceScalar_IsFatal()
    {
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreateExcelWithInlineStrings([["One"]]));
        using (var document = SpreadsheetDocument.Open(buffer, true))
            document.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<S.Row>().Single().SetAttribute(new("", "r", "", "bad"));
        var result = await ExcelService.Create().ImportAsync(new MemoryStream(buffer.ToArray()));
        Assert.Equal("invalid_office_package", Assert.Single(result.Errors).Code);
        Assert.Equal("failed", result.Metadata.Status);
    }

    /// <summary>
    /// Checks singleton services keep request selections and skip metadata independent.
    /// </summary>
    /// <returns>Task completing after concurrent request and stable serialization assertions.</returns>
    [Fact]
    public async Task ConcurrentSelections_DoNotShareMetadata()
    {
        var bytes = OfficeFixtureFactory.CreateSelectionWorkbook();
        ISelectableFileHandler<ExcelSelection> service = ExcelService.Create();
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(index => Task.Run(async () =>
            await service.ImportAsync(new MemoryStream(bytes), new ExcelSelection([index % 2 == 0 ? "7" : "42"]), default))));
        for (var index = 0; index < results.Length; index++)
        {
            Assert.Empty(results[index].Errors);
            Assert.Equal(index % 2 == 0 ? new[] { "First", "One" } : new[] { "Secret", "Three" }, results[index].Texts);
            Assert.Equal(JsonSerializer.Serialize(results[index % 2].Metadata), JsonSerializer.Serialize(results[index].Metadata));
        }
    }

    /// <summary>
    /// Checks unreferenced stories are reported once and retain complete source content.
    /// </summary>
    /// <returns>Task completing after story metadata and output preservation assertions.</returns>
    [Fact]
    public async Task WordUnreferencedStories_AreReportedAndPreserved()
    {
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreateWordDocumentWithElements(OfficeFixtureFactory.WordParagraph("Visible")));
        using (var document = WordprocessingDocument.Open(buffer, true))
        {
            document.MainDocumentPart!.AddNewPart<HeaderPart>().Header = new(OfficeFixtureFactory.WordParagraph("Unused header"));
            document.MainDocumentPart.AddNewPart<FootnotesPart>().Footnotes = new(new W.Footnote(OfficeFixtureFactory.WordParagraph("Unused note")) { Id = 5 });
        }
        var bytes = buffer.ToArray();
        var service = WordService.Create();
        var imported = await service.ImportAsync(new MemoryStream(bytes), true, default);
        Assert.Empty(imported.Errors);
        Assert.Equal(new[] { "Visible" }, imported.Texts);
        Assert.Equal(2, imported.Metadata.Skipped.Count(s => s.Code == "unreferenced_story"));
        Assert.Equal("success", imported.Metadata.Status);
        var output = await service.ExportAsync(new MemoryStream(bytes), ["Translated"]);
        Assert.Empty(output.Errors);
        using var result = WordprocessingDocument.Open(new MemoryStream(output.Content!), false);
        Assert.Equal("Unused header", result.MainDocumentPart!.HeaderParts.Single().Header!.InnerText);
        Assert.Equal("Unused note", result.MainDocumentPart.FootnotesPart!.Footnotes!.InnerText);
    }

    /// <summary>
    /// Checks a late batch quota failure remains fatal despite earlier recoverable inputs.
    /// </summary>
    /// <param name="format">Source format identifier.</param>
    /// <returns>Task completing after quota and metadata assertions.</returns>
    [Theory]
    [InlineData("markdown")]
    [InlineData("plaintext")]
    [InlineData("word")]
    [InlineData("excel")]
    [InlineData("powerpoint")]
    public async Task FatalQuota_WinsAfterManyInvalidTranslations(string format)
    {
        var (service, bytes) = Source(format, Enumerable.Repeat("Source", 103).ToArray());
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        var translations = imported.Texts.Select(_ => "").ToArray();
        translations[^1] = new string('x', 100001);
        var output = await service.ExportAsync(new MemoryStream(bytes), translations);
        Assert.Null(output.Content);
        Assert.Contains(output.Errors, e => e.Code == "translation_too_long");
        Assert.Equal("failed", output.Metadata.Status);
        Assert.Equal(imported.Texts.Count, output.Metadata.UnitCount);
    }

    /// <summary>
    /// Checks missing and duplicate native IDs are source failures in discovery and import.
    /// </summary>
    /// <param name="duplicate">Whether to duplicate an ID rather than omit it.</param>
    /// <returns>Task completing after fatal source assertions.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeIds_AreNeverSynthesized(bool duplicate)
    {
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreateSelectionWorkbook());
        using (var document = SpreadsheetDocument.Open(buffer, true))
            document.WorkbookPart!.Workbook!.Sheets!.Elements<S.Sheet>().ElementAt(1).SheetId = duplicate ? 7U : null;
        var bytes = buffer.ToArray();
        var service = ExcelService.Create();
        var discovery = await service.GetSheetsAsync(new MemoryStream(bytes));
        Assert.Equal("invalid_office_package", Assert.Single(discovery.Errors).Code);
        Assert.Empty(discovery.Sheets);
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Equal("invalid_office_package", Assert.Single(imported.Errors).Code);
        Assert.Empty(imported.Texts);
    }

    /// <summary>
    /// Checks schema-valid charts are preserved while independent text translates.
    /// </summary>
    /// <param name="excel">Whether source is Excel rather than PowerPoint.</param>
    /// <returns>Task completing after schema, skip and output assertions.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedChart_PreservesChartAndTranslatesText(bool excel)
    {
        var bytes = OfficeFixtureFactory.CreateChartWithText(excel);
        using (OpenXmlPackage package = excel ? SpreadsheetDocument.Open(new MemoryStream(bytes), false) : PresentationDocument.Open(new MemoryStream(bytes), false))
            Assert.Empty(new DocumentFormat.OpenXml.Validation.OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2019).Validate(package));
        IFileHandler service = excel ? ExcelService.Create() : PowerPointService.Create();
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Empty(imported.Errors);
        Assert.Contains(imported.Metadata.Skipped, s => s.Code == "unsupported_graphic_frame" && s.Severity == "warning");
        var translations = imported.Texts.ToArray();
        translations[^1] = "Changed";
        if (excel) translations[0] = "Renamed";
        var output = await service.ExportAsync(new MemoryStream(bytes), translations);
        Assert.Empty(output.Errors);
        var reimported = await service.ImportAsync(new MemoryStream(output.Content!));
        Assert.Equal("Changed", reimported.Texts[^1]);
        if (excel)
        {
            using var document = SpreadsheetDocument.Open(new MemoryStream(output.Content!), false);
            Assert.Equal("'Renamed'!$A$1", document.WorkbookPart!.WorksheetParts.Single().DrawingsPart!.ChartParts.Single().ChartSpace!
                .Descendants<DocumentFormat.OpenXml.Drawing.Charts.Formula>().Single().Text);
        }
    }

    /// <summary>
    /// Checks source schema findings are admitted only inside identified preserved subtrees.
    /// </summary>
    /// <returns>Task completing after scoped schema and output assertions.</returns>
    [Fact]
    public async Task PreservedBaseline_DoesNotDisableSchemaValidationForEntirePart()
    {
        var bytes = OfficeFixtureFactory.CreateWordDocumentWithElements(new W.Paragraph(new W.Run(new W.Ruby())), OfficeFixtureFactory.WordParagraph("Visible"));
        var service = WordService.Create();
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Empty(imported.Errors);
        Assert.Equal(new[] { "Visible" }, imported.Texts);
        var output = await service.ExportAsync(new MemoryStream(bytes), ["Changed"]);
        Assert.Empty(output.Errors);
        using var buffer = new MemoryStream();
        buffer.Write(bytes);
        using (var document = WordprocessingDocument.Open(buffer, true))
            document.MainDocumentPart!.Document!.Body!.Append(new W.Run(new W.Text("Invalid body child")));
        var invalid = await service.ImportAsync(new MemoryStream(buffer.ToArray()));
        Assert.Contains(invalid.Errors, e => e.Code == "invalid_office_package");
        Assert.Equal("failed", invalid.Metadata.Status);
    }

    /// <summary>
    /// Checks debug mapping is opt-in per invocation without changing texts, skips or identity export.
    /// </summary>
    /// <param name="format">Source format identifier.</param>
    /// <returns>Task completing after concurrent service and preservation assertions.</returns>
    [Theory]
    [InlineData("markdown")]
    [InlineData("plaintext")]
    [InlineData("word")]
    [InlineData("excel")]
    [InlineData("powerpoint")]
    public async Task DebugMapping_IsOptionalAndIsolatedAcrossConcurrentImports(string format)
    {
        var (service, bytes) = Source(format, ["One", "Two"]);
        bytes = format switch
        {
            "excel" => OfficeFixtureFactory.CreateSelectionWorkbook(),
            "powerpoint" => OfficeFixtureFactory.CreateSelectionPresentation(),
            "markdown" => Encoding.UTF8.GetBytes("One\n\n```text\nProtected\n```\n\nTwo"),
            _ => bytes
        };
        using var input = new MemoryStream(bytes);
        var baseline = await service.ImportAsync(input);
        Assert.Empty(baseline.Errors);
        Assert.Null(baseline.Metadata.Units);
        Assert.DoesNotContain(baseline.Metadata.Skipped, skip => skip.Severity == "info");
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(index => Task.Run(async () =>
        {
            using var source = new MemoryStream(bytes);
            return await service.ImportAsync(source, index % 2 == 0, default);
        })));
        for (var index = 0; index < results.Length; index++)
        {
            var result = results[index];
            Assert.Empty(result.Errors);
            Assert.Equal(baseline.Texts, result.Texts);
            Assert.Equal(JsonSerializer.Serialize(baseline.Metadata), JsonSerializer.Serialize(result.Metadata with { Units = null, Skipped = result.Metadata.Skipped.Where(skip => skip.Severity != "info").ToArray() }));
            if (index % 2 == 0)
            {
                Assert.NotNull(result.Metadata.Units);
                if (format is "excel" or "powerpoint" or "markdown") Assert.Contains(result.Metadata.Skipped, skip => skip.Severity == "info");
                Assert.Equal(result.Texts.Count, result.Metadata.Units.Count);
                Assert.Equal(Enumerable.Range(0, result.Texts.Count), result.Metadata.Units.Select(unit => unit.Index));
            }
            else
            {
                Assert.Null(result.Metadata.Units);
                Assert.DoesNotContain(result.Metadata.Skipped, skip => skip.Severity == "info");
            }
        }
        using var exportSource = new MemoryStream(bytes);
        var exported = await service.ExportAsync(exportSource, baseline.Texts);
        Assert.Empty(exported.Errors);
        Assert.Null(exported.Metadata.Units);
        Assert.Equal(bytes, exported.Content);
    }

    /// <summary>
    /// Checks debug mapping follows explicit hidden selection without suppressing skipped objects.
    /// </summary>
    /// <param name="format">Selectable source format.</param>
    /// <returns>Task completing after selection and mapping assertions.</returns>
    [Theory]
    [InlineData("excel")]
    [InlineData("powerpoint")]
    public async Task DebugMapping_UsesExplicitSelection(string format)
    {
        var bytes = format == "excel" ? OfficeFixtureFactory.CreateSelectionWorkbook() : OfficeFixtureFactory.CreateSelectionPresentation();
        var selectedId = format == "excel" ? "42" : "900";
        var excel = ExcelService.Create();
        var powerpoint = PowerPointService.Create();
        using var first = new MemoryStream(bytes);
        using var second = new MemoryStream(bytes);
        var ordinary = format == "excel"
            ? await excel.ImportAsync(first, new ExcelSelection([selectedId]), false, default)
            : await powerpoint.ImportAsync(first, new PowerPointSelection([selectedId]), false, default);
        var diagnostic = format == "excel"
            ? await excel.ImportAsync(second, new ExcelSelection([selectedId]), true, default)
            : await powerpoint.ImportAsync(second, new PowerPointSelection([selectedId]), true, default);
        Assert.Empty(ordinary.Errors);
        Assert.Empty(diagnostic.Errors);
        Assert.Null(ordinary.Metadata.Units);
        Assert.Equal(format == "excel" ? new[] { "Secret", "Three" } : new[] { "Hidden text" }, ordinary.Texts);
        Assert.Equal(ordinary.Texts, diagnostic.Texts);
        Assert.Empty(ordinary.Metadata.Skipped);
        Assert.NotEmpty(diagnostic.Metadata.Skipped);
        Assert.All(diagnostic.Metadata.Skipped, skip => Assert.Equal("info", skip.Severity));
        Assert.Equal(JsonSerializer.Serialize(ordinary.Metadata), JsonSerializer.Serialize(diagnostic.Metadata with { Units = null, Skipped = diagnostic.Metadata.Skipped.Where(skip => skip.Severity != "info").ToArray() }));
        Assert.NotNull(diagnostic.Metadata.Units);
        Assert.Equal(ordinary.Texts.Count, diagnostic.Metadata.Units.Count);
        Assert.All(diagnostic.Metadata.Units, unit => Assert.Equal(selectedId, format == "excel" ? unit.Location.SheetId : unit.Location.SlideId));
    }

    /// <summary>
    /// Checks public metadata and exact identity for every supported format.
    /// </summary>
    /// <param name="format">Source format identifier.</param>
    /// <returns>Task completing after serialization and byte assertions.</returns>
    [Theory]
    [InlineData("markdown")]
    [InlineData("plaintext")]
    [InlineData("word")]
    [InlineData("excel")]
    [InlineData("powerpoint")]
    public async Task Metadata_ProjectsOnlyPublicFacts(string format)
    {
        var (service, bytes) = Source(format, ["One", "Two"]);
        var imported = await service.ImportAsync(new MemoryStream(bytes), true, default);
        Assert.Empty(imported.Errors);
        Assert.Equal(format, imported.Metadata.Format);
        Assert.Equal("success", imported.Metadata.Status);
        Assert.Equal(imported.Texts.Count, imported.Metadata.UnitCount);
        Assert.Equal(Enumerable.Range(0, imported.Texts.Count), imported.Metadata.Units!.Select(u => u.Index));
        var exported = await service.ExportAsync(new MemoryStream(bytes), imported.Texts);
        Assert.Empty(exported.Errors);
        Assert.Equal(bytes, exported.Content);
        Assert.Null(exported.Metadata.Units);
        foreach (var metadata in new[] { imported.Metadata, exported.Metadata })
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            foreach (var name in new[] { "schemaVersion", "operation", "sourceHash", "appliedUnitCount", "changedUnitCount", "skippedUnitCount", "paragraphCount", "hasFrontmatter", "hasBom" })
                Assert.False(json.RootElement.TryGetProperty(name, out _));
        }
    }

    /// <summary>
    /// Checks warnings beyond legacy error limit retain mapping and later valid translations.
    /// </summary>
    /// <param name="format">Source format identifier.</param>
    /// <returns>Task completing after complete skip and output assertions.</returns>
    [Theory]
    [InlineData("markdown")]
    [InlineData("plaintext")]
    [InlineData("word")]
    [InlineData("excel")]
    [InlineData("powerpoint")]
    public async Task PartialProcessing_DoesNotTruncateOrShiftUnits(string format)
    {
        var (service, bytes) = Source(format, Enumerable.Range(0, 103).Select(i => "Source " + i).ToArray());
        var imported = await service.ImportAsync(new MemoryStream(bytes), true, default);
        Assert.Empty(imported.Errors);
        var translated = imported.Texts.ToArray();
        var content = imported.Metadata.Units!.Where(u => u.Kind != "sheetName").ToArray();
        foreach (var unit in content.Take(102)) translated[unit.Index] = "";
        translated[content[^1].Index] = "Last translated";
        var result = await service.ExportAsync(new MemoryStream(bytes), translated);
        Assert.Empty(result.Errors);
        Assert.Equal("partial", result.Metadata.Status);
        Assert.Equal(102, result.Metadata.Skipped.Count(s => s.Stage == "translation"));
        Assert.Equal(content.Take(102).Select(u => (int?)u.Index), result.Metadata.Skipped.Where(s => s.Stage == "translation").Select(s => s.UnitIndex));
        var output = await service.ImportAsync(new MemoryStream(result.Content!));
        Assert.Equal("Source 0", output.Texts[content[0].Index]);
        Assert.Equal("Source 101", output.Texts[content[101].Index]);
        Assert.Equal("Last translated", output.Texts[content[^1].Index]);
    }

    /// <summary>
    /// Checks visible defaults, explicit hidden IDs, empty selection and chartsheet discovery.
    /// </summary>
    /// <returns>Task completing after inventory and preservation assertions.</returns>
    [Fact]
    public async Task ExcelSelection_UsesNativeSourceOrderAndPreservesAllSheets()
    {
        var bytes = OfficeFixtureFactory.CreateSelectionWorkbook();
        var service = ExcelService.Create();
        var discovered = await service.GetSheetsAsync(new MemoryStream(bytes));
        Assert.Empty(discovered.Errors);
        Assert.Equal(new[] { "7", "21", "42", "99", "140" }, discovered.Sheets.Select(s => s.SheetId));
        Assert.Equal("veryHidden", discovered.Sheets[2].State);
        Assert.Equal("chartsheet", discovered.Sheets[4].Kind);
        Assert.False(discovered.Sheets[4].CanImport);
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Empty(imported.Errors);
        Assert.Equal(new[] { "First", "One", "Empty" }, imported.Texts);
        Assert.Equal("partial", imported.Metadata.Status);
        var selection = new ExcelSelection(["42", "7", "42"]);
        var selected = await service.ImportAsync(new MemoryStream(bytes), selection, default);
        Assert.Equal(new[] { "First", "One", "Secret", "Three" }, selected.Texts);
        Assert.Equal(2, selected.Metadata.Sheets![2].UnitStartIndex);
        var output = await service.ExportAsync(new MemoryStream(bytes), ["First", "Changed", "Secret", "Hidden changed"], selection, default);
        Assert.Empty(output.Errors);
        Assert.All(output.Metadata.Sheets!, s => { Assert.Null(s.UnitStartIndex); Assert.Null(s.UnitEndIndex); });
        using var workbook = SpreadsheetDocument.Open(new MemoryStream(output.Content!), false);
        Assert.Equal(5, workbook.WorkbookPart!.Workbook!.Sheets!.Count());
        Assert.Equal(S.SheetStateValues.VeryHidden, workbook.WorkbookPart.Workbook!.Sheets!.Elements<S.Sheet>().ElementAt(2).State!.Value);
        var empty = await service.ImportAsync(new MemoryStream(bytes), new ExcelSelection([]), default);
        Assert.Empty(empty.Texts);
        var identity = await service.ExportAsync(new MemoryStream(bytes), [], new ExcelSelection([]), default);
        Assert.Equal(bytes, identity.Content);
        var unknown = await service.ImportAsync(new MemoryStream(bytes), new ExcelSelection(["404"]), default);
        Assert.Equal("unknown_selection_id", Assert.Single(unknown.Errors).Code);
        Assert.Equal(5, unknown.Metadata.Sheets!.Count);
        Assert.Equal("failed", unknown.Metadata.Status);
    }

    /// <summary>
    /// Checks discovery title semantics and explicit hidden-slide extraction.
    /// </summary>
    /// <returns>Task completing after source-order and visibility assertions.</returns>
    [Fact]
    public async Task PowerPointSelection_PreservesNativeIdsAndHiddenState()
    {
        var bytes = OfficeFixtureFactory.CreateSelectionPresentation();
        var service = PowerPointService.Create();
        var discovered = await service.GetSlidesAsync(new MemoryStream(bytes));
        Assert.Empty(discovered.Errors);
        Assert.Equal(new[] { "300", "900" }, discovered.Slides.Select(s => s.SlideId));
        Assert.Equal("Visible title", discovered.Slides[0].Title);
        Assert.Null(discovered.Slides[1].Title);
        Assert.True(discovered.Slides[1].Hidden);
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Equal(new[] { "Visible title" }, imported.Texts);
        var selected = await service.ImportAsync(new MemoryStream(bytes), new PowerPointSelection(["900", "300"]), default);
        Assert.Equal(new[] { "Visible title", "Hidden text" }, selected.Texts);
        var output = await service.ExportAsync(new MemoryStream(bytes), ["New title", "Changed hidden"], new PowerPointSelection(["900", "300"]), default);
        Assert.Empty(output.Errors);
        var inventory = await service.GetSlidesAsync(new MemoryStream(output.Content!));
        Assert.Equal("New title", inventory.Slides[0].Title);
        Assert.True(inventory.Slides[1].Hidden);
    }

    /// <summary>
    /// Checks locked inline content and cross-paragraph fields share exclusions across traversal.
    /// </summary>
    /// <returns>Task completing after precise path and source preservation assertions.</returns>
    [Fact]
    public async Task WordExclusions_PreserveFieldsAndLockedContentWhileTranslatingIndependentText()
    {
        var locked = new W.SdtRun(new W.SdtProperties(new W.Lock { Val = W.LockingValues.SdtContentLocked }),
            new W.SdtContentRun(new W.Run(new W.Text("Locked"))));
        var bytes = OfficeFixtureFactory.CreateWordDocumentWithElements(
            new W.Paragraph(new W.Run(new W.Text("Before")), locked),
            new W.Paragraph(new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Begin }), new W.Run(new W.FieldCode("DATE"))),
            new W.Paragraph(new W.Run(new W.Text("Cached")), new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.End })),
            OfficeFixtureFactory.WordParagraph("After"));
        var service = WordService.Create();
        var imported = await service.ImportAsync(new MemoryStream(bytes), true, default);
        Assert.Empty(imported.Errors);
        Assert.Equal(new[] { "<ox:r0>Before</ox:r0><ox:k0/>", "After" }, imported.Texts);
        Assert.Contains(imported.Metadata.Skipped, s => s.Code == "protected_content_control");
        Assert.Equal(2, imported.Metadata.Skipped.Count(s => s.Code == "cross_paragraph_field"));
        Assert.Contains("document[1]", imported.Metadata.Units![0].Location.Path);
        Assert.EndsWith("p[1]", imported.Metadata.Units[0].Location.Path);
        var output = await service.ExportAsync(new MemoryStream(bytes), ["<ox:r0>Changed</ox:r0><ox:k0/>", "Tail"]);
        Assert.Empty(output.Errors);
        using var document = WordprocessingDocument.Open(new MemoryStream(output.Content!), false);
        Assert.Equal(new[] { "Changed", "Locked", "Cached", "Tail" }, document.MainDocumentPart!.Document!.Descendants<W.Text>().Select(t => t.Text));
    }

    /// <summary>
    /// Creates equivalent independent text regions for each supported format.
    /// </summary>
    /// <param name="format">Source format identifier.</param>
    /// <param name="texts">Independent source texts.</param>
    /// <returns>Service and valid source bytes.</returns>
    private static (IFileHandler Service, byte[] Bytes) Source(string format, string[] texts) => format switch
    {
        "markdown" => (MarkdownService.Create(Options.Create(new FileHandlingOptions())), Encoding.UTF8.GetBytes(string.Join("\n\n", texts))),
        "plaintext" => (new PlainTextService(Options.Create(new FileHandlingOptions())), Encoding.UTF8.GetBytes(string.Join("\r\n\r\n", texts))),
        "word" => (WordService.Create(), OfficeFixtureFactory.CreateWordDocument(texts)),
        "excel" => (ExcelService.Create(), OfficeFixtureFactory.CreateExcelWithInlineStrings(texts.Select(t => new[] { t }).ToArray())),
        "powerpoint" => (PowerPointService.Create(), OfficeFixtureFactory.CreatePowerPointPresentation(texts)),
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
}
