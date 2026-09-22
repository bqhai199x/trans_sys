using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FileHandler.Api.Common;
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
    public ExcelPlan Analyze(OfficeSource source, OfficeInventory inventory, CancellationToken cancellationToken) =>
        Analyze(source, inventory, new ExcelSelection(null), cancellationToken);

    /// <summary>
    /// Extracts selected source regions while recording preserved exclusions.
    /// </summary>
    /// <param name="source">Source snapshot.</param>
    /// <param name="inventory">Preflight inventory.</param>
    /// <param name="selection">Native identifiers to select.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Source mapping, inventory and exclusions.</returns>
    public ExcelPlan Analyze(OfficeSource source, OfficeInventory inventory, ExcelSelection selection, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream(source.Bytes);
        using var doc = SpreadsheetDocument.Open(ms, false, OfficeTextBindings.Settings(_options));

        if (doc.WorkbookPart?.Workbook?.Sheets is null)
            throw new InvalidDataException("Excel package missing workbook or sheets collection.");

        var sstPart = doc.WorkbookPart.SharedStringTablePart;
        var sstItems = sstPart?.SharedStringTable?.Elements<SharedStringItem>().ToList();

        var templates = new Dictionary<OpenXmlElement, (OfficeTextTemplate Template, string Encoded, string Hash)>(ReferenceEqualityComparer.Instance);
        var units = new OfficeUnitCollection(_options, _codec.MaxUnits);
        var sheets = new List<ExcelSheetSnapshot>();
        var allTables = new List<ExcelTableSnapshot>();

        var catalog = OfficeCatalog.Sheets(doc, cancellationToken).ToArray();
        var selectedIds = selection.SheetIds?.ToHashSet(StringComparer.Ordinal);
        if (selectedIds is not null && selectedIds.Except(catalog.Select(s => s.SheetId)).Any())
            throw new UnknownSelectionException(FileMetadata.Create("excel") with { Sheets = catalog });
        var skipped = new OfficeSkipCollector(source);
        source.ProcessingMetadata = FileMetadata.Create("excel") with { Sheets = catalog, Skipped = skipped };

        var sheetIndex = 0;

        foreach (var sheet in doc.WorkbookPart.Workbook.Sheets.Elements<Sheet>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            sheetIndex++;

            var descriptor = catalog[sheetIndex - 1];
            var selected = selectedIds?.Contains(descriptor.SheetId) ?? descriptor.State == SheetVisibility.Visible;
            descriptor = descriptor with { Selected = selected };
            catalog[sheetIndex - 1] = descriptor;
            var sheetName = descriptor.Name;
            var relId = sheet.Id?.Value;
            if (string.IsNullOrEmpty(relId) || !doc.WorkbookPart.TryGetPartById(relId, out var part))
                continue;

            var isHidden = sheet.State?.Value == SheetStateValues.Hidden || sheet.State?.Value == SheetStateValues.VeryHidden;
            var partUri = "/" + part.Uri.ToString().TrimStart('/');
            var isWorksheet = part is WorksheetPart;

            if (!selected || !isWorksheet)
            {
                if (!selected)
                    skipped.Info(SkipCodes.SheetNotSelected, SkipStage.Selection, SkipScope.Sheet, ProcessingMessages.SheetNotSelected, partUri, sheetId: descriptor.SheetId);
                else
                    skipped.Add(new(SkipCodes.UnsupportedSheet, SkipSeverity.Warning, SkipStage.Extraction, SkipScope.Sheet, 1,
                        ProcessingMessages.UnsupportedSheet, new(PartUri: partUri, SheetId: descriptor.SheetId)));
                sheets.Add(new ExcelSheetSnapshot(sheetName, partUri, isHidden ? "Hidden" : "OtherPart", 0));
                continue;
            }

            var worksheetPart = (WorksheetPart)part;
            var worksheet = worksheetPart.Worksheet;
            if (worksheet is null)
                throw new InvalidDataException("Missing worksheet root.");

            var sheetTables = _tableReader.ReadTables(worksheetPart, partUri);
            allTables.AddRange(sheetTables);
            var protectedCells = new ExcelProtectedCellIndex(sheetTables);

            var sheetUnitsBefore = units.Count;
            var nameLocation = new OfficeLocation("/" + doc.WorkbookPart.Uri.ToString().TrimStart('/'), OfficeTextBindings.Path(sheet))
            {
                SheetId = descriptor.SheetId,
                Root = new(doc.WorkbookPart.Workbook.NamespaceUri, doc.WorkbookPart.Workbook.LocalName, 1)
            };
            units.Add(new OfficeTranslationUnit(units.Count, "sheet:" + descriptor.SheetId, "sheet:" + descriptor.SheetId,
                nameLocation, UnitMode.Plain, sheetName, [new("r0", sheetName, "")], [], [], "")
            { Kind = OfficeUnitKinds.SheetName });
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
            var addresses = new HashSet<string>(StringComparer.Ordinal);

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var cell in row.Elements<Cell>())
                    if (cell.CellReference?.Value is { } address && !addresses.Add(address))
                        throw new InvalidDataException("Duplicate cell coordinates prevent safe extraction.");
                if (row.Hidden?.Value == true)
                {
                    skipped.Info(SkipCodes.HiddenRow, SkipStage.Extraction, SkipScope.Row, ProcessingMessages.HiddenRow,
                        partUri, sheetId: descriptor.SheetId, cellReference: row.RowIndex?.Value.ToString());
                    continue;
                }

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
                    cancellationToken.ThrowIfCancellationRequested();
                    var cellRef = cell.CellReference?.Value;
                    if (string.IsNullOrEmpty(cellRef))
                    {
                        skipped.Add(new(SkipCodes.ImplicitCellAddress, SkipSeverity.Warning, SkipStage.Extraction, SkipScope.Cell, 1,
                            ProcessingMessages.ImplicitCellAddress, OfficeMetadata.Location(OfficeMetadata.At(new(partUri, []) { SheetId = descriptor.SheetId }, cell))));
                        continue;
                    }

                    if (!ExcelCellResolver.TryParseCoordinates(cellRef, out var columnNumber, out var rowNumber) || columnNumber > 16384)
                        throw new InvalidDataException("Invalid cell coordinates.");
                    if (hiddenColumns[columnNumber])
                    {
                        skipped.Info(SkipCodes.HiddenColumn, SkipStage.Extraction, SkipScope.Cell, ProcessingMessages.HiddenColumn, partUri, sheetId: descriptor.SheetId, cellReference: cellRef);
                        continue;
                    }

                    // Check if protected table identifier
                    if (protectedCells.Contains(columnNumber, rowNumber))
                    {
                        skipped.Info(SkipCodes.ProtectedTableCell, SkipStage.Extraction, SkipScope.Cell, ProcessingMessages.ProtectedTableCell, partUri, sheetId: descriptor.SheetId, cellReference: cellRef);
                        continue;
                    }

                    if (ExcelCellResolver.HasFormula(cell))
                    {
                        skipped.Info(SkipCodes.FormulaCell, SkipStage.Extraction, SkipScope.Cell, ProcessingMessages.FormulaCell, partUri, sheetId: descriptor.SheetId, cellReference: cellRef);
                        continue;
                    }
                    OpenXmlElement? payload = null;
                    var shared = cell.DataType?.Value == CellValues.SharedString;
                    if (shared)
                    {
                        if (!int.TryParse(cell.CellValue?.Text, out var index) || sstItems is null || index < 0 || index >= sstItems.Count)
                            throw new InvalidDataException("Invalid shared string reference.");
                        payload = sstItems[index];
                    }
                    else if (cell.DataType?.Value == CellValues.InlineString) payload = cell.InlineString;
                    if (payload is null)
                    {
                        if (!string.IsNullOrEmpty(cell.InnerText))
                            skipped.Info(SkipCodes.NonTextCell, SkipStage.Extraction, SkipScope.Cell, ProcessingMessages.NonTextCell, partUri, sheetId: descriptor.SheetId, cellReference: cellRef);
                        continue;
                    }
                    if (!templates.TryGetValue(payload, out var cached))
                    {
                        var cellText = payload.InnerText;
                        if (string.IsNullOrWhiteSpace(cellText))
                        {
                            if (cellText.Length > 0) skipped.Info(SkipCodes.WhitespaceCell, SkipStage.Extraction, SkipScope.Cell, ProcessingMessages.WhitespaceCell, partUri, sheetId: descriptor.SheetId, cellReference: cellRef);
                            continue;
                        }
                        if (payload.Descendants<S.PhoneticRun>().Any() || payload.Descendants<S.PhoneticProperties>().Any())
                        {
                            var phoneticLocation = shared
                                ? OfficeMetadata.Location(OfficeMetadata.At(new("/" + sstPart!.Uri.ToString().TrimStart('/'), [], CellReference: cellRef) { SheetId = descriptor.SheetId }, payload))
                                : new SourceLocation(PartUri: partUri, SheetId: descriptor.SheetId, CellReference: cellRef);
                            skipped.Add(new(SkipCodes.PhoneticContent, SkipSeverity.Warning, SkipStage.Extraction, SkipScope.Cell, 1, ProcessingMessages.PhoneticContent, phoneticLocation));
                            continue;
                        }
                        var builder = new OfficeTemplateBuilder(_options);
                        foreach (var text in payload.Descendants<S.Text>())
                            builder.Text(text, shared ? "/" + sstPart!.Uri.ToString().TrimStart('/') : partUri,
                                text.Parent is S.Run run ? OfficeStyleFingerprint.Create(run.RunProperties) : "");
                        var built = builder.Build()!;
                        cached = (built, _codec.Encode(built), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(cellText))));
                        if (shared) templates.Add(payload, cached);
                    }
                    if (mergedCells.IsFollower(columnNumber, rowNumber))
                    {
                        skipped.Add(new(SkipCodes.MergedFollowerText, SkipSeverity.Warning, SkipStage.Extraction, SkipScope.Cell, 1,
                            ProcessingMessages.MergedFollowerText, new(PartUri: partUri, SheetId: descriptor.SheetId, CellReference: cellRef)));
                        continue;
                    }
                    var unitId = OfficeIdentity.CreateUnitId("office-v1", string.Empty, OfficeFormat.Excel,
                        partUri, OfficeObjectKind.SpreadsheetCell, Array.Empty<OfficeElementPathSegment>(), units.Count);
                    var loc = new OfficeLocation(partUri, Array.Empty<OfficeElementPathSegment>(), CellReference: cellRef) { SheetId = descriptor.SheetId };
                    var template = cached.Template;
                    var encodedSource = cached.Encoded;
                    var plainHash = cached.Hash;

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

            if (worksheetPart.DrawingsPart?.WorksheetDrawing is not null)
            {
                var drawingUri = "/" + worksheetPart.DrawingsPart.Uri.ToString().TrimStart('/');
                var drawingLoc = new OfficeLocation(drawingUri, Array.Empty<OfficeElementPathSegment>()) { SheetId = descriptor.SheetId };
                foreach (var frame in worksheetPart.DrawingsPart.WorksheetDrawing.Descendants<Xdr.GraphicFrame>())
                    skipped.Add(new(SkipCodes.UnsupportedGraphicFrame, SkipSeverity.Warning, SkipStage.Extraction, SkipScope.Shape, 1, ProcessingMessages.UnsupportedGraphicFrame,
                        OfficeMetadata.Location(OfficeMetadata.At(drawingLoc, frame))));
                var paraOrdinal = 0;

                foreach (var p in worksheetPart.DrawingsPart.WorksheetDrawing.Descendants<A.Paragraph>())
                {
                    if (p.Ancestors<Xdr.GraphicFrame>().Any()) continue;
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
                            OfficeMetadata.At(drawingLoc, p),
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

            catalog[sheetIndex - 1] = descriptor with { UnitStartIndex = sheetUnitsBefore, UnitEndIndex = units.Count };
            sheets.Add(new ExcelSheetSnapshot(sheetName, partUri, "Visible", units.Count - sheetUnitsBefore));
        }

        long totalPlanChars = 0;
        foreach (var u in units)
            totalPlanChars += u.EncodedSource.Length;

        if (totalPlanChars > _options.MaxPlanChars)
            throw new FileLimitException("office_plan_limit_exceeded");

        if (units.Count > _options.MaxObjects || units.Any(u => u.Slots.Count + u.Anchors.Count > _options.MaxTokensPerUnit))
            throw new FileLimitException("office_plan_limit_exceeded");

        return new ExcelPlan(source.SourceHash, units, sheets, allTables) { Metadata = OfficeMetadata.Describe("excel", units) with { Sheets = catalog, Skipped = skipped, Status = ProcessingStatus.Resolve(skipped) } };
    }
}
