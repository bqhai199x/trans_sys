using System.Runtime.CompilerServices;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Modules.Office;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace FileHandler.Api.Modules.Word;

/// <summary>
/// Normalizes font hints only when resolved Latin and East Asian fonts agree.
/// </summary>
internal sealed class WordRunFingerprint
{

    /// <summary>
    /// Style indexes scoped to document lifetime.
    /// </summary>
    private static readonly ConditionalWeakTable<MainDocumentPart, WordRunFingerprint> Documents = new();

    /// <summary>
    /// Wordprocessing namespace for font attributes.
    /// </summary>
    private const string Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>
    /// Available styles indexed by source identifier.
    /// </summary>
    private readonly Dictionary<string, W.Style> _styles;

    /// <summary>
    /// Default run fonts before style overrides.
    /// </summary>
    private readonly W.RunFonts? _defaults;

    /// <summary>
    /// Default paragraph style identifier, when declared.
    /// </summary>
    private readonly string? _defaultParagraph;

    /// <summary>
    /// Default character style identifier, when declared.
    /// </summary>
    private readonly string? _defaultCharacter;

    /// <summary>
    /// Default table style identifier, when declared.
    /// </summary>
    private readonly string? _defaultTable;

    /// <summary>
    /// Resolved style chains, including unknown or cyclic failures.
    /// </summary>
    private readonly Dictionary<string, IReadOnlyList<W.Style>?> _chains = new(StringComparer.Ordinal);

    /// <summary>
    /// Indexes document style definitions without modifying package XML.
    /// </summary>
    /// <param name="main">Owning main document part.</param>
    private WordRunFingerprint(MainDocumentPart main)
    {
        var styles = main.StyleDefinitionsPart?.Styles;
        _styles = styles?.Elements<W.Style>().Where(s => s.StyleId?.Value is not null)
            .ToDictionary(s => s.StyleId!.Value!, StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
        _defaults = styles?.GetFirstChild<W.DocDefaults>()?.GetFirstChild<W.RunPropertiesDefault>()?
            .GetFirstChild<W.RunPropertiesBaseStyle>()?.GetFirstChild<W.RunFonts>();
        _defaultParagraph = _styles.Values.FirstOrDefault(s => s.Type?.Value == W.StyleValues.Paragraph && s.Default?.Value == true)?.StyleId?.Value;
        _defaultCharacter = _styles.Values.FirstOrDefault(s => s.Type?.Value == W.StyleValues.Character && s.Default?.Value == true)?.StyleId?.Value;
        _defaultTable = _styles.Values.FirstOrDefault(s => s.Type?.Value == W.StyleValues.Table && s.Default?.Value == true)?.StyleId?.Value;
    }

    /// <summary>
    /// Compares direct run formatting with conservative effective-font hint normalization.
    /// </summary>
    /// <param name="run">Attached source run, or null.</param>
    /// <returns>Formatting fingerprint retaining unresolved or meaningful hint differences.</returns>
    internal static string Create(W.Run? run)
    {
        var properties = run?.RunProperties;
        var fonts = properties?.GetFirstChild<W.RunFonts>();
        if (fonts?.Hint?.Value is not { } hint || hint != W.FontTypeHintValues.EastAsia && hint != W.FontTypeHintValues.Default)
            return OfficeStyleFingerprint.Create(properties);
        var root = run!.Ancestors<OpenXmlPartRootElement>().FirstOrDefault();
        if (root?.OpenXmlPart?.OpenXmlPackage is not WordprocessingDocument document || document.MainDocumentPart is not { } main ||
            !Documents.GetValue(main, part => new WordRunFingerprint(part)).EquivalentFonts(run))
            return OfficeStyleFingerprint.Create(properties);
        var normalized = (W.RunProperties)properties!.CloneNode(true);
        var normalizedFonts = normalized.GetFirstChild<W.RunFonts>()!;
        normalizedFonts.Hint = null;
        if (!normalizedFonts.HasAttributes && !normalizedFonts.HasChildren) normalizedFonts.Remove();
        return OfficeStyleFingerprint.Create(normalized);
    }

    /// <summary>
    /// Resolves font declarations through defaults, paragraph styles, character styles and direct properties.
    /// </summary>
    /// <param name="run">Attached source run.</param>
    /// <returns>True only when all hint-sensitive slots resolve to one explicit font name.</returns>
    private bool EquivalentFonts(W.Run run)
    {
        var paragraph = run.Ancestors<W.Paragraph>().FirstOrDefault();
        if (paragraph is null || paragraph.ParagraphProperties?.NumberingProperties is not null) return false;
        var fonts = new Dictionary<string, string>(StringComparer.Ordinal);
        // Table formatting is unknown here; require higher-precedence styles to resolve every slot.
        Overlay(fonts, _defaults);
        var tables = paragraph.Ancestors<W.Table>().ToArray();
        if (tables.Length > 0)
        {
            foreach (var slot in new[] { "ascii", "hAnsi", "eastAsia" }) fonts.Remove(slot);
            foreach (var table in tables)
            {
                var tableId = table.TableProperties?.TableStyle?.Val?.Value ?? _defaultTable;
                if (tableId is null) continue;
                var tableChain = Chain(tableId);
                if (tableChain is null || tableChain.SelectMany(s => s.Descendants<W.RunFonts>()).Any(f =>
                    f.Hint?.Value is { } tableHint && tableHint != W.FontTypeHintValues.Default && tableHint != W.FontTypeHintValues.EastAsia)) return false;
            }
        }
        var paragraphId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? _defaultParagraph;
        var characterId = run.RunProperties?.RunStyle?.Val?.Value ?? _defaultCharacter;
        foreach (var id in new[] { paragraphId, characterId })
        {
            if (id is null) continue;
            var chain = Chain(id);
            if (chain is null) return false;
            foreach (var style in chain)
            {
                if (style.StyleParagraphProperties?.NumberingProperties is not null) return false;
                Overlay(fonts, style.StyleRunProperties?.GetFirstChild<W.RunFonts>());
            }
        }
        if (fonts.TryGetValue("hint", out var inheritedHint) && inheritedHint is not ("default" or "eastAsia")) return false;
        Overlay(fonts, run.RunProperties?.GetFirstChild<W.RunFonts>());
        return fonts.TryGetValue("ascii", out var ascii) && ascii.StartsWith("font:", StringComparison.Ordinal) &&
            fonts.TryGetValue("hAnsi", out var ansi) && fonts.TryGetValue("eastAsia", out var eastAsia) &&
            ascii == ansi && ascii == eastAsia;
    }

    /// <summary>
    /// Resolves base styles in inheritance order and rejects missing or cyclic definitions.
    /// </summary>
    /// <param name="id">Requested style identifier.</param>
    /// <returns>Base-first style chain, or null when inheritance cannot be resolved.</returns>
    private IReadOnlyList<W.Style>? Chain(string id)
    {
        if (_chains.TryGetValue(id, out var cached)) return cached;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var chain = new List<W.Style>();
        string? current = id;
        while (current is not null)
        {
            if (!visited.Add(current) || !_styles.TryGetValue(current, out var style)) return _chains[id] = null;
            chain.Add(style);
            current = style.BasedOn?.Val?.Value;
        }
        chain.Reverse();
        return _chains[id] = chain;
    }

    /// <summary>
    /// Applies one font declaration layer with theme precedence within that layer.
    /// </summary>
    /// <param name="resolved">Accumulated effective slot identities.</param>
    /// <param name="fonts">Higher-precedence font declarations, or null.</param>
    /// <returns>No return value.</returns>
    private static void Overlay(Dictionary<string, string> resolved, W.RunFonts? fonts)
    {
        if (fonts is null) return;
        var attributes = fonts.GetAttributes().Where(a => a.NamespaceUri == Word).ToDictionary(a => a.LocalName, a => a.Value);
        var hint = attributes.GetValueOrDefault("hint");
        if (!string.IsNullOrEmpty(hint)) resolved["hint"] = hint;
        foreach (var slot in new[] { "ascii", "hAnsi", "eastAsia" })
        {
            var theme = attributes.GetValueOrDefault(slot + "Theme");
            var name = attributes.GetValueOrDefault(slot);
            if (!string.IsNullOrEmpty(theme)) resolved[slot] = "theme:" + theme;
            else if (!string.IsNullOrEmpty(name)) resolved[slot] = "font:" + name;
        }
    }
}
