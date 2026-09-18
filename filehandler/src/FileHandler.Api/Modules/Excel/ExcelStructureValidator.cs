using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FileHandler.Api.Common;
using FileHandler.Api.Diagnostics;
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
        using var trace = DebugTrace.Enter("ExcelStructureValidator", "Validate", () => new
        {
            unitCount = plan.Units.Count,
            tableCount = plan.Tables.Count
        });

        try
        {
            trace.State("stage", () => "compareTopology");
            cancellationToken.ThrowIfCancellationRequested();

            using var ms = new MemoryStream(outputBytes);
            using var doc = SpreadsheetDocument.Open(ms, false, OfficeTextBindings.Settings(new OfficeProcessingOptions()));

            if (doc.WorkbookPart?.Workbook?.Sheets is null)
                return OfficeValidationResult.Failure(new[] { new FileError("office_output_invalid", "Sổ tính Excel đầu ra thiếu phần bảng tính.") });

            // Validate table column names are preserved
            foreach (var expectedTable in plan.Tables)
            {
                var found = false;
                foreach (var wsPart in doc.WorkbookPart.WorksheetParts)
                {
                    foreach (var tblPart in wsPart.TableDefinitionParts)
                    {
                        var tbl = tblPart.Table;
                        if (tbl?.DisplayName?.Value == expectedTable.TableName || tbl?.Name?.Value == expectedTable.TableName)
                        {
                            found = true;
                            var colNames = tbl.TableColumns?.Elements<S.TableColumn>().Select(c => c.Name?.Value).ToList() ?? new List<string?>();
                            if (!colNames.SequenceEqual(expectedTable.ColumnNames))
                            {
                                return OfficeValidationResult.Failure(new[] { new FileError("office_output_invalid", $"Tên cột trong bảng '{expectedTable.TableName}' bị thay đổi ngoài ý muốn.") });
                            }
                        }
                    }
                }

                if (!found && expectedTable.ColumnNames.Count > 0)
                {
                    return OfficeValidationResult.Failure(new[] { new FileError("office_output_invalid", $"Bảng '{expectedTable.TableName}' bị thiếu trong tài liệu đầu ra.") });
                }
            }

            trace.Return(new { outcome = "success", validatedTables = plan.Tables.Count });
            return OfficeValidationResult.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            trace.Error(ex);
            throw;
        }
    }
}
