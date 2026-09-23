using FileHandler.Api.Common;
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
        Reordered(unit, decoded) || !unit.Slots.Select(s => s.OriginalText).SequenceEqual(decoded.DecodedSlots, StringComparer.Ordinal);

    /// <summary>
    /// Detects changed token order independently of scalar values.
    /// </summary>
    /// <param name="unit">Source unit.</param>
    /// <param name="decoded">Validated translation.</param>
    /// <returns>True when ordered token identities changed.</returns>
    internal static bool Reordered(OfficeTranslationUnit unit, OfficeDecodedUnit decoded) => decoded.Parts.Count > 0 &&
        !TranslationTokenParser.Parse(unit.EncodedSource).Select(p => p.Id).SequenceEqual(decoded.Parts.Select(p => p.Id));

    /// <summary>
    /// Applies exact bindings after checking original scalar hashes.
    /// </summary>
    /// <param name="root">Target part root.</param>
    /// <param name="unit">Source unit with complete bindings.</param>
    /// <param name="decoded">Decoded slot values.</param>
    /// <param name="mask">Exact region expectations for package validation.</param>
    /// <returns>No return value.</returns>
    internal static void Apply(OpenXmlElement root, OfficeTranslationUnit unit, OfficeDecodedUnit decoded, OfficeEditMask? mask = null)
    {
        var index = Targets.GetValue(root, r => Paths.GetValue(r, Index).ToDictionary(p => Key(p.Value), p => p.Key, StringComparer.Ordinal));
        if (Reordered(unit, decoded))
        {
            ApplyReordered(root, index, unit, decoded, mask);
            return;
        }
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
    /// Applies nested units before their owners and verifies complete outer reconstruction regions.
    /// </summary>
    /// <param name="root">Attached source part root.</param>
    /// <param name="translations">Changed units and validated translations belonging to this part.</param>
    /// <param name="mask">Part expectations receiving final reconstruction regions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No return value.</returns>
    internal static void ApplyNested(
        OpenXmlElement root,
        IReadOnlyList<(OfficeTranslationUnit Unit, OfficeDecodedUnit Decoded)> translations,
        OfficeEditMask mask,
        CancellationToken cancellationToken)
    {
        var index = Targets.GetValue(root, r => Paths.GetValue(r, Index).ToDictionary(p => Key(p.Value), p => p.Key, StringComparer.Ordinal));
        var regions = new Dictionary<string, (IReadOnlyList<OfficeElementPathSegment> Path, string Source)>();
        foreach (var (unit, decoded) in translations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Reordered(unit, decoded)) continue;
            var region = ReconstructionRegion(root, index, unit);
            var path = Path(region);
            regions.TryAdd(Key(path), (path, region.OuterXml));
        }
        var outerRegions = new Dictionary<string, (IReadOnlyList<OfficeElementPathSegment> Path, string Source)>();
        foreach (var (key, region) in regions.OrderBy(pair => pair.Value.Path.Count))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var covered = false;
            for (var depth = 0; depth < region.Path.Count && !covered; depth++)
                covered = outerRegions.ContainsKey(Key(region.Path.Take(depth).ToArray()));
            if (!covered) outerRegions.Add(key, region);
        }

        // Deeper unit bindings are consumed before an owner replaces its cloned descendants.
        foreach (var (unit, decoded) in translations.OrderByDescending(pair => pair.Unit.Location.ElementPath.Count))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Apply(root, unit, decoded);
        }
        foreach (var (key, region) in outerRegions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            mask.Regions.Add(key, (region.Source, Resolve(root, region.Path).OuterXml));
        }
    }

    /// <summary>
    /// Finds source container enclosing all scalar and protected bindings of one unit.
    /// </summary>
    /// <param name="root">Part or detached rich string root.</param>
    /// <param name="index">Immutable source address lookup.</param>
    /// <param name="unit">Complete source bindings.</param>
    /// <returns>Lowest container shared by all bound nodes.</returns>
    private static OpenXmlElement ReconstructionRegion(OpenXmlElement root, Dictionary<string, OpenXmlElement> index, OfficeTranslationUnit unit)
    {
        var nodes = unit.Bindings.Select(b => index[Key(b.Location.ElementPath)])
            .Concat(unit.Anchors.Where(a => a.Path is not null).Select(a => index[Key(a.Path!)])).ToArray();
        var region = nodes[0].Parent!;
        while (nodes.Any(n => !n.Ancestors().Contains(region))) region = region.Parent ?? root;
        return region;
    }

    /// <summary>
    /// Reconstructs one source container on a clone before committing changed order.
    /// </summary>
    /// <param name="root">Attached part or detached rich string root.</param>
    /// <param name="index">Immutable source address lookup.</param>
    /// <param name="unit">Complete source bindings.</param>
    /// <param name="decoded">Validated output parts.</param>
    /// <param name="mask">Optional exact region expectations.</param>
    /// <returns>No return value.</returns>
    private static void ApplyReordered(OpenXmlElement root, Dictionary<string, OpenXmlElement> index, OfficeTranslationUnit unit, OfficeDecodedUnit decoded, OfficeEditMask? mask)
    {
        var region = ReconstructionRegion(root, index, unit);
        var regionPath = Path(region);
        var clone = region.CloneNode(true);
        var originalXml = region.OuterXml;
        var relative = unit with
        {
            Bindings = unit.Bindings.Select(b => b with { Location = b.Location with { ElementPath = b.Location.ElementPath.Skip(regionPath.Count).ToArray() } }).ToArray()
        };
        Apply(clone, relative, decoded with { Parts = [] });
        var sourceParts = TranslationTokenParser.Parse(unit.EncodedSource);
        var fragmentRuns = new Dictionary<OpenXmlElement, OpenXmlElement>(ReferenceEqualityComparer.Instance);
        var sourceNodes = new Dictionary<string, OpenXmlElement>();
        foreach (var path in unit.Bindings.Select(b => b.Location.ElementPath).Concat(unit.Anchors.Where(a => a.Path is not null).Select(a => a.Path!)))
            sourceNodes[Key(path)] = Resolve(clone, path.Skip(regionPath.Count));
        foreach (var run in sourceNodes.Values.Select(n => n.Parent).OfType<OpenXmlElement>().Where(n => n.LocalName == "r").Distinct().ToArray())
        {
            var contents = run.ChildElements.Where(n => n.LocalName != "rPr").ToArray();
            if (contents.Length <= 1) continue;
            foreach (var content in contents)
            {
                var fragment = run.CloneNode(false);
                foreach (var properties in run.ChildElements.Where(n => n.LocalName == "rPr")) fragment.AppendChild(properties.CloneNode(true));
                fragment.AppendChild(content.CloneNode(true));
                fragmentRuns[content] = fragment;
                run.Parent!.InsertBefore(fragment, run);
            }
            run.Remove();
        }
        var atoms = new Dictionary<string, List<OpenXmlElement>>();
        foreach (var part in sourceParts)
        {
            var paths = part.Text is not null
                ? unit.Bindings.Where(b => b.EditGroupId == part.Id).Select(b => b.Location.ElementPath)
                : unit.Anchors.Where(a => a.AnchorId == part.Id && a.Path is not null).Select(a => a.Path!);
            var physical = new List<OpenXmlElement>();
            foreach (var path in paths)
            {
                var node = sourceNodes[Key(path)];
                if (fragmentRuns.TryGetValue(node, out var fragment)) node = fragment;
                else if (node.Parent?.LocalName == "r") node = node.Parent;
                if (!physical.Contains(node)) physical.Add(node);
            }
            atoms[part.Id] = physical;
        }
        var movable = sourceParts.Where(p => atoms[p.Id].Count > 0).SelectMany(p => atoms[p.Id]).Distinct().ToArray();
        foreach (var parent in movable.Select(n => n.Parent).OfType<OpenXmlElement>().Distinct().ToArray())
        {
            var originalChildren = parent.ChildElements.ToArray();
            // Multiple token spans can share one scalar containing fixed raw controls.
            var ordered = decoded.Parts.SelectMany(p => atoms[p.Id]).Where(n => n.Parent == parent).Distinct().ToArray();
            var replaceable = ordered.ToHashSet();
            var next = 0;
            var candidate = originalChildren.Select(n => replaceable.Contains(n) ? ordered[next++] : n).ToArray();
            foreach (var node in originalChildren) node.Remove();
            foreach (var node in candidate) parent.AppendChild(node);
        }
        if (mask is not null) mask.Regions.Add(Key(regionPath), (originalXml, clone.OuterXml));
        var originals = region.ChildElements.ToArray();
        foreach (var child in originals) child.Remove();
        foreach (var child in clone.ChildElements.ToArray()) { child.Remove(); region.AppendChild(child); }
    }

    /// <summary>
    /// Resolves a validated source-relative path on a clone.
    /// </summary>
    /// <param name="root">Cloned container.</param>
    /// <param name="path">Relative namespace-qualified address.</param>
    /// <returns>Resolved element.</returns>
    private static OpenXmlElement Resolve(OpenXmlElement root, IEnumerable<OfficeElementPathSegment> path)
    {
        foreach (var segment in path)
            root = root.ChildElements.Where(e => e.NamespaceUri == segment.NamespaceUri && e.LocalName == segment.LocalName).ElementAt(segment.SiblingOrdinal - 1);
        return root;
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
            }).ToArray(),
            Anchors = unit.Anchors.Select(a => a with { Path = a.Path?.Skip(prefixLength).ToArray() }).ToArray()
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
    /// Groups exact scalar expectations in one pass over changed units.
    /// </summary>
    /// <param name="units">Original ordered units.</param>
    /// <param name="decoded">Validated translations.</param>
    /// <returns>Editable part maps; shared strings are handled by copy-on-write.</returns>
    internal static Dictionary<string, Dictionary<string, OfficeScalarEdit>> EditsByPart(IReadOnlyList<OfficeTranslationUnit> units, IReadOnlyList<OfficeDecodedUnit> decoded)
    {
        var parts = new Dictionary<string, Dictionary<string, OfficeScalarEdit>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            if (unit.Kind == OfficeUnitKinds.SheetName || !Changed(unit, decoded[i])) continue;
            var uri = unit.Location.PartUri;
            if (!parts.TryGetValue(uri, out var edits)) parts[uri] = edits = new(StringComparer.Ordinal);
            if (Reordered(unit, decoded[i])) continue;
            foreach (var edit in BuildChanges(unit, decoded[i], uri)) edits.Add(edit.Key, edit.Value);
        }
        return parts;
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
        if (unit.Kind == OfficeUnitKinds.SheetName) return new(StringComparer.Ordinal);
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
