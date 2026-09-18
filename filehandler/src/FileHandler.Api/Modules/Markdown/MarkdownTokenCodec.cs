using System.Globalization;
using System.Text;
using FileHandler.Api.Common;
using FileHandler.Api.Diagnostics;

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
        using var trace = DebugTrace.Enter("MarkdownTokenCodec", "Encode", () => new { inline.Start, inline.End });
        try
        {
            var tokens = MarkdownMarkerCodec.Parse(inline.Text);
            var parts = new List<MarkdownTokenPart>();
            var runs = 0;
            var anchors = 0;
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.IsMarker)
                {
                    if (!token.IsClosing && inline.Markers[token.Id].Kind == MarkerKind.Protected)
                        parts.Add(new("k" + (anchors++).ToString(CultureInfo.InvariantCulture), i, true, string.Empty));
                }
                else if (string.IsNullOrWhiteSpace(token.Value))
                    parts.Add(new("k" + (anchors++).ToString(CultureInfo.InvariantCulture), i, true, token.Value));
                else
                    parts.Add(new("r" + (runs++).ToString(CultureInfo.InvariantCulture), i, false, token.Value));
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
            trace.State("template", () => new { structured, runs, anchors });
            trace.State("sourceText", () => text);
            trace.Return(new { structured, runs, anchors });
            return (text, new(structured, tokens, parts));
        }
        catch (Exception exception)
        {
            trace.Error(exception);
            throw;
        }
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
        using var trace = DebugTrace.Enter("MarkdownTokenCodec", "Decode", () => new { index, template.Structured, translation });
        try
        {
            var restored = template.Tokens.ToArray();
            var offset = 0;
            foreach (var part in template.Parts)
            {
                var marker = part.IsAnchor ? TranslationTokenSyntax.Anchor(part.Id) : TranslationTokenSyntax.Open(part.Id);
                string text;
                if (!template.Structured)
                    text = translation;
                else
                {
                    if (!translation.AsSpan(offset).StartsWith(marker, StringComparison.Ordinal))
                        return Reject(trace, index, line, "invalid_marker_syntax", marker);
                    offset += marker.Length;
                    if (part.IsAnchor)
                        continue;
                    if (!TranslationTokenSyntax.TryReadText(translation, ref offset, TranslationTokenSyntax.Close(part.Id), out text))
                        return Reject(trace, index, line, "invalid_marker_syntax", marker);
                }
                if (string.IsNullOrWhiteSpace(text))
                    return Reject(trace, index, line, "empty_translation", marker);
                restored[part.TokenIndex] = restored[part.TokenIndex] with { Value = text };
            }
            if (template.Structured && offset != translation.Length)
                return Reject(trace, index, line, "invalid_marker_syntax", null);
            trace.State("validation", () => "valid");
            trace.Return(new { outcome = "success", tokenCount = restored.Length });
            return (restored, null);
        }
        catch (Exception exception)
        {
            trace.Error(exception);
            throw;
        }

    }

    /// <summary>
    /// Completes failed decoding without exposing internal preservation syntax.
    /// </summary>
    /// <param name="trace">Active decoder trace.</param>
    /// <param name="index">Zero-based translation index.</param>
    /// <param name="line">Original source line range.</param>
    /// <param name="code">Public error code.</param>
    /// <param name="marker">Expected public token, when available.</param>
    /// <returns>Empty token list with located validation error.</returns>
    private static (IReadOnlyList<MarkerToken> Tokens, FileError? Error) Reject(
        TraceCall trace, int index, SourceLineRange line, string code, string? marker)
    {
        var error = new FileError(code, code == "empty_translation"
            ? "Vùng dịch không được rỗng hoặc chỉ chứa khoảng trắng."
            : "Bản dịch phải giữ nguyên token ox, thứ tự và cú pháp escape của nguồn.", index, line, marker);
        trace.State("validation", () => new { error.Code, error.Index, error.Marker });
        trace.Return(new { outcome = "failed", code });
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
/// <param name="TokenIndex">Original restoration token index.</param>
/// <param name="IsAnchor">Whether original content must remain unchanged.</param>
/// <param name="SourceText">Original decoded text for literal binding.</param>
internal sealed record MarkdownTokenPart(string Id, int TokenIndex, bool IsAnchor, string SourceText);
