using System.Text;
using FileHandler.Api.Common;
using FileHandler.Api.Diagnostics;

namespace FileHandler.Api.Modules.Markdown;

internal static class MarkdownTranslationApplier
{

    /// <summary>
    /// Validates translations and patches original Markdown source.
    /// </summary>
    /// <param name="extraction">Extracted units and source metadata.</param>
    /// <param name="translations">Translated units in source order.</param>
    /// <param name="options">File processing limits.</param>
    /// <param name="cancellationToken">Token for cancelling this operation.</param>
    /// <returns>Patched Markdown text, or null with validation errors.</returns>
    public static (string? Text, IReadOnlyList<FileError> Errors) Apply(MarkdownExtraction extraction, IReadOnlyList<string> translations, FileHandlingOptions options, CancellationToken cancellationToken)
    {
        using var trace = DebugTrace.Enter("MarkdownTranslationApplier", "Apply", () => new { extraction, translations, options, cancellationToken });
        try
        {
            var errors = ValidateBatch(extraction, translations, options);
            if (errors.Count > 0)
                return trace.Return<(string? Text, IReadOnlyList<FileError> Errors)>((null, errors));

            if (extraction.HasInternalLinks && extraction.Units.Select((unit, index) => (unit, index)).Where(x => x.unit.IsHeading).Any(x => translations[x.index] != x.unit.Text))
            {
                trace.State("outcome", () => "internalAnchorChange");
                return trace.Return<(string? Text, IReadOnlyList<FileError> Errors)>((null, [new("internal_anchor_change_unsupported", "Không thể đổi heading khi tài liệu có liên kết anchor nội bộ trong profile V1.")]));
            }

            var replacements = new List<(int Start, int End, string Value)>();
            long outputBytes = Encoding.UTF8.GetByteCount(extraction.Source.Text) + (extraction.Source.HasBom ? 3 : 0);
            for (var i = 0; i < extraction.Units.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var unit = extraction.Units[i];
                var translated = translations[i];
                using var item = DebugTrace.Item(i + 1);
                item.State("unit", () => unit);
                item.State("translation", () => translated);
                if (translated == unit.Text)
                {
                    item.State("outcome", () => "unchanged");
                    continue;
                }

                if (unit.IsHeading && translated.IndexOfAny(['\r', '\n']) >= 0)
                {
                    errors.Add(new("invalid_structure", "Bản dịch heading không được tạo thêm dòng hoặc block.", i, unit.Line));
                    item.State("error", () => errors[^1]);
                    continue;
                }

                var (value, markerErrors) = DecodeTranslation(unit, translated, i);
                errors.AddRange(markerErrors);
                if (value is not null)
                {
                    outputBytes += Encoding.UTF8.GetByteCount(value) - Encoding.UTF8.GetByteCount(extraction.Source.Text.AsSpan(unit.Start, unit.End - unit.Start));
                    replacements.Add((unit.Start, unit.End, value));
                    item.State("outcome", () => "replacementQueued");
                }
                else
                    item.State("outcome", () => "invalidMarkers");
            }

            trace.State("replacementCount", () => replacements.Count);
            if (errors.Count > 0)
                return trace.Return<(string? Text, IReadOnlyList<FileError> Errors)>((null, errors));

            replacements.Sort((a, b) => b.Start.CompareTo(a.Start));
            for (var i = 1; i < replacements.Count; i++)
            {
                if (replacements[i - 1].Start < replacements[i].End)
                {
                    trace.State("conflict", () => new { previous = new { replacements[i - 1].Start, replacements[i - 1].End }, current = new { replacements[i].Start, replacements[i].End } });
                    errors.Add(new("patch_conflict", "Các vùng thay thế bị chồng lấn."));
                }
            }

            if (errors.Count > 0)
                return trace.Return<(string? Text, IReadOnlyList<FileError> Errors)>((null, errors));

            foreach (var patch in replacements)
                trace.State("patch", () => new { patch.Start, patch.End, patch.Value });

            if (outputBytes > options.MaxOutputBytes)
                return (null, [new FileError("output_too_large", "Kết quả vượt giới hạn đầu ra.")]);
            var output = new StringBuilder((int)Math.Min(outputBytes, int.MaxValue));
            var offset = 0;
            for (var i = replacements.Count - 1; i >= 0; i--)
            {
                var patch = replacements[i];
                output.Append(extraction.Source.Text, offset, patch.Start - offset);
                output.Append(patch.Value);
                offset = patch.End;
            }

            output.Append(extraction.Source.Text, offset, extraction.Source.Text.Length - offset);

            return trace.Return<(string? Text, IReadOnlyList<FileError> Errors)>((output.ToString(), []));
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
    }

    /// <summary>
    /// Validates extraction errors, translation count, lengths, and non-empty translations.
    /// </summary>
    /// <param name="extraction">Extracted source units.</param>
    /// <param name="translations">Translated units.</param>
    /// <param name="options">File processing limits.</param>
    /// <returns>Validation errors found in batch.</returns>
    private static List<FileError> ValidateBatch(MarkdownExtraction extraction, IReadOnlyList<string> translations, FileHandlingOptions options)
    {
        using var trace = DebugTrace.Enter("MarkdownTranslationApplier", "ValidateBatch", () => new { extraction, translations, options });
        try
        {
            var errors = new List<FileError>();
            if (extraction.Errors.Count > 0)
                errors.AddRange(extraction.Errors);

            if (translations.Count != extraction.Units.Count)
                errors.Add(new("translation_count_mismatch", $"Cần {extraction.Units.Count} bản dịch nhưng nhận được {translations.Count}."));

            var count = Math.Min(translations.Count, extraction.Units.Count);
            for (var i = 0; i < count; i++)
            {
                if (translations[i] is null)
                    errors.Add(new("invalid_translation", "Bản dịch không được null.", i, extraction.Units[i].Line));
                else if (string.IsNullOrWhiteSpace(translations[i]))
                    errors.Add(new("empty_translation", "Bản dịch không được rỗng hoặc chỉ chứa khoảng trắng.", i, extraction.Units[i].Line));
                else if (translations[i].Length > options.MaxTranslationChars)
                    errors.Add(new("translation_too_long", $"Bản dịch vượt giới hạn {options.MaxTranslationChars} ký tự.", i, extraction.Units[i].Line));
                else
                {
                    try
                    {
                        Utf8TextReader.GetByteCount(translations[i].AsSpan());
                    }
                    catch (EncoderFallbackException)
                    {
                        errors.Add(new("invalid_translation", "Bản dịch chứa chuỗi Unicode không hợp lệ.", i, extraction.Units[i].Line));
                    }
                }
            }

            return trace.Return<List<FileError>>(errors);
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
    }

    /// <summary>
    /// Validates marker syntax, pairing, and restores preserved content.
    /// </summary>
    /// <param name="unit">Original translation unit definition.</param>
    /// <param name="translation">Translated text containing preservation markers.</param>
    /// <param name="index">Zero-based unit index.</param>
    /// <returns>Decoded Markdown text, or null with marker validation errors.</returns>
    private static (string? Value, List<FileError> Errors) DecodeTranslation(MarkdownUnit unit, string translation, int index)
    {
        using var trace = DebugTrace.Enter("MarkdownTranslationApplier", "DecodeTranslation", () => new { unit, translation, index });
        try
        {
            var errors = new List<FileError>();
            IReadOnlyList<MarkerToken> rawTokens;
            if (unit.TokenTemplate is { } template)
            {
                var decoded = MarkdownTokenCodec.Decode(template, translation, index, unit.Line);
                if (decoded.Error is not null)
                    return trace.Return<(string? Value, List<FileError> Errors)>((null, [decoded.Error]));
                rawTokens = decoded.Tokens;
            }
            else
                rawTokens = MarkdownMarkerCodec.Parse(translation);
            trace.State("tokens", () => rawTokens);
            var tokens = CanonicalizeFormattingTokens(rawTokens, unit.Markers);
            trace.State("tokens", () => tokens);
            var seenOpen = new HashSet<int>();
            var seenClose = new HashSet<int>();
            var stack = new Stack<int>();
            var output = new StringBuilder();
            foreach (var token in tokens)
            {
                if (!token.IsMarker)
                {
                    if (token.Value.Length > 0 && stack.TryPeek(out var owner) && unit.Markers[owner].Kind == MarkerKind.Protected)
                        errors.Add(new("protected_marker_not_empty", "Marker bảo vệ phải rỗng.", index, unit.Line));
                    if (unit.TokenTemplate is null && token.Value.Contains(MarkdownMarkerCodec.MarkerPrefix, StringComparison.Ordinal))
                        errors.Add(new("invalid_marker_syntax", "Marker keepme không đúng cú pháp hoặc vượt miền ID hỗ trợ.", index, unit.Line));
                    output.Append(EscapeText(token.Value, unit.NewlineReplacement));
                    continue;
                }

                var canonical = token.IsClosing ? MarkdownMarkerCodec.Close(token.Id) : MarkdownMarkerCodec.Open(token.Id);
                if (!string.Equals(token.Value, canonical, StringComparison.Ordinal))
                {
                    errors.Add(new("invalid_marker_syntax", $"Marker {token.Value} không ở dạng chuẩn {canonical}.", index, unit.Line, token.Value));
                    continue;
                }

                if (!unit.Markers.TryGetValue(token.Id, out var definition))
                {
                    errors.Add(new("unexpected_marker", $"Marker {token.Value} không thuộc đơn vị này.", index, unit.Line, token.Value));
                    continue;
                }

                if (!token.IsClosing)
                {
                    if (!seenOpen.Add(token.Id))
                        errors.Add(new("duplicate_marker", $"Marker {token.Value} bị lặp.", index, unit.Line, token.Value));
                    stack.Push(token.Id);
                    output.Append(definition.OpenSource);
                }
                else
                {
                    if (!seenClose.Add(token.Id))
                        errors.Add(new("duplicate_marker", $"Marker {token.Value} bị lặp.", index, unit.Line, token.Value));
                    if (stack.Count == 0 || stack.Pop() != token.Id)
                        errors.Add(new("invalid_marker_nesting", $"Marker {token.Value} đóng sai thứ tự.", index, unit.Line, token.Value));
                    output.Append(definition.CloseSource);
                }
            }

            foreach (var marker in unit.Markers.Values)
            {
                if (!seenOpen.Contains(marker.Id))
                    errors.Add(new("missing_marker", $"Thiếu marker mở {MarkdownMarkerCodec.Open(marker.Id)}.", index, unit.Line, MarkdownMarkerCodec.Open(marker.Id)));
                if (!seenClose.Contains(marker.Id))
                    errors.Add(new("missing_marker", $"Thiếu marker đóng {MarkdownMarkerCodec.Close(marker.Id)}.", index, unit.Line, MarkdownMarkerCodec.Close(marker.Id)));
            }

            trace.State("markerValidation", () => new { seenOpen, seenClose, unclosed = stack.ToArray() });
            if (errors.Count > 0)
                trace.State("partialOutput", () => output);
            return trace.Return<(string? Value, List<FileError> Errors)>(errors.Count == 0 ? (output.ToString(), errors) : (null, errors));
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
    }

    /// <summary>
    /// Pushes leading and trailing whitespace outside formatting delimiters to preserve Markdown semantics.
    /// </summary>
    /// <param name="tokens">Parsed marker and literal tokens.</param>
    /// <param name="markers">Marker definitions for current unit.</param>
    /// <returns>Canonicalized tokens with whitespace outside formatting delimiters.</returns>
    private static List<MarkerToken> CanonicalizeFormattingTokens(IReadOnlyList<MarkerToken> tokens, IReadOnlyDictionary<int, MarkerDefinition> markers)
    {
        var values = tokens.ToArray();
        var closing = new int[tokens.Count];
        Array.Fill(closing, -1);
        var nextLiteral = new int[tokens.Count];
        var previousLiteral = new int[tokens.Count];
        var openings = new Dictionary<int, Stack<int>>();
        var last = -1;
        for (var i = 0; i < tokens.Count; i++)
        {
            previousLiteral[i] = last;
            var token = tokens[i];
            if (!token.IsMarker && token.Value.Length > 0) last = i;
            if (!token.IsMarker) continue;
            if (!openings.TryGetValue(token.Id, out var stack)) openings[token.Id] = stack = new Stack<int>();
            if (!token.IsClosing) stack.Push(i);
            else if (stack.TryPop(out var open)) closing[open] = i;
        }
        last = -1;
        for (var i = tokens.Count - 1; i >= 0; i--)
        {
            nextLiteral[i] = last;
            if (!tokens[i].IsMarker && tokens[i].Value.Length > 0) last = i;
        }
        var before = new string?[tokens.Count];
        var after = new string?[tokens.Count];
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!tokens[i].IsMarker || tokens[i].IsClosing || closing[i] < 0 ||
                !markers.TryGetValue(tokens[i].Id, out var definition) || definition.Kind != MarkerKind.Formatting) continue;
            var close = closing[i];
            var first = nextLiteral[i];
            if (first >= 0 && first < close)
            {
                var value = values[first].Value;
                var trimmed = value.TrimStart(' ', '\t');
                if (trimmed.Length > 0 && trimmed.Length < value.Length)
                {
                    before[i] = value[..(value.Length - trimmed.Length)];
                    values[first] = values[first] with { Value = trimmed };
                }
            }
            var final = previousLiteral[close];
            if (final > i)
            {
                var value = values[final].Value;
                var trimmed = value.TrimEnd(' ', '\t');
                if (trimmed.Length > 0 && trimmed.Length < value.Length)
                {
                    after[close] = value[trimmed.Length..];
                    values[final] = values[final] with { Value = trimmed };
                }
            }
        }
        var result = new List<MarkerToken>(tokens.Count);
        for (var i = 0; i < values.Length; i++)
        {
            if (before[i] is { } leading) result.Add(new(false, 0, leading, false));
            result.Add(values[i]);
            if (after[i] is { } trailing) result.Add(new(false, 0, trailing, false));
        }
        return result;
    }

    /// <summary>
    /// Escapes Markdown punctuation and preserves source newline policy.
    /// </summary>
    /// <param name="text">Text to process.</param>
    /// <param name="newlineReplacement">Source newline sequence and required prefix.</param>
    /// <returns>Escaped text using source newline policy.</returns>
    private static string EscapeText(string text, string newlineReplacement)
    {
        using var trace = DebugTrace.Enter("MarkdownTranslationApplier", "EscapeText", () => new { text, newlineReplacement });
        try
        {
            var sb = new StringBuilder(text.Length);
            var atLineStart = true;
            var digitsOnlySinceLineStart = true;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    sb.Append(newlineReplacement);
                    i++;
                    atLineStart = true;
                    digitsOnlySinceLineStart = true;
                    continue;
                }

                if (c is '\r' or '\n')
                {
                    sb.Append(newlineReplacement);
                    atLineStart = true;
                    digitsOnlySinceLineStart = true;
                    continue;
                }

                if (atLineStart && char.IsAsciiDigit(c))
                {
                    // Track digits at line start.
                }
                else if (atLineStart && (c == '.' || c == ')') && digitsOnlySinceLineStart && i > 0 && char.IsAsciiDigit(text[i - 1]))
                {
                    sb.Append('\\');
                    atLineStart = false;
                    digitsOnlySinceLineStart = false;
                }
                else
                {
                    atLineStart = false;
                    digitsOnlySinceLineStart = false;
                }

                if (c is '\\' or '`' or '*' or '_' or '{' or '}' or '[' or ']' or '(' or ')' or '<' or '>' or '#' or '!' or '|' or '+' or '-' or '=' or '~' or '&')
                    sb.Append('\\');
                sb.Append(c);
            }

            return trace.Return<string>(sb.ToString());
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
    }
}
