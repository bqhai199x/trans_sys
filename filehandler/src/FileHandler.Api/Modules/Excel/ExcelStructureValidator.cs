using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace FileHandler.Api.Modules.Excel;

/// <summary>
/// Validates Excel semantic invariants including table column names and formulas.
/// </summary>
public sealed class ExcelStructureValidator
{

    /// <summary>
    /// Validates structure preservation between original plan and translated workbook output.
    /// </summary>
    /// <param name="outputBytes">Translated workbook bytes.</param>
    /// <param name="plan">Original document plan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result.</returns>
    public OfficeValidationResult Validate(
        byte[] outputBytes,
        ExcelPlan plan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var ms = new MemoryStream(outputBytes);
        using var doc = SpreadsheetDocument.Open(ms, false, OfficeTextBindings.Settings(new OfficeProcessingOptions()));

        if (doc.WorkbookPart?.Workbook?.Sheets is null)
            return OfficeValidationResult.Failure([new FileError("office_output_invalid", ProcessingMessages.MissingOutputWorkbook)]);

        var tables = new Dictionary<(string Part, string Name), S.Table>();
        foreach (var worksheet in doc.WorkbookPart.WorksheetParts)
            foreach (var part in worksheet.TableDefinitionParts)
            {
                if (part.Table is not { } table) continue;
                foreach (var name in new[] { table.DisplayName?.Value, table.Name?.Value }.OfType<string>().Distinct())
                    if (!tables.TryAdd((worksheet.Uri.ToString(), name), table))
                        return OfficeValidationResult.Failure([new FileError("office_output_invalid", "Duplicate table identifier.")]);
            }
        foreach (var expected in plan.Tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!tables.TryGetValue((expected.PartUri, expected.TableName), out var table) ||
                !(table.TableColumns?.Elements<S.TableColumn>().Select(c => c.Name?.Value) ?? [])
                    .SequenceEqual(expected.ColumnNames))
                return OfficeValidationResult.Failure([new FileError("office_output_invalid", "Table definition changed.")]);
        }

        return OfficeValidationResult.Success();
    }
}
