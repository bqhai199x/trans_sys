using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Modules.Excel;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace FileHandler.Tests.Modules.Excel;

/// <summary>
/// Verifies deterministic suffix allocation across repeated and overlapping names.
/// </summary>
public sealed class ExcelRenameCollisionTests
{

    /// <summary>
    /// Allocates repeated names without changing case, source order or occupied suffix behavior.
    /// </summary>
    /// <returns>Task completing after ordered workbook name assertions.</returns>
    [Fact]
    public async Task Rename_ResumesSuffixesAcrossCaseInsensitiveCollisions()
    {
        var names = Enumerable.Range(0, 120).Select(index => "Source" + index).Prepend("Report (2)").ToArray();
        var bytes = Workbook(names);
        var requested = names.Select((name, index) => index == 0 ? name : index % 2 == 0 ? "report" : "Report").ToArray();
        var output = await ExcelService.Create().ExportAsync(new MemoryStream(bytes), requested);
        Assert.Empty(output.Errors);
        using var result = SpreadsheetDocument.Open(new MemoryStream(output.Content!), false);
        var actual = result.WorkbookPart!.Workbook!.Sheets!.Elements<S.Sheet>().Select(sheet => sheet.Name!.Value!).ToArray();
        Assert.Equal("Report (2)", actual[0]);
        Assert.Equal("Report", actual[1]);
        for (var index = 2; index < actual.Length; index++) Assert.Equal(requested[index] + " (" + (index + 1) + ")", actual[index]);
        Assert.Equal(actual.Length, actual.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// Resolves collisions introduced by UTF-16 truncation and existing suffixed names.
    /// </summary>
    /// <returns>Task completing after exact truncated-name assertions.</returns>
    [Fact]
    public async Task Rename_TruncatedBasesShareReservedNamesSafely()
    {
        var prefix = new string('x', 25) + "😀";
        var first = prefix + "AAAA";
        var second = prefix + "BBBB";
        var bytes = Workbook(["A", "B", "C", "D", "E"]);
        var output = await ExcelService.Create().ExportAsync(new MemoryStream(bytes), [first, first, second, second, first]);
        Assert.Empty(output.Errors);
        using var result = SpreadsheetDocument.Open(new MemoryStream(output.Content!), false);
        Assert.Equal(new[] { first, prefix + " (2)", second, prefix + " (3)", prefix + " (4)" },
            result.WorkbookPart!.Workbook!.Sheets!.Elements<S.Sheet>().Select(sheet => sheet.Name!.Value));
    }

    /// <summary>
    /// Creates empty worksheets whose only translation units are sheet names.
    /// </summary>
    /// <param name="names">Ordered original worksheet names.</param>
    /// <returns>Schema-valid source workbook.</returns>
    private static byte[] Workbook(IReadOnlyList<string> names)
    {
        using var buffer = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new S.Workbook(new S.Sheets());
            for (var index = 0; index < names.Count; index++)
            {
                var sheet = workbook.AddNewPart<WorksheetPart>();
                sheet.Worksheet = new S.Worksheet(new S.SheetData());
                workbook.Workbook.Sheets!.Append(new S.Sheet { SheetId = (uint)(index + 1), Id = workbook.GetIdOfPart(sheet), Name = names[index] });
            }
        }
        return buffer.ToArray();
    }
}
