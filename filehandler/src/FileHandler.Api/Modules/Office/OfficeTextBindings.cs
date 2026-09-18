using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace FileHandler.Api.Modules.Office;

/// <summary>
/// Resolves immutable XML addresses and applies verified scalar bindings.
/// </summary>
internal static class OfficeTextBindings
{

    /// <summary>
    /// Per-root indexes released together with document trees.
    /// </summary>
    private static readonly ConditionalWeakTable<OpenXmlElement, Dictionary<OpenXmlElement, IReadOnlyList<OfficeElementPathSegment>>> Paths = new();

    /// <summary>
    /// Cached scalar lookup for each export tree.
    /// </summary>
    private static readonly ConditionalWeakTable<OpenXmlElement, Dictionary<string, OpenXmlElement>> Targets = new();

    /// <summary>
    /// Creates explicit read-only package settings.
    /// </summary>
    /// <param name="limits">Active XML limits.</param>
    /// <returns>Settings disabling automatic saves and compatibility rewriting.</returns>
    internal static OpenSettings Settings(OfficeProcessingOptions limits) => new()
    {
        AutoSave = false,
        MarkupCompatibilityProcessSettings = new MarkupCompatibilityProcessSettings(MarkupCompatibilityProcessMode.NoProcess, FileFormatVersions.Office2019),
        MaxCharactersInPart = limits.MaxXmlCharactersPerPart
    };

    /// <summary>
    /// Builds root-relative address using a single indexing pass per tree.
    /// </summary>
    /// <param name="element">Attached target element.</param>
    /// <returns>Exact address excluding part root.</returns>
    internal static IReadOnlyList<OfficeElementPathSegment> Path(OpenXmlElement element)
    {
        var root = element;
        while (root.Parent is not null) root = root.Parent;
        return Paths.GetValue(root, Index)[element];
    }

    /// <summary>
    /// Indexes child ordinals without repeated sibling scans.
    /// </summary>
    /// <param name="root">Root element.</param>
    /// <returns>Addresses keyed by element identity.</returns>
    private static Dictionary<OpenXmlElement, IReadOnlyList<OfficeElementPathSegment>> Index(OpenXmlElement root)
    {
        var result = new Dictionary<OpenXmlElement, IReadOnlyList<OfficeElementPathSegment>> { [root] = Array.Empty<OfficeElementPathSegment>() };
        var pending = new Stack<OpenXmlElement>();
        pending.Push(root);
        while (pending.TryPop(out var parent))
        {
            var counts = new Dictionary<(string, string), int>();
            foreach (var child in parent.ChildElements)
            {
                var key = (child.NamespaceUri, child.LocalName);
                counts.TryGetValue(key, out var ordinal);
                counts[key] = ++ordinal;
                result[child] = [.. result[parent], new(child.NamespaceUri, child.LocalName, ordinal)];
                pending.Push(child);
            }
        }
        return result;
    }

    /// <summary>
    /// Detects changes across every slot.
    /// </summary>
    /// <param name="unit">Source unit.</param>
    /// <param name="decoded">Validated translation.</param>
    /// <returns>True when any scalar changed.</returns>
    internal static bool Changed(OfficeTranslationUnit unit, OfficeDecodedUnit decoded) =>
        !unit.Slots.Select(s => s.OriginalText).SequenceEqual(decoded.DecodedSlots, StringComparer.Ordinal);

    /// <summary>
    /// Applies exact bindings after checking original scalar hashes.
    /// </summary>
    /// <param name="root">Target part root.</param>
    /// <param name="unit">Source unit with complete bindings.</param>
    /// <param name="decoded">Decoded slot values.</param>
    /// <returns>No return value.</returns>
    internal static void Apply(OpenXmlElement root, OfficeTranslationUnit unit, OfficeDecodedUnit decoded)
    {
        var index = Targets.GetValue(root, r => Paths.GetValue(r, Index).ToDictionary(p => Key(p.Value), p => p.Key, StringComparer.Ordinal));
        foreach (var (key, edit) in BuildChanges(unit, decoded, null))
        {
            if (!index.TryGetValue(key, out var target) || target is not OpenXmlLeafTextElement text ||
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.Text))) != edit.SourceHash)
                throw new InvalidOperationException("Source text binding mismatch.");
            text.Text = edit.Value;
            if (text is DocumentFormat.OpenXml.Wordprocessing.Text word && text.Text.Any(char.IsWhiteSpace))
                word.Space = SpaceProcessingModeValues.Preserve;
            if (text is DocumentFormat.OpenXml.Spreadsheet.Text cell && text.Text.Any(char.IsWhiteSpace))
                cell.Space = SpaceProcessingModeValues.Preserve;
        }
    }

    /// <summary>
    /// Applies rich string translations to a detached clone using source addresses.
    /// </summary>
    /// <param name="original">Attached source rich string.</param>
    /// <param name="clone">Detached clone with identical child order.</param>
    /// <param name="unit">Bound source unit.</param>
    /// <param name="decoded">Translated slots.</param>
    /// <returns>No return value.</returns>
    internal static void ApplyClone(OpenXmlElement original, OpenXmlElement clone, OfficeTranslationUnit unit, OfficeDecodedUnit decoded)
    {
        var prefixLength = Path(original).Count;
        var relative = unit with
        {
            Bindings = unit.Bindings.Select(b => b with
            {
                Location = b.Location with { ElementPath = b.Location.ElementPath.Skip(prefixLength).ToArray() }
            }).ToArray()
        };
        Apply(clone, relative, decoded);
    }

    /// <summary>
    /// Serializes an unambiguous address key.
    /// </summary>
    /// <param name="path">Root-relative address.</param>
    /// <returns>Length-prefixed address key.</returns>
    internal static string Key(IReadOnlyList<OfficeElementPathSegment> path) => string.Concat(path.Select(p => $"{p.NamespaceUri.Length}:{p.NamespaceUri}{p.LocalName.Length}:{p.LocalName}:{p.SiblingOrdinal}/"));

    /// <summary>
    /// Builds exact scalar expectations from changed slot bindings.
    /// </summary>
    /// <param name="units">Original ordered units.</param>
    /// <param name="decoded">Validated translations.</param>
    /// <param name="partUri">Target part URI.</param>
    /// <returns>Expected source hashes and translated scalar values.</returns>
    internal static IReadOnlyDictionary<string, OfficeScalarEdit> Edits(IReadOnlyList<OfficeTranslationUnit> units, IReadOnlyList<OfficeDecodedUnit> decoded, string partUri)
    {
        var edits = new Dictionary<string, OfficeScalarEdit>(StringComparer.Ordinal);
        for (var i = 0; i < units.Count; i++)
            foreach (var edit in BuildChanges(units[i], decoded[i], partUri))
                edits.Add(edit.Key, edit.Value);
        return edits;
    }

    /// <summary>
    /// Composes all changed spans per scalar while preserving unbound source characters.
    /// </summary>
    /// <param name="unit">Bound source unit.</param>
    /// <param name="decoded">Translated slots.</param>
    /// <param name="partUri">Optional part filter.</param>
    /// <returns>Exact scalar expectations.</returns>
    private static Dictionary<string, OfficeScalarEdit> BuildChanges(OfficeTranslationUnit unit, OfficeDecodedUnit decoded, string? partUri)
    {
        var replacements = new Dictionary<string, List<(OfficeTextBinding Binding, string Value)>>(StringComparer.Ordinal);
        var groups = unit.Bindings.ToLookup(b => b.EditGroupId, StringComparer.Ordinal);
        for (var slot = 0; slot < unit.Slots.Count; slot++)
        {
            if (unit.Slots[slot].OriginalText == decoded.DecodedSlots[slot]) continue;
            if (!groups.Contains(unit.Slots[slot].SlotId))
                throw new InvalidOperationException("Missing source slot binding.");
            var first = true;
            foreach (var binding in groups[unit.Slots[slot].SlotId])
            {
                if (partUri is not null && binding.TargetPartUri != partUri) continue;
                var key = Key(binding.Location.ElementPath);
                if (!replacements.TryGetValue(key, out var spans)) replacements[key] = spans = [];
                spans.Add((binding, first ? decoded.DecodedSlots[slot] : ""));
                first = false;
            }
        }
        var result = new Dictionary<string, OfficeScalarEdit>(StringComparer.Ordinal);
        foreach (var (key, spans) in replacements)
        {
            spans.Sort((a, b) => a.Binding.SpanOffset.CompareTo(b.Binding.SpanOffset));
            var original = spans[0].Binding.SourceValue ?? throw new InvalidOperationException("Missing source scalar.");
            var output = new StringBuilder();
            var offset = 0;
            foreach (var (binding, value) in spans)
            {
                if (binding.SpanOffset < offset || binding.SourceValue != original ||
                    binding.SpanOffset + binding.SpanLength > original.Length)
                    throw new InvalidOperationException("Overlapping or invalid scalar binding.");
                output.Append(original, offset, binding.SpanOffset - offset).Append(value);
                offset = binding.SpanOffset + binding.SpanLength;
            }
            output.Append(original, offset, original.Length - offset);
            result.Add(key, new(spans[0].Binding.OriginalValueHash, output.ToString()));
        }
        return result;
    }
}
