using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace FileHandler.Api.Modules.Excel;

/// <summary>
/// Plans simultaneous sheet renames and exact formula or hyperlink modifications.
/// </summary>
internal static class ExcelRenamePlanner
{

    /// <summary>
    /// Builds final names and strict reference edits before touching source XML.
    /// </summary>
    /// <param name="document">Read-only source package.</param>
    /// <param name="plan">Original translation mapping.</param>
    /// <param name="decoded">Effective decoded translations after unit fallback.</param>
    /// <param name="requested">Raw caller translations for reporting.</param>
    /// <param name="patch">Content patch receiving exact rename masks.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Rename edits and public outcomes.</returns>
    internal static ExcelRenamePlan Prepare(SpreadsheetDocument document, ExcelPlan plan,
        IReadOnlyList<OfficeDecodedUnit> decoded, IReadOnlyList<string> requested, ExcelPreparedPatch patch, CancellationToken token)
    {
        var catalog = plan.Metadata.Sheets!;
        var names = catalog.ToDictionary(s => s.SheetId, s => s.Name, StringComparer.Ordinal);
        var units = plan.Units.Where(u => u.Kind == "sheetName").ToArray();
        foreach (var unit in units)
        {
            var raw = decoded[unit.Index].DecodedSlots[0];
            if (raw != unit.EncodedSource) names[unit.Location.SheetId!] = Normalize(raw);
        }
        var changed = units.Where(u => names[u.Location.SheetId!] != u.EncodedSource).ToArray();
        var skipped = new List<SkipMetadata>();
        var edits = new List<ExcelRenameEdit>();
        if (changed.Length > 0)
        {
            var parts = Parts(document).ToArray();
            var references = new List<(OpenXmlPart Part, OpenXmlElement Element, string? Attribute, string Value)>();
            var safe = true;
            var blockedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var originalNames = catalog.ToDictionary(s => s.Name, s => s.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var part in parts)
            {
                token.ThrowIfCancellationRequested();
                if (part.ContentType.Contains("pivot", StringComparison.OrdinalIgnoreCase) || part.ContentType.Contains("externalLink", StringComparison.OrdinalIgnoreCase)) safe = false;
                if (part.RootElement is not { } root) continue;
                foreach (var element in root.Descendants())
                {
                    token.ThrowIfCancellationRequested();
                    if (element.LocalName == "extLst") safe = false;
                    var isFormula = element is OpenXmlLeafTextElement &&
                        (element.NamespaceUri == "http://schemas.openxmlformats.org/spreadsheetml/2006/main" &&
                            element.LocalName is "f" or "definedName" or "formula" or "formula1" or "formula2" or "calculatedColumnFormula" or "totalsRowFormula" ||
                         element.NamespaceUri == "http://schemas.openxmlformats.org/drawingml/2006/chart" && element.LocalName == "f");
                    if (isFormula)
                    {
                        if (ExcelFormulaReferences.TryRewrite(element.InnerText, originalNames, out _))
                            references.Add((part, element, null, element.InnerText));
                        else if (ExcelFormulaReferences.TryFindAffectedSheets(element.InnerText, catalog.Select(s => s.Name).ToArray(), out var affected))
                            blockedNames.UnionWith(affected);
                        else safe = false;
                    }
                    if (element is S.Hyperlink hyperlink && hyperlink.Location?.Value is { } location)
                    {
                        references.Add((part, element, "location", location));
                        if (!ExcelFormulaReferences.TryRewrite(location, originalNames, out _)) safe = false;
                    }
                }
            }
            if (!safe || blockedNames.Count > 0)
            {
                foreach (var unit in changed.Where(u => !safe || blockedNames.Contains(u.EncodedSource)))
                {
                    names[unit.Location.SheetId!] = unit.EncodedSource;
                    skipped.Add(new(SkipCodes.UnsafeSheetReference, SkipSeverity.Warning, SkipStage.Rename, SkipScope.Sheet, 1,
                        ProcessingMessages.UnsafeSheetReference, OfficeMetadata.Location(unit.Location), unit.Index));
                }
            }
            if (safe)
            {
                var changingIds = changed.Where(u => !blockedNames.Contains(u.EncodedSource)).Select(u => u.Location.SheetId!).ToHashSet(StringComparer.Ordinal);
                var reserved = catalog.Where(s => !changingIds.Contains(s.SheetId)).Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var sheet in catalog.Where(s => changingIds.Contains(s.SheetId)))
                {
                    var name = names[sheet.SheetId];
                    var candidate = name;
                    for (var suffix = 2; !reserved.Add(candidate); suffix++)
                    {
                        var tail = " (" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
                        candidate = Truncate(name, 31 - tail.Length) + tail;
                    }
                    names[sheet.SheetId] = candidate;
                }
                var replacements = catalog.ToDictionary(s => s.Name, s => names[s.SheetId], StringComparer.OrdinalIgnoreCase);
                foreach (var reference in references)
                {
                    if (!ExcelFormulaReferences.TryRewrite(reference.Value, replacements, out var replacement))
                        throw new InvalidOperationException("Reference safety changed during planning.");
                    if (replacement != reference.Value)
                        edits.Add(new(reference.Part, reference.Element, reference.Attribute, reference.Value, replacement));
                }
                foreach (var sheet in document.WorkbookPart!.Workbook!.Sheets!.Elements<S.Sheet>())
                {
                    var name = names[sheet.SheetId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)];
                    if (name != sheet.Name!.Value) edits.Add(new(document.WorkbookPart, sheet, "name", sheet.Name.Value!, name));
                }
            }
        }
        foreach (var group in edits.GroupBy(e => e.Part.Uri.ToString()))
        {
            if (!patch.EditMasks.TryGetValue(group.Key, out var mask)) mask = new(group.Key, [], ScalarEdits: new Dictionary<string, OfficeScalarEdit>());
            var scalars = new Dictionary<string, OfficeScalarEdit>(mask.ScalarEdits ?? new Dictionary<string, OfficeScalarEdit>(), StringComparer.Ordinal);
            var attributes = mask.AttributeEdits.ToList();
            foreach (var edit in group)
            {
                var path = OfficeTextBindings.Key(OfficeTextBindings.Path(edit.Element));
                if (edit.Attribute is not null) attributes.Add(new(path, "", edit.Attribute, edit.SourceValue, edit.Value));
                else scalars.Add(path, new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(edit.SourceValue))), edit.Value));
            }
            patch.EditMasks[group.Key] = mask with { ScalarEdits = scalars, AttributeEdits = attributes };
        }
        var changes = units.Where(u => requested[u.Index] != u.EncodedSource).Select(u => new SheetNameChange(
            u.Location.SheetId!, u.EncodedSource, requested[u.Index], names[u.Location.SheetId!])).ToArray();
        return new(edits, changes, skipped);
    }

    /// <summary>
    /// Applies verified edits and rewrites only affected part payloads.
    /// </summary>
    /// <param name="plan">Prepared rename edits.</param>
    /// <param name="session">Atomic export session.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>No return value.</returns>
    internal static void Apply(ExcelRenamePlan plan, OfficeExportSession session, CancellationToken token)
    {
        foreach (var edit in plan.Edits)
        {
            token.ThrowIfCancellationRequested();
            var current = edit.Attribute is null ? edit.Element.InnerText : edit.Element.GetAttribute(edit.Attribute, "").Value;
            if (current != edit.SourceValue) throw new InvalidOperationException("Rename source binding changed.");
            if (edit.Attribute is null) ((OpenXmlLeafTextElement)edit.Element).Text = edit.Value;
            else edit.Element.SetAttribute(new OpenXmlAttribute("", edit.Attribute, "", edit.Value));
        }
        foreach (var part in plan.Edits.Select(e => e.Part).Distinct()) session.WritePart(part.Uri.ToString(), part.RootElement!);
    }

    /// <summary>
    /// Normalizes changed names without splitting UTF-16 surrogate pairs.
    /// </summary>
    /// <param name="value">Nonempty valid Unicode translation.</param>
    /// <returns>Excel-compatible base name before collision suffixing.</returns>
    internal static string Normalize(string value)
    {
        var name = new string(value.Trim().Select(c => char.IsControl(c) || ":\\/?*[]".Contains(c) ? '_' : c).ToArray());
        name = TrimEdges(name);
        name = TrimEdges(Truncate(name, 31));
        if (name.Length == 0) name = "Sheet";
        if (name.Equals("History", StringComparison.OrdinalIgnoreCase)) name += "_";
        return Truncate(name, 31);
    }

    /// <summary>
    /// Removes whitespace and apostrophes exposed at either name boundary.
    /// </summary>
    /// <param name="value">Normalized or truncated name.</param>
    /// <returns>Name without prohibited edge apostrophes or surrounding whitespace.</returns>
    private static string TrimEdges(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && (char.IsWhiteSpace(value[start]) || value[start] == '\'')) start++;
        while (end > start && (char.IsWhiteSpace(value[end - 1]) || value[end - 1] == '\'')) end--;
        return value[start..end];
    }

    /// <summary>
    /// Truncates a valid Unicode name at a UTF-16 boundary.
    /// </summary>
    /// <param name="value">Name to shorten.</param>
    /// <param name="length">Maximum UTF-16 code units.</param>
    /// <returns>Original or shortened name.</returns>
    private static string Truncate(string value, int length)
    {
        if (value.Length <= length) return value;
        if (length > 0 && char.IsHighSurrogate(value[length - 1])) length--;
        return value[..length];
    }

    /// <summary>
    /// Visits every workbook dependency once, including unselected sheets.
    /// </summary>
    /// <param name="document">Source workbook package.</param>
    /// <returns>All reachable package parts.</returns>
    private static IEnumerable<OpenXmlPart> Parts(SpreadsheetDocument document)
    {
        var visited = new HashSet<OpenXmlPart>();
        var pending = new Stack<OpenXmlPart>(document.Parts.Select(p => p.OpenXmlPart));
        while (pending.TryPop(out var part))
        {
            if (!visited.Add(part)) continue;
            yield return part;
            foreach (var child in part.Parts) pending.Push(child.OpenXmlPart);
        }
    }
}

/// <summary>
/// Exact source-bound rename edit.
/// </summary>
/// <param name="Part">Owning package part.</param>
/// <param name="Element">Bound source element.</param>
/// <param name="Attribute">Attribute local name, or null for scalar text.</param>
/// <param name="SourceValue">Expected source value.</param>
/// <param name="Value">Planned output value.</param>
internal sealed record ExcelRenameEdit(OpenXmlPart Part, OpenXmlElement Element, string? Attribute, string SourceValue, string Value);

/// <summary>
/// Final rename edits and public outcomes.
/// </summary>
/// <param name="Edits">Verified source-bound modifications.</param>
/// <param name="Changes">Requested and effective names.</param>
/// <param name="Skipped">Renames retained from source.</param>
internal sealed record ExcelRenamePlan(IReadOnlyList<ExcelRenameEdit> Edits, IReadOnlyList<SheetNameChange> Changes, IReadOnlyList<SkipMetadata> Skipped);
