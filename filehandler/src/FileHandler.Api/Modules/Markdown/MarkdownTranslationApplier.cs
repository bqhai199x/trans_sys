using System.Text;
using FileHandler.Api.Common;

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
    /// <param name="validationBaseline">Whether nonempty translations use source text to validate intended structure changes.</param>
    /// <param name="validateStructure">Optional block structure validator.</param>
    /// <returns>Patched text, fatal errors and recoverable unit skips.</returns>
    public static (string? Text, IReadOnlyList<FileError> Errors, IReadOnlyList<SkipMetadata> Skipped) Apply(MarkdownExtraction extraction, IReadOnlyList<string> translations, FileHandlingOptions options, CancellationToken cancellationToken, bool validationBaseline = false, Func<string, string, IReadOnlyList<FileError>>? validateStructure = null)
    {
        var errors = ValidateBatch(extraction, translations, options);
        var skipped = new List<SkipMetadata>();
        if (errors.Count > 0)
            return (null, errors, skipped);

        var replacements = new List<(int Index, int Start, int End, string Value)>();
        long outputChars = extraction.Source.Text.Length;
        long outputBytes = Encoding.UTF8.GetByteCount(extraction.Source.Text) + (extraction.Source.HasBom ? 3 : 0);
        for (var i = 0; i < extraction.Units.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var unit = extraction.Units[i];
            var translated = translations[i];
            if (translated == unit.Text)
                continue;

            if (string.IsNullOrWhiteSpace(translated))
            {
                skipped.Add(new(SkipCodes.EmptyTranslation, SkipSeverity.Warning, SkipStage.Translation, SkipScope.Unit, 1, ProcessingMessages.EmptyTranslation, new(Line: unit.Line), i));
                continue;
            }
            try { Utf8TextReader.GetByteCount(translated.AsSpan()); }
            catch (EncoderFallbackException)
            {
                skipped.Add(new(SkipCodes.InvalidTranslation, SkipSeverity.Warning, SkipStage.Translation, SkipScope.Unit, 1, ProcessingMessages.InvalidUnicode, new(Line: unit.Line), i));
                continue;
            }
            if (unit.IsHeading && extraction.HasInternalLinks)
            {
                skipped.Add(new(SkipCodes.InternalAnchorChangeUnsupported, SkipSeverity.Warning, SkipStage.Translation, SkipScope.Unit, 1, ProcessingMessages.InternalAnchorChange, new(Line: unit.Line), i));
                continue;
            }
            if (unit.IsHeading && translated.IndexOfAny(['\r', '\n']) >= 0)
            {
                skipped.Add(new(SkipCodes.InvalidStructure, SkipSeverity.Warning, SkipStage.Translation, SkipScope.Unit, 1, ProcessingMessages.HeadingStructure, new(Line: unit.Line), i));
                continue;
            }

            var (value, markerErrors) = DecodeTranslation(unit, translated, i, validationBaseline);
            if (markerErrors.Count > 0)
                skipped.Add(new(markerErrors[0].Code, SkipSeverity.Warning, SkipStage.Translation, SkipScope.Unit, 1,
                    string.Join(" ", markerErrors.Select(e => e.Message).Distinct(StringComparer.Ordinal)), new(Line: unit.Line), i));
            if (value is not null)
            {
                if (validateStructure is not null && !unit.IsMermaidLabel && unit.BlockStart is int blockStart && unit.BlockEnd is int blockEnd)
                {
                    var prefix = extraction.Source.Text[blockStart..unit.Start];
                    var suffix = extraction.Source.Text[unit.End..blockEnd];
                    var original = extraction.Source.Text[blockStart..blockEnd];
                    var candidate = prefix + value + suffix;
                    var findings = validateStructure(original, candidate);
                    if (findings.Count > 0)
                    {
                        var baseline = DecodeTranslation(unit, translated, i, true);
                        if (baseline.Value is not null) findings = validateStructure(prefix + baseline.Value + suffix, candidate);
                    }
                    if (findings.Count > 0)
                    {
                        skipped.Add(new(SkipCodes.InvalidStructure, SkipSeverity.Warning, SkipStage.Translation, SkipScope.Unit, 1, ProcessingMessages.BlockStructure, new(Line: unit.Line), i));
                        continue;
                    }
                }
                outputChars += value.Length - (unit.End - unit.Start);
                outputBytes += Encoding.UTF8.GetByteCount(value) - Encoding.UTF8.GetByteCount(extraction.Source.Text.AsSpan(unit.Start, unit.End - unit.Start));
                replacements.Add((i, unit.Start, unit.End, value));
            }
        }

        if (errors.Count > 0)
            return (null, errors, skipped);

        for (var i = 1; i < replacements.Count; i++)
        {
            if (replacements[i - 1].End > replacements[i].Start)
            {
                errors.Add(new("patch_conflict", ProcessingMessages.PatchConflict));
            }
        }

        if (errors.Count > 0)
            return (null, errors, skipped);

        if (outputBytes > options.MaxOutputBytes)
            return (null, [new FileError("output_too_large", ProcessingMessages.OutputSizeLimit)], skipped);
        var output = new StringBuilder((int)Math.Min(outputChars, int.MaxValue));
        var offset = 0;
        for (var i = 0; i < replacements.Count; i++)
        {
            var patch = replacements[i];
            output.Append(extraction.Source.Text, offset, patch.Start - offset);
            output.Append(patch.Value);
            offset = patch.End;
        }

        output.Append(extraction.Source.Text, offset, extraction.Source.Text.Length - offset);

        return (output.ToString(), [], skipped);
    }

    /// <summary>
    /// Checks fatal extraction, count, null and translation length constraints.
    /// </summary>
    /// <param name="extraction">Extracted source units.</param>
    /// <param name="translations">Translated units.</param>
    /// <param name="options">File processing limits.</param>
    /// <returns>Validation errors found in batch.</returns>
    private static List<FileError> ValidateBatch(MarkdownExtraction extraction, IReadOnlyList<string> translations, FileHandlingOptions options)
    {
        var errors = new List<FileError>();
        if (extraction.Errors.Count > 0)
            errors.AddRange(extraction.Errors);

        if (translations.Count != extraction.Units.Count)
            errors.Add(new("translation_count_mismatch", ProcessingMessages.TranslationCountMismatch(extraction.Units.Count, translations.Count)));

        var count = Math.Min(translations.Count, extraction.Units.Count);
        for (var i = 0; i < count; i++)
        {
            if (translations[i] is null)
                errors.Add(new(SkipCodes.InvalidTranslation, ProcessingMessages.NullTranslation, i, extraction.Units[i].Line));
            else if (translations[i].Length > options.MaxTranslationChars)
                errors.Add(new("translation_too_long", ProcessingMessages.TranslationLimit(options.MaxTranslationChars), i, extraction.Units[i].Line));
        }

        return errors;
    }

    /// <summary>
    /// Validates marker syntax, pairing, and restores preserved content.
    /// </summary>
    /// <param name="unit">Original translation unit definition.</param>
    /// <param name="translation">Translated text containing preservation markers.</param>
    /// <param name="index">Zero-based unit index.</param>
    /// <param name="validationBaseline">Whether nonempty translated bindings retain source text.</param>
    /// <returns>Decoded Markdown text, or null with marker validation errors.</returns>
    private static (string? Value, List<FileError> Errors) DecodeTranslation(MarkdownUnit unit, string translation, int index, bool validationBaseline)
    {
        var errors = new List<FileError>();
        if (unit.IsMermaidLabel)
        {
            if (translation.Any(char.IsControl))
                return (null, [new(SkipCodes.InvalidStructure, ProcessingMessages.MermaidStructure, index, unit.Line)]);
            var value = MermaidCodec.Encode(validationBaseline ? unit.Text : translation, unit.MermaidQuoted);
            return (value, errors);
        }
        IReadOnlyList<MarkerToken> rawTokens;
        if (unit.TokenTemplate is { } template)
        {
            var decoded = MarkdownTokenCodec.Decode(template, translation, index, unit.Line);
            if (decoded.Error is not null)
                return (null, [decoded.Error]);
            rawTokens = validationBaseline
                ? decoded.Tokens.Select((token, i) => !token.IsMarker && !string.IsNullOrWhiteSpace(token.Value)
                    ? token with { Value = template.Tokens[i].Value } : token).ToArray()
                : decoded.Tokens;
        }
        else
            rawTokens = MarkdownMarkerCodec.Parse(translation);
        var tokens = CanonicalizeFormattingTokens(rawTokens, unit.Markers);
        var emptyEmphasis = FindEmptyEmphasis(tokens, unit.Markers);
        var seenOpen = new HashSet<int>();
        var seenClose = new HashSet<int>();
        var stack = new Stack<int>();
        var output = new StringBuilder();
        foreach (var token in tokens)
        {
            if (!token.IsMarker)
            {
                if (token.Value.Length > 0 && stack.TryPeek(out var owner) && unit.Markers[owner].Kind == MarkerKind.Protected)
                    errors.Add(new(SkipCodes.ProtectedMarkerNotEmpty, ProcessingMessages.ProtectedMarkerNotEmpty, index, unit.Line));
                if (unit.TokenTemplate is null && token.Value.Contains(MarkdownMarkerCodec.MarkerPrefix, StringComparison.Ordinal))
                    errors.Add(new(SkipCodes.InvalidMarkerSyntax, ProcessingMessages.LegacyMarkerSyntax, index, unit.Line));
                output.Append(EscapeText(token.Value, unit.NewlineReplacement));
                continue;
            }

            var canonical = unit.TokenTemplate is null
                ? (token.IsClosing ? MarkdownMarkerCodec.Close(token.Id) : MarkdownMarkerCodec.Open(token.Id))
                : token.Value;
            if (!string.Equals(token.Value, canonical, StringComparison.Ordinal))
            {
                errors.Add(new(SkipCodes.InvalidMarkerSyntax, ProcessingMessages.NoncanonicalMarker(token.Value, canonical), index, unit.Line, token.Value));
                continue;
            }

            if (!unit.Markers.TryGetValue(token.Id, out var definition))
            {
                errors.Add(new(SkipCodes.UnexpectedMarker, ProcessingMessages.UnexpectedMarker(token.Value), index, unit.Line, token.Value));
                continue;
            }

            if (!token.IsClosing)
            {
                if (!seenOpen.Add(token.Id))
                    errors.Add(new(SkipCodes.DuplicateMarker, ProcessingMessages.DuplicateMarker(token.Value), index, unit.Line, token.Value));
                stack.Push(token.Id);
                if (!emptyEmphasis.Contains(token.Id)) output.Append(definition.OpenSource);
            }
            else
            {
                if (!seenClose.Add(token.Id))
                    errors.Add(new(SkipCodes.DuplicateMarker, ProcessingMessages.DuplicateMarker(token.Value), index, unit.Line, token.Value));
                if (stack.Count == 0 || stack.Pop() != token.Id)
                    errors.Add(new(SkipCodes.InvalidMarkerNesting, ProcessingMessages.InvalidMarkerNesting(token.Value), index, unit.Line, token.Value));
                if (!emptyEmphasis.Contains(token.Id)) output.Append(definition.CloseSource);
            }
        }

        foreach (var marker in unit.Markers.Values)
        {
            if (!seenOpen.Contains(marker.Id))
                errors.Add(new(SkipCodes.MissingMarker, ProcessingMessages.MissingOpeningMarker(MarkdownMarkerCodec.Open(marker.Id)), index, unit.Line, MarkdownMarkerCodec.Open(marker.Id)));
            if (!seenClose.Contains(marker.Id))
                errors.Add(new(SkipCodes.MissingMarker, ProcessingMessages.MissingClosingMarker(MarkdownMarkerCodec.Close(marker.Id)), index, unit.Line, MarkdownMarkerCodec.Close(marker.Id)));
        }

        return errors.Count == 0 ? (output.ToString(), errors) : (null, errors);
    }

    /// <summary>
    /// Finds emphasis wrappers without visible text or protected content.
    /// </summary>
    /// <param name="tokens">Decoded typed restoration bindings.</param>
    /// <param name="markers">Source formatting and protection definitions.</param>
    /// <returns>Emphasis IDs whose delimiters must be omitted.</returns>
    private static HashSet<int> FindEmptyEmphasis(IReadOnlyList<MarkerToken> tokens, IReadOnlyDictionary<int, MarkerDefinition> markers)
    {
        var empty = markers.Values.Where(m => m.IsEmphasis).Select(m => m.Id).ToHashSet();
        var owners = new Stack<int>();
        foreach (var token in tokens)
        {
            if (!token.IsMarker)
            {
                if (!string.IsNullOrWhiteSpace(token.Value)) empty.ExceptWith(owners);
            }
            else if (!token.IsClosing)
            {
                if (markers.TryGetValue(token.Id, out var definition) &&
                    (definition.Kind == MarkerKind.Protected || !definition.IsEmphasis)) empty.ExceptWith(owners);
                owners.Push(token.Id);
            }
            else if (owners.Count > 0) owners.Pop();
        }
        return empty;
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

        return sb.ToString();
    }
}
