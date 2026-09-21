using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Common;
using S = DocumentFormat.OpenXml.Spreadsheet;
using P = DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace FileHandler.Api.Modules.Office;

/// <summary>
/// Reads native topology without invoking translation extraction.
/// </summary>
internal static class OfficeCatalog
{

    /// <summary>
    /// Reads workbook sheet order, native IDs, visibility and part types.
    /// </summary>
    /// <param name="document">Open source workbook.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Validated sheet inventory.</returns>
    internal static IReadOnlyList<SheetMetadata> Sheets(SpreadsheetDocument document, CancellationToken token)
    {
        var workbook = document.WorkbookPart ?? throw new InvalidDataException("Missing workbook.");
        var sheets = workbook.Workbook?.Sheets ?? throw new InvalidDataException("Missing sheet inventory.");
        var result = new List<SheetMetadata>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in sheets.Elements<S.Sheet>())
        {
            token.ThrowIfCancellationRequested();
            if (!uint.TryParse(sheet.SheetId?.InnerText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var nativeId) || nativeId == 0)
                throw new InvalidDataException("Invalid native sheet ID.");
            var id = nativeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!ids.Add(id) || string.IsNullOrEmpty(sheet.Name?.Value) || !names.Add(sheet.Name.Value) ||
                sheet.Id?.Value is not { } relation || !workbook.TryGetPartById(relation, out var part))
                throw new InvalidDataException("Invalid sheet inventory.");
            var state = sheet.State?.InnerText ?? SheetVisibility.Visible;
            if (state is not (SheetVisibility.Visible or SheetVisibility.Hidden or SheetVisibility.VeryHidden)) throw new InvalidDataException("Invalid sheet visibility.");
            result.Add(new(id, result.Count + 1, sheet.Name!.Value!, state, part is WorksheetPart ? "worksheet" : part is ChartsheetPart ? "chartsheet" : part.GetType().Name, part is WorksheetPart)
            {
                PartUri = "/" + part.Uri.ToString().TrimStart('/')
            });
        }
        return result;
    }

    /// <summary>
    /// Reads native slide order and title placeholders only.
    /// </summary>
    /// <param name="document">Open source presentation.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Validated slide inventory.</returns>
    internal static IReadOnlyList<SlideMetadata> Slides(PresentationDocument document, CancellationToken token)
    {
        var presentation = document.PresentationPart ?? throw new InvalidDataException("Missing presentation.");
        var slideList = presentation.Presentation?.SlideIdList ?? throw new InvalidDataException("Missing slide inventory.");
        var result = new List<SlideMetadata>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slide in slideList.Elements<P.SlideId>())
        {
            token.ThrowIfCancellationRequested();
            if (!uint.TryParse(slide.Id?.InnerText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var nativeId) || nativeId < 256 || nativeId >= 2147483648U)
                throw new InvalidDataException("Invalid native slide ID.");
            var id = nativeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!ids.Add(id) || slide.RelationshipId?.Value is not { } relation ||
                !presentation.TryGetPartById(relation, out var part) || part is not SlidePart slidePart || slidePart.Slide is null)
                throw new InvalidDataException("Invalid slide inventory.");
            var title = slidePart.Slide.Descendants<P.Shape>().FirstOrDefault(s =>
                s.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.GetFirstChild<P.PlaceholderShape>()?.Type?.Value is { } type &&
                (type == P.PlaceholderValues.Title || type == P.PlaceholderValues.CenteredTitle));
            result.Add(new(id, result.Count + 1, title is null ? null : string.Join("\n", title.Descendants<A.Paragraph>().Select(p => string.Concat(p.Descendants<A.Text>().Select(t => t.Text)))), slidePart.Slide.Show?.Value == false)
            {
                PartUri = "/" + slidePart.Uri.ToString().TrimStart('/')
            });
        }
        return result;
    }
}
