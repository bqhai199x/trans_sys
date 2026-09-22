using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Excel;
using FileHandler.Api.Modules.Office;
using FileHandler.Api.Modules.Word;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace FileHandler.Tests.Modules.Office;

/// <summary>
/// Verifies source caching and compact diagnostics retain validation boundaries.
/// </summary>
public sealed class OfficeOptimizationTests
{

    /// <summary>
    /// Keeps cached source facts independent of caller-owned mutable byte arrays.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void SourceSnapshot_IsolatedFromInputAndReturnedBytes()
    {
        var bytes = OfficeFixtureFactory.CreateWordDocument("Original");
        using var source = new OfficeSource(bytes, "source", OfficeFormat.Word, new());
        var validator = new OfficePackageValidator(new());
        Assert.True(validator.ValidateSource(source, ["/word/document.xml"], default).IsValid);
        Array.Clear(bytes);
        var exposed = source.OriginalBytes;
        Array.Clear(exposed);
        using var document = WordprocessingDocument.Open(new MemoryStream(source.OriginalBytes), false);
        Assert.Equal("Original", document.MainDocumentPart!.Document!.Body!.InnerText);
        Assert.True(validator.ValidateOutput(source, Output(source.OriginalBytes), new Dictionary<string, OfficeEditMask>(), default).IsValid);
    }

    /// <summary>
    /// Checks cached and standalone validation both reject changed untouched payloads.
    /// </summary>
    /// <param name="prepareCache">Whether source validation and inspection precede output validation.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutputValidation_RejectsUntouchedMutation(bool prepareCache)
    {
        var bytes = OfficeFixtureFactory.CreateWordDocument("Original");
        using var source = new OfficeSource(bytes, "source", OfficeFormat.Word, new());
        var validator = new OfficePackageValidator(new());
        if (prepareCache)
        {
            new OfficePackageInspector(new()).Inspect(source, default);
            Assert.True(validator.ValidateSource(source, ["/word/document.xml"], default).IsValid);
        }
        using var buffer = new MemoryStream();
        buffer.Write(bytes);
        using (var document = WordprocessingDocument.Open(buffer, true))
            document.MainDocumentPart!.Document!.Descendants<W.Text>().Single().Text = "Tampered";
        var result = validator.ValidateOutput(source, Output(buffer.ToArray()), new Dictionary<string, OfficeEditMask>(), default);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "office_output_invalid");
    }

    /// <summary>
    /// Reapplies selection rules rather than treating a cached baseline as source acceptance.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void SourceValidation_RechecksSelectedScope()
    {
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreateWordDocument("Original"));
        string headerUri;
        using (var document = WordprocessingDocument.Open(buffer, true))
        {
            var header = document.MainDocumentPart!.AddNewPart<HeaderPart>();
            header.Header = new W.Header(new W.Run(new W.Text("Invalid direct child")));
            headerUri = header.Uri.ToString();
        }
        using var source = new OfficeSource(buffer.ToArray(), "source", OfficeFormat.Word, new());
        var validator = new OfficePackageValidator(new());
        Assert.True(validator.ValidateSource(source, [], default).IsValid);
        Assert.False(validator.ValidateSource(source, [headerUri], default).IsValid);
    }

    /// <summary>
    /// Reuses complete source findings without consuming them or bypassing stricter quotas.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void CachedBaseline_SurvivesRepeatedValidationAndStricterQuota()
    {
        var clean = OfficeFixtureFactory.CreateWordDocument("Original");
        using var buffer = new MemoryStream();
        buffer.Write(clean);
        using (var document = WordprocessingDocument.Open(buffer, true))
        {
            for (var index = 0; index < 2; index++)
            {
                var header = document.MainDocumentPart!.AddNewPart<HeaderPart>();
                header.Header = new W.Header(new W.Run(new W.Text("Invalid direct child")));
            }
        }
        using var source = new OfficeSource(buffer.ToArray(), "source", OfficeFormat.Word, new());
        var validator = new OfficePackageValidator(new());
        var masks = new Dictionary<string, OfficeEditMask>();
        Assert.True(validator.ValidateSource(source, [], default).IsValid);
        var unchanged = Output(source.OriginalBytes);
        Assert.True(validator.ValidateOutput(source, unchanged, masks, default).IsValid);
        Assert.True(validator.ValidateOutput(source, unchanged, masks, default).IsValid);
        var strict = new OfficePackageValidator(new() { MaxSchemaErrors = 1 });
        var rejected = strict.ValidateOutput(source, Output(clean), masks, default);
        Assert.False(rejected.IsValid);
        Assert.Equal("office_schema_limit_exceeded", Assert.Single(rejected.Errors).Code);
    }

    /// <summary>
    /// Preserves hidden field subtrees while continuing to reject invalid editable neighbors.
    /// </summary>
    /// <param name="debug">Whether diagnostic entries are requested.</param>
    /// <returns>Task completing after partial translation and invalid-neighbor assertions.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompactWordSkips_PreserveOnlyRecordedSubtrees(bool debug)
    {
        var bytes = OfficeFixtureFactory.CreateWordDocumentWithElements(new W.Paragraph(
            new W.SimpleField(new W.Run(new W.Text("Cached"))), new W.Run(new W.Text("Visible"))));
        var service = WordService.Create();
        var imported = await service.ImportAsync(new MemoryStream(bytes), debug, default);
        Assert.Empty(imported.Errors);
        Assert.Equal(1, imported.Metadata.SkipCount.Info);
        Assert.Equal(debug ? 1 : 0, imported.Metadata.Skipped.Count);
        var output = await service.ExportAsync(new MemoryStream(bytes), ["<ox:k0/><ox:r0>Translated</ox:r0>"]);
        Assert.Empty(output.Errors);
        Assert.Equal(1, output.Metadata.SkipCount.Info);
        using var document = WordprocessingDocument.Open(new MemoryStream(output.Content!), false);
        Assert.Equal("CachedTranslated", document.MainDocumentPart!.Document!.Body!.InnerText);
        using var invalid = new MemoryStream();
        invalid.Write(bytes);
        using (var changed = WordprocessingDocument.Open(invalid, true))
            changed.MainDocumentPart!.Document!.Body!.Append(new W.Run(new W.Text("Invalid neighbor")));
        var rejected = await service.ImportAsync(new MemoryStream(invalid.ToArray()), debug, default);
        Assert.Contains(rejected.Errors, error => error.Code == "invalid_office_package");
    }

    /// <summary>
    /// Retains totals when compact source skips are combined with translation and rename warnings.
    /// </summary>
    /// <returns>Task completing after combined metadata and output assertions.</returns>
    [Fact]
    public async Task CompactExcelSkips_CombineTranslationAndRenameWarnings()
    {
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreateExcelWithInlineStrings([["Original"]]));
        using (var document = SpreadsheetDocument.Open(buffer, true))
        {
            var row = document.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<S.Row>().Single();
            row.Append(new S.Cell(new S.CellFormula("INDIRECT(\"Sheet1!A1\")")) { CellReference = "B1" });
            row.Append(new S.Cell(new S.CellValue("123")) { CellReference = "C1", DataType = S.CellValues.Number });
        }
        var bytes = buffer.ToArray();
        var service = ExcelService.Create();
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Empty(imported.Errors);
        Assert.Empty(imported.Metadata.Skipped);
        Assert.Equal(new SkipCounts(0, 2), imported.Metadata.SkipCount);
        var output = await service.ExportAsync(new MemoryStream(bytes), ["Renamed", ""]);
        Assert.Empty(output.Errors);
        Assert.Equal(bytes, output.Content);
        Assert.Equal(new SkipCounts(2, 2), output.Metadata.SkipCount);
        Assert.Equal(2, output.Metadata.Skipped.Count);
    }

    /// <summary>
    /// Retains compact counts collected before an extraction quota failure.
    /// </summary>
    /// <returns>Task completing after failed-result metadata assertions.</returns>
    [Fact]
    public async Task CompactSkips_RetainCountsOnExtractionFailure()
    {
        using var buffer = new MemoryStream();
        buffer.Write(OfficeFixtureFactory.CreateExcelWithInlineStrings([["First", "Second", "Third"]]));
        using (var document = SpreadsheetDocument.Open(buffer, true))
        {
            var cell = document.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<S.Cell>().First();
            cell.RemoveAllChildren();
            cell.DataType = S.CellValues.Number;
            cell.CellValue = new("123");
        }
        var service = ExcelService.Create(Microsoft.Extensions.Options.Options.Create(new FileHandlingOptions { MaxUnits = 2 }));
        var result = await service.ImportAsync(new MemoryStream(buffer.ToArray()));
        Assert.Contains(result.Errors, error => error.Code == "too_many_units");
        Assert.Equal(new SkipCounts(0, 1), result.Metadata.SkipCount);
        Assert.Empty(result.Metadata.Skipped);
    }

    /// <summary>
    /// Creates validator input without relying on output digest fields.
    /// </summary>
    /// <param name="bytes">Candidate package bytes.</param>
    /// <returns>Output payload for independent validation.</returns>
    private static OfficeOutput Output(byte[] bytes) => new(bytes, "output", bytes.Length, "test");
}
