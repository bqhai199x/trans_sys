using System.Globalization;
using System.Text;
using FileHandler.Api.Common;

namespace FileHandler.Api.Modules.Markdown;

/// <summary>
/// Maps flat public run and anchor tokens to Markdown syntax bindings.
/// </summary>
internal static class MarkdownTokenCodec
{

    /// <summary>
    /// Builds flat wire tokens while retaining nested Markdown bindings.
    /// </summary>
    /// <param name="inline">Internal syntax-preserving extraction.</param>
    /// <returns>Public text and restoration template.</returns>
    internal static (string Text, MarkdownTokenTemplate Template) Encode(EncodedInline inline)
    {
        var tokens = inline.Tokens;
        var parts = new List<MarkdownTokenPart>();
        var owners = new Stack<int>();
        string? previousStyle = null;
        var runs = 0;
        var anchors = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.IsMarker)
            {
                var definition = inline.Markers[token.Id];
                if (definition.Kind == MarkerKind.Formatting)
                {
                    if (token.IsClosing) owners.Pop();
                    else owners.Push(token.Id);
                }
                else if (!token.IsClosing)
                {
                    parts.Add(new("k" + (anchors++).ToString(CultureInfo.InvariantCulture), [i], true, string.Empty));
                    previousStyle = null;
                }
            }
            else
            {
                var style = string.Join("/", owners.Reverse().Select(id => inline.Markers[id].IsEmphasis
                    ? "emphasis:" + inline.Markers[id].OpenSource.Replace('_', '*')
                    : "owner:" + id.ToString(CultureInfo.InvariantCulture)));
                if (parts.Count > 0 && !parts[^1].IsAnchor && previousStyle == style)
                    parts[^1] = parts[^1] with
                    {
                        SourceText = parts[^1].SourceText + token.Value,
                        TokenIndexes = [.. parts[^1].TokenIndexes, i]
                    };
                else if (string.IsNullOrWhiteSpace(token.Value))
                    parts.Add(new("k" + (anchors++).ToString(CultureInfo.InvariantCulture), [i], true, token.Value));
                else
                    parts.Add(new("r" + (runs++).ToString(CultureInfo.InvariantCulture), [i], false, token.Value));
                previousStyle = style;
            }
        }

        var stacks = new Dictionary<string, IReadOnlyList<int>>();
        var active = new List<int>();
        var byIndex = parts.ToDictionary(p => p.TokenIndexes[0]);
        for (var i = 0; i < tokens.Count; i++)
        {
            if (byIndex.TryGetValue(i, out var part)) stacks[part.Id] = active.ToArray();
            var token = tokens[i];
            if (!token.IsMarker || inline.Markers[token.Id].Kind != MarkerKind.Formatting) continue;
            if (token.IsClosing) active.RemoveAt(active.Count - 1);
            else active.Add(token.Id);
        }
        var scopes = new List<string>();
        var wireParts = new List<MarkdownTokenPart>();
        foreach (var part in parts)
        {
            var owner = string.Join("/", stacks[part.Id].Where(id => !inline.Markers[id].IsEmphasis));
            scopes.Add(owner);
            var id = part.Id;
            if (part.IsAnchor)
            {
                var token = tokens[part.TokenIndexes[0]];
                var movable = token.IsMarker && inline.Markers[token.Id].IsMovable;
                if (!movable) id = "b" + part.Id[1..];
            }
            stacks[id] = stacks[part.Id];
            wireParts.Add(part with { Id = id });
        }
        var fullSource = TranslationTokenSyntax.Encode(wireParts.Select(p => new TranslationTokenPart(p.Id, p.IsAnchor ? null : p.SourceText)).ToArray(), scopes, false);
        var fullParts = TranslationTokenParser.Parse(fullSource);
        var visible = TranslationTokenSyntax.Compact(fullParts);
        var structured = visible.Count != 1 || visible[0].Text is null || TranslationTokenSyntax.IsStructured(visible[0].Text!);
        var text = structured
            ? TranslationTokenSyntax.Encode(wireParts.Select(p => new TranslationTokenPart(p.Id, p.IsAnchor ? null : p.SourceText)).ToArray(), scopes)
            : visible[0].Text!;
        return (text, new(structured, tokens, wireParts) { Source = text, Owners = stacks, FullParts = fullParts });
    }

    /// <summary>
    /// Validates wire tokens and restores internal Markdown binding tokens.
    /// </summary>
    /// <param name="template">Source-derived immutable token mapping.</param>
    /// <param name="translation">Public translation string.</param>
    /// <param name="index">Zero-based translation index.</param>
    /// <param name="line">Original source line range.</param>
    /// <param name="baseline">Whether to restore source text in translated token order.</param>
    /// <returns>Restoration tokens or validation error without partial tokens.</returns>
    internal static (IReadOnlyList<MarkerToken> Tokens, FileError? Error) Decode(
        MarkdownTokenTemplate template, string translation, int index, SourceLineRange line, bool baseline = false)
    {
        if (template.Structured)
        {
            var error = TranslationTokenParser.Validate(template.Source, translation);
            if (error is not null) return Reject(index, line, error == "empty_translation" ? SkipCodes.EmptyTranslation : SkipCodes.InvalidMarkerSyntax, null);
            var output = new List<MarkerToken>();
            var active = new List<int>();
            var bindings = template.Parts.ToDictionary(p => p.Id);
            foreach (var part in TranslationTokenSyntax.Expand(template.FullParts, TranslationTokenParser.Parse(translation)))
            {
                if (!bindings.TryGetValue(part.Id, out var binding)) continue; // Synthetic scope boundary has no source content.
                var owners = template.Owners[part.Id];
                var common = 0;
                while (common < active.Count && common < owners.Count && active[common] == owners[common]) common++;
                for (var i = active.Count - 1; i >= common; i--) output.Add(new(true, active[i], "", true));
                for (var i = common; i < owners.Count; i++) output.Add(new(true, owners[i], "", false));
                active = owners.ToList();
                if (part.Text is not null) output.Add(new(false, 0, baseline && !string.IsNullOrWhiteSpace(part.Text) ? binding.SourceText : part.Text, false));
                else
                {
                    var token = template.Tokens[binding.TokenIndexes[0]];
                    output.Add(token);
                    if (token.IsMarker) output.Add(new(true, token.Id, "", true));
                }
            }
            for (var i = active.Count - 1; i >= 0; i--) output.Add(new(true, active[i], "", true));
            return (output, null);
        }
        if (string.IsNullOrWhiteSpace(translation))
            return Reject(index, line, SkipCodes.EmptyTranslation, null);
        var restored = template.Tokens.ToArray();
        foreach (var part in template.Parts.Where(p => !p.IsAnchor))
            foreach (var tokenIndex in part.TokenIndexes)
                restored[tokenIndex] = restored[tokenIndex] with { Value = tokenIndex == part.TokenIndexes[0] ? translation : string.Empty };
        return (restored, null);
    }

    /// <summary>
    /// Completes failed decoding without exposing internal preservation syntax.
    /// </summary>
    /// <param name="index">Zero-based translation index.</param>
    /// <param name="line">Original source line range.</param>
    /// <param name="code">Public error code.</param>
    /// <param name="marker">Expected public token, when available.</param>
    /// <returns>Empty token list with located validation error.</returns>
    private static (IReadOnlyList<MarkerToken> Tokens, FileError? Error) Reject(
        int index, SourceLineRange line, string code, string? marker)
    {
        var error = new FileError(code, code == SkipCodes.EmptyTranslation
            ? ProcessingMessages.EmptySlots
            : ProcessingMessages.MarkdownTokens, index, line, marker);
        return ([], error);
    }
}

/// <summary>
/// Public wire representation and internal syntax restoration bindings.
/// </summary>
/// <param name="Structured">Whether public text uses run and anchor tokens.</param>
/// <param name="Tokens">Original internal restoration tokens.</param>
/// <param name="Parts">Ordered public runs and anchors.</param>
internal sealed record MarkdownTokenTemplate(bool Structured, IReadOnlyList<MarkerToken> Tokens, IReadOnlyList<MarkdownTokenPart> Parts)
{

    /// <summary>
    /// Complete source sequence retaining hidden fixed objects for restoration.
    /// </summary>
    internal IReadOnlyList<TranslationTokenPart> FullParts { get; init; } = [];

    /// <summary>
    /// Canonical source used for movement validation.
    /// </summary>
    internal string Source { get; init; } = "";

    /// <summary>
    /// Formatting and ownership stack for each public token.
    /// </summary>
    internal IReadOnlyDictionary<string, IReadOnlyList<int>> Owners { get; init; } = new Dictionary<string, IReadOnlyList<int>>();


}

/// <summary>
/// Public run or anchor mapped to original restoration token.
/// </summary>
/// <param name="Id">Canonical run or anchor identifier scoped to unit.</param>
/// <param name="TokenIndexes">Original restoration token indexes sharing formatting and ownership.</param>
/// <param name="IsAnchor">Whether original content must remain unchanged.</param>
/// <param name="SourceText">Original decoded text for literal binding.</param>
internal sealed record MarkdownTokenPart(string Id, IReadOnlyList<int> TokenIndexes, bool IsAnchor, string SourceText);
