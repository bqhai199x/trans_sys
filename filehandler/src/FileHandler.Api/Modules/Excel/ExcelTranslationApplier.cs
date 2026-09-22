using System.Text;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;
using S = DocumentFormat.OpenXml.Spreadsheet;
using A = DocumentFormat.OpenXml.Drawing;

namespace FileHandler.Api.Modules.Excel;

/// <summary>
/// Prepares and applies translated text units to Excel worksheet cells and Shared String Table.
/// </summary>
public sealed class ExcelTranslationApplier
{

    /// <summary>
    /// Prepares patch by analyzing changes and building modification masks.
    /// </summary>
    /// <param name="plan">Excel extraction plan.</param>
    /// <param name="decodedUnits">Decoded translations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Prepared patch.</returns>
    public ExcelPreparedPatch Prepare(
        ExcelPlan plan,
        IReadOnlyList<OfficeDecodedUnit> decodedUnits,
        CancellationToken cancellationToken)
    {
        var touchedParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < plan.Units.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var unit = plan.Units[i];
            var decoded = decodedUnits[i];
            if (unit.Kind == OfficeUnitKinds.SheetName) continue;

            if (OfficeTextBindings.Changed(unit, decoded))
            {
                touchedParts.Add(unit.Location.PartUri);
                foreach (var binding in unit.Bindings) touchedParts.Add(binding.TargetPartUri);
            }
        }

        var masks = new Dictionary<string, OfficeEditMask>(StringComparer.OrdinalIgnoreCase);
        var editsByPart = OfficeTextBindings.EditsByPart(plan.Units, decodedUnits);
        var unitParts = plan.Units.Select(u => u.Location.PartUri).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var partUri in touchedParts)
        {
            masks[partUri] = new OfficeEditMask(partUri, new[] { "//x:t", "//x:v", "//x:si", "//a:t" }, SstOptionalCountersRemoved: true);
            if (unitParts.Contains(partUri))
                masks[partUri] = masks[partUri] with { ScalarEdits = editsByPart[partUri] };
        }

        return new ExcelPreparedPatch(plan, decodedUnits, masks);
    }

    /// <summary>
    /// Applies translations to Excel workbook and writes modified parts to export session.
    /// </summary>
    /// <param name="session">Active export session.</param>
    /// <param name="doc">Open XML Spreadsheet document.</param>
    /// <param name="patch">Prepared patch containing translations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No return value.</returns>
    public void Apply(
        OfficeExportSession session,
        SpreadsheetDocument doc,
        ExcelPreparedPatch patch,
        CancellationToken cancellationToken)
    {
        var workbookPart = doc.WorkbookPart;
        if (workbookPart is null)
            return;

        var sstPart = workbookPart.SharedStringTablePart;
        var sst = sstPart?.SharedStringTable;
        ExcelSharedStringWriter? sstWriter = sst is not null ? new ExcelSharedStringWriter(sst) : null;
        var sstModified = false;

        var worksheetPartsByUri = workbookPart.WorksheetParts.ToDictionary(
            wp => "/" + wp.Uri.ToString().TrimStart('/'),
            StringComparer.OrdinalIgnoreCase);

        var neededCells = patch.Plan.Units.Where(u => u.Location.CellReference is not null && OfficeTextBindings.Changed(u, patch.DecodedUnits[u.Index]))
            .GroupBy(u => u.Location.PartUri).ToDictionary(g => g.Key, g => g.Select(u => u.Location.CellReference!).ToHashSet(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase);
        var cellsByPart = new Dictionary<string, Dictionary<string, Cell>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (partUri, addresses) in neededCells)
        {
            var cells = new Dictionary<string, Cell>(StringComparer.Ordinal);
            foreach (var cell in worksheetPartsByUri[partUri].Worksheet!.Descendants<Cell>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (cell.CellReference?.Value is not { } address || !addresses.Contains(address)) continue;
                if (!cells.TryAdd(address, cell)) throw new InvalidDataException("Duplicate cell coordinates prevent safe translation.");
            }
            if (cells.Count != addresses.Count) throw new InvalidDataException("Translation cell coordinates cannot be resolved.");
            cellsByPart.Add(partUri, cells);
        }
        var drawingParts = workbookPart.WorksheetParts.Select(w => w.DrawingsPart).OfType<DrawingsPart>()
            .Distinct().ToDictionary(p => "/" + p.Uri.ToString().TrimStart('/'), StringComparer.OrdinalIgnoreCase);
        var originalItems = sst?.Elements<SharedStringItem>().ToArray();
        var expectedAppends = new SortedDictionary<int, string>();
        var touchedParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < patch.Plan.Units.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var unit = patch.Plan.Units[i];
            var decoded = patch.DecodedUnits[i];
            if (unit.Kind == OfficeUnitKinds.SheetName) continue;

            if (!OfficeTextBindings.Changed(unit, decoded))
            {
                continue;
            }

            if (drawingParts.TryGetValue(unit.Location.PartUri, out var drawing))
            {
                OfficeTextBindings.Apply(drawing.WorksheetDrawing!, unit, decoded);
                touchedParts.Add(unit.Location.PartUri);
                continue;
            }

            if (worksheetPartsByUri.TryGetValue(unit.Location.PartUri, out var wsPart))
            {
                var worksheet = wsPart.Worksheet;
                if (worksheet is null)
                    continue;

                var cellRef = unit.Location.CellReference;
                if (string.IsNullOrEmpty(cellRef))
                    continue;

                cellsByPart[unit.Location.PartUri].TryGetValue(cellRef, out var cell);
                if (cell is null)
                    continue;

                if (cell.DataType?.Value == CellValues.SharedString && sstWriter is not null && sst is not null)
                {
                    var original = originalItems![int.Parse(cell.CellValue!.Text)];
                    var clone = (SharedStringItem)original.CloneNode(true);
                    OfficeTextBindings.ApplyClone(original, clone, unit, decoded);
                    var newIndex = sstWriter.ResolveOrAppend(sst, clone);
                    if (newIndex >= originalItems!.Length) expectedAppends[newIndex] = clone.OuterXml;
                    var mask = patch.EditMasks[unit.Location.PartUri];
                    var edits = (Dictionary<string, OfficeScalarEdit>)mask.ScalarEdits!;
                    edits.Add(OfficeTextBindings.Key(OfficeTextBindings.Path(cell.CellValue!)), new(
                        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(cell.CellValue!.Text))), newIndex.ToString()));
                    cell.CellValue = new CellValue(newIndex.ToString());
                    sstModified = true;
                    touchedParts.Add(unit.Location.PartUri);
                }
                else if (cell.DataType?.Value == CellValues.InlineString && cell.InlineString is not null)
                {
                    OfficeTextBindings.Apply(worksheet, unit, decoded);
                    touchedParts.Add(unit.Location.PartUri);
                }
            }
        }

        if (sstModified && sst is not null && sstPart is not null)
        {
            ExcelSharedStringWriter.CleanOptionalCounters(sst);
            var sstUri = "/" + sstPart.Uri.ToString().TrimStart('/');
            patch.EditMasks[sstUri] = patch.EditMasks[sstUri] with { AppendedXml = expectedAppends.Values.ToArray() };
            session.WritePart(sstUri, sst);
        }

        foreach (var partUri in touchedParts)
        {
            if (drawingParts.TryGetValue(partUri, out var drawing))
            {
                session.WritePart(partUri, drawing.WorksheetDrawing!);
            }
            if (worksheetPartsByUri.TryGetValue(partUri, out var wsPart) && wsPart.Worksheet is not null)
            {
                session.WritePart(partUri, wsPart.Worksheet);
            }
        }
    }
}
