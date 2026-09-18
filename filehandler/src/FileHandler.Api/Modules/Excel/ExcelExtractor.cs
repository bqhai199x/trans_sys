using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FileHandler.Api.Common;
using FileHandler.Api.Diagnostics;
using FileHandler.Api.Modules.Office;
using S = DocumentFormat.OpenXml.Spreadsheet;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;
using A = DocumentFormat.OpenXml.Drawing;

namespace FileHandler.Api.Modules.Excel;

/// <summary>
/// Extracts translation units, cells, and drawings from Excel workbooks.
/// </summary>
public sealed class ExcelExtractor : IExcelExtractor
{

    /// <summary>
    /// Text codec used for canonical unit encoding.
    /// </summary>
    private readonly OfficeTextCodec _codec;

    /// <summary>
    /// Excel table metadata reader.
    /// </summary>
    private readonly ExcelTableReader _tableReader;

    /// <summary>
    /// Configuration options governing limits.
    /// </summary>
    private readonly OfficeProcessingOptions _options;

    /// <summary>
    /// Creates Excel extractor instance.
    /// </summary>
    /// <param name="codec">Office text codec.</param>
    /// <param name="tableReader">Excel table reader.</param>
    /// <param name="options">Office processing options.</param>
    public ExcelExtractor(OfficeTextCodec codec, ExcelTableReader tableReader, OfficeProcessingOptions options)
    {
        _codec = codec;
        _tableReader = tableReader;
        _options = options;
    }

    /// <summary>
    /// Extracts bounded translation units and document topology.
    /// </summary>
    /// <param name="source">Immutable source snapshot.</param>
    /// <param name="inventory">Preflight package inventory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extraction plan.</returns>
    public ExcelPlan Analyze(OfficeSource source, OfficeInventory inventory, CancellationToken cancellationToken)
    {
        using var trace = DebugTrace.Enter("ExcelExtractor", "Analyze", () => new { sourceHash = source.SourceHash });

        try
        {
            trace.State("stage", () => "indexWorkbook");
            using var ms = new MemoryStream(source.OriginalBytes);
            using var doc = SpreadsheetDocument.Open(ms, false, OfficeTextBindings.Settings(_options));

            if (doc.WorkbookPart?.Workbook?.Sheets is null)
                throw new InvalidOperationException("Excel package missing workbook or sheets collection.");

            var sstPart = doc.WorkbookPart.SharedStringTablePart;
            var sstItems = sstPart?.SharedStringTable?.Elements<SharedStringItem>().ToList();

            var units = new OfficeUnitCollection(_options);
            var sheets = new List<ExcelSheetSnapshot>();
            var allTables = new List<ExcelTableSnapshot>();

            var hasCharts = inventory.Parts.Any(p =>
                p.ContentType.Contains("chartsheet", StringComparison.OrdinalIgnoreCase) ||
                p.ContentType.Contains("chart", StringComparison.OrdinalIgnoreCase) ||
                p.ContentType.Contains("diagram", StringComparison.OrdinalIgnoreCase)) ||
                doc.WorkbookPart.ChartsheetParts.Any() ||
                doc.WorkbookPart.WorksheetParts.Any(wp => wp.DrawingsPart?.ChartParts.Any() == true);

            if (hasCharts)
            {
                throw new InvalidOperationException("Excel package contains Chart or ChartSheet parts which are unsupported in v1.");
            }

            trace.State("stage", () => "selectSheets");
            var sheetIndex = 0;

            foreach (var sheet in doc.WorkbookPart.Workbook.Sheets.Elements<Sheet>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                sheetIndex++;

                var sheetName = sheet.Name?.Value ?? $"Sheet{sheetIndex}";
                var relId = sheet.Id?.Value;
                if (string.IsNullOrEmpty(relId) || !doc.WorkbookPart.TryGetPartById(relId, out var part))
                    continue;

                var isHidden = sheet.State?.Value == SheetStateValues.Hidden || sheet.State?.Value == SheetStateValues.VeryHidden;
                var partUri = "/" + part.Uri.ToString().TrimStart('/');
                var isWorksheet = part is WorksheetPart;

                using var sheetItem = DebugTrace.Item(sheetIndex);
                sheetItem.State("sheet", () => new
                {
                    id = relId,
                    name = sheetName,
                    uri = partUri,
                    sdkType = part.GetType().Name
                });

                if (isHidden || !isWorksheet)
                {
                    sheetItem.State("decision", () => "excluded");
                    sheets.Add(new ExcelSheetSnapshot(sheetName, partUri, isHidden ? "Hidden" : "OtherPart", 0));
                    continue;
                }

                sheetItem.State("decision", () => "translate");
                var worksheetPart = (WorksheetPart)part;
                var worksheet = worksheetPart.Worksheet;
                if (worksheet is null)
                    continue;

                var sheetTables = _tableReader.ReadTables(worksheetPart, partUri);
                allTables.AddRange(sheetTables);

                trace.State("stage", () => "walkCells");
                var sheetUnitsBefore = units.Count;
                var hiddenColumns = new bool[16385];
                var hiddenColumnChanges = new int[16386];
                var mergedCells = new ExcelMergeIndex(worksheet.Descendants<MergeCell>());
                foreach (var column in worksheet.Descendants<Column>().Where(c => c.Hidden?.Value == true))
                {
                    var first = Math.Max(1U, column.Min?.Value ?? 1);
                    var last = (int)Math.Min(16384U, column.Max?.Value ?? 0);
                    if (first > last) continue;
                    hiddenColumnChanges[first]++;
                    hiddenColumnChanges[last + 1]--;
                }
                var hiddenDepth = 0;
                for (var number = 1; number < hiddenColumns.Length; number++)
                {
                    hiddenDepth += hiddenColumnChanges[number];
                    hiddenColumns[number] = hiddenDepth > 0;
                }

                // Read rows in numeric order
                var rows = worksheet.GetFirstChild<SheetData>()?.Elements<Row>()
                    .OrderBy(r => r.RowIndex?.Value ?? 0U)
                    .ToList() ?? new List<Row>();

                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (row.Hidden?.Value == true)
                        continue; // Hidden row excluded

                    var cells = row.Elements<Cell>().ToList();
                    // Sort cells by numeric column
                    cells.Sort((a, b) =>
                    {
                        ExcelCellResolver.TryParseCoordinates(a.CellReference?.Value, out var c1, out _);
                        ExcelCellResolver.TryParseCoordinates(b.CellReference?.Value, out var c2, out _);
                        return c1.CompareTo(c2);
                    });

                    foreach (var cell in cells)
                    {
                        var cellRef = cell.CellReference?.Value;
                        if (string.IsNullOrEmpty(cellRef))
                            continue;

                        if (!ExcelCellResolver.TryParseCoordinates(cellRef, out var columnNumber, out var rowNumber) || columnNumber > 16384)
                            throw new InvalidOperationException("Invalid cell coordinates.");
                        if (hiddenColumns[columnNumber]) continue;

                        // Check if protected table identifier
                        if (ExcelTableReader.IsProtectedTableIdentifier(cellRef, sheetTables))
                            continue;

                        var cellText = ExcelCellResolver.ResolveCellString(cell, sstItems, out var runs);
                        if (cellText is null || string.IsNullOrWhiteSpace(cellText))
                            continue;
                        if (mergedCells.IsFollower(columnNumber, rowNumber))
                            throw new InvalidOperationException("Merged follower contains text outside visible merge owner.");

                        var unitId = OfficeIdentity.CreateUnitId(
                            "office-v1",
                            string.Empty,
                            OfficeFormat.Excel,
                            partUri,
                            OfficeObjectKind.SpreadsheetCell,
                            Array.Empty<OfficeElementPathSegment>(),
                            units.Count);

                        var loc = new OfficeLocation(partUri, Array.Empty<OfficeElementPathSegment>(), CellReference: cellRef);

                        var payload = cell.DataType?.Value == CellValues.SharedString
                            ? (OpenXmlElement)sstItems![int.Parse(cell.CellValue!.Text)]
                            : cell.InlineString!;
                        if (payload.Descendants<S.PhoneticRun>().Any() || payload.Descendants<S.PhoneticProperties>().Any())
                            throw new InvalidOperationException("Phonetic strings are unsupported.");
                        var builder = new OfficeTemplateBuilder(_options);
                        foreach (var text in payload.Descendants<S.Text>())
                            builder.Text(text, cell.DataType?.Value == CellValues.SharedString ? "/" + sstPart!.Uri.ToString().TrimStart('/') : partUri, text.Parent is S.Run run ? run.RunProperties?.OuterXml ?? "" : "");
                        var template = builder.Build()!;

                        var encodedSource = _codec.Encode(template);
                        var plainHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(cellText)));

                        var unit = new OfficeTranslationUnit(
                            units.Count,
                            unitId,
                            unitId,
                            loc,
                            template.Mode,
                            encodedSource,
                            template.Slots,
                            template.Anchors,
                            template.Bindings,
                            plainHash);

                        units.Add(unit);
                    }
                }

                trace.State("stage", () => "walkDrawings");
                if (worksheetPart.DrawingsPart?.WorksheetDrawing is not null)
                {
                    var drawingUri = "/" + worksheetPart.DrawingsPart.Uri.ToString().TrimStart('/');
                    var drawingLoc = new OfficeLocation(drawingUri, Array.Empty<OfficeElementPathSegment>());
                    var paraOrdinal = 0;

                    foreach (var p in worksheetPart.DrawingsPart.WorksheetDrawing.Descendants<A.Paragraph>())
                    {
                        paraOrdinal++;
                        var template = DrawingTextCodec.ReadParagraph(p, drawingLoc, paraOrdinal, _options);
                        if (template is not null)
                        {
                            var unitId = OfficeIdentity.CreateUnitId(
                                "office-v1",
                                string.Empty,
                                OfficeFormat.Excel,
                                drawingUri,
                                OfficeObjectKind.DrawingParagraph,
                                Array.Empty<OfficeElementPathSegment>(),
                                units.Count);

                            var plainSb = new StringBuilder();
                            foreach (var s in template.Slots) plainSb.Append(s.OriginalText);
                            var plainHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plainSb.ToString())));

                            var unit = new OfficeTranslationUnit(
                                units.Count,
                                unitId,
                                unitId,
                                drawingLoc,
                                template.Mode,
                                _codec.Encode(template),
                                template.Slots,
                                template.Anchors,
                                template.Bindings,
                                plainHash);

                            units.Add(unit);
                        }
                    }
                }

                sheets.Add(new ExcelSheetSnapshot(sheetName, partUri, "Visible", units.Count - sheetUnitsBefore));
            }

            if (units.Count > 0 && hasCharts)
            {
                throw new InvalidOperationException("Workbook contains Chart or SmartArt diagrams which are not supported for translation in office-v1.");
            }

            trace.State("stage", () => "buildUnits");
            long totalPlanChars = 0;
            foreach (var u in units)
                totalPlanChars += u.EncodedSource.Length;

            if (totalPlanChars > _options.MaxPlanChars)
                throw new FileLimitException("office_plan_limit_exceeded");

            if (units.Count > _options.MaxObjects || units.Any(u => u.Slots.Count + u.Anchors.Count > _options.MaxTokensPerUnit))
                throw new FileLimitException("office_plan_limit_exceeded");

            trace.Return(new { outcome = "success", unitCount = units.Count, sheetCount = sheets.Count, tableCount = allTables.Count });
            return new ExcelPlan(source.SourceHash, units, sheets, allTables);
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
