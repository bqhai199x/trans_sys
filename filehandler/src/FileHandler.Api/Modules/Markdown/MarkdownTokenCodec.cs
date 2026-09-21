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

        var structured = runs != 1 || anchors != 0;
        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            if (!structured)
                builder.Append(part.SourceText);
            else if (part.IsAnchor)
                builder.Append(TranslationTokenSyntax.Anchor(part.Id));
            else
            {
                builder.Append(TranslationTokenSyntax.Open(part.Id));
                TranslationTokenSyntax.AppendEscaped(builder, part.SourceText);
                builder.Append(TranslationTokenSyntax.Close(part.Id));
            }
        }
        var text = builder.ToString();
        return (text, new(structured, tokens, parts));
    }

    /// <summary>
    /// Validates wire tokens and restores internal Markdown binding tokens.
    /// </summary>
    /// <param name="template">Source-derived immutable token mapping.</param>
    /// <param name="translation">Public translation string.</param>
    /// <param name="index">Zero-based translation index.</param>
    /// <param name="line">Original source line range.</param>
    /// <returns>Restoration tokens or validation error without partial tokens.</returns>
    internal static (IReadOnlyList<MarkerToken> Tokens, FileError? Error) Decode(
        MarkdownTokenTemplate template, string translation, int index, SourceLineRange line)
    {
        var restored = template.Tokens.ToArray();
        var offset = 0;
        var hasContent = false;
        foreach (var part in template.Parts)
        {
            var marker = part.IsAnchor ? TranslationTokenSyntax.Anchor(part.Id) : TranslationTokenSyntax.Open(part.Id);
            string text;
            if (!template.Structured)
                text = translation;
            else
            {
                if (!translation.AsSpan(offset).StartsWith(marker, StringComparison.Ordinal))
                    return Reject(index, line, SkipCodes.InvalidMarkerSyntax, marker);
                offset += marker.Length;
                if (part.IsAnchor)
                    continue;
                if (!TranslationTokenSyntax.TryReadText(translation, ref offset, TranslationTokenSyntax.Close(part.Id), out text))
                    return Reject(index, line, SkipCodes.InvalidMarkerSyntax, marker);
            }
            hasContent |= !string.IsNullOrWhiteSpace(text);
            foreach (var tokenIndex in part.TokenIndexes)
                restored[tokenIndex] = restored[tokenIndex] with { Value = tokenIndex == part.TokenIndexes[0] ? text : string.Empty };
        }
        if (template.Structured && offset != translation.Length)
            return Reject(index, line, SkipCodes.InvalidMarkerSyntax, null);
        if (!hasContent)
            return Reject(index, line, SkipCodes.EmptyTranslation, null);
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
internal sealed record MarkdownTokenTemplate(bool Structured, IReadOnlyList<MarkerToken> Tokens, IReadOnlyList<MarkdownTokenPart> Parts);

/// <summary>
/// Public run or anchor mapped to original restoration token.
/// </summary>
/// <param name="Id">Canonical run or anchor identifier scoped to unit.</param>
/// <param name="TokenIndexes">Original restoration token indexes sharing formatting and ownership.</param>
/// <param name="IsAnchor">Whether original content must remain unchanged.</param>
/// <param name="SourceText">Original decoded text for literal binding.</param>
internal sealed record MarkdownTokenPart(string Id, IReadOnlyList<int> TokenIndexes, bool IsAnchor, string SourceText);
