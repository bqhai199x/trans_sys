using System.Text;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Excel;

namespace FileHandler.Api.Modules.Office;

/// <summary>
/// Encodes document units to caller strings and validates/decodes translated tokens.
/// </summary>
public sealed class OfficeTextCodec
{

    /// <summary>
    /// Configuration options governing limits.
    /// </summary>
    private readonly OfficeProcessingOptions _options;

    /// <summary>
    /// File handling limits for general translation quotas.
    /// </summary>
    private readonly FileHandlingOptions _fileHandlingOptions;

    /// <summary>
    /// Maximum extraction units permitted by file policy.
    /// </summary>
    internal int MaxUnits => _fileHandlingOptions.MaxUnits;

    /// <summary>
    /// Creates text codec instance.
    /// </summary>
    /// <param name="options">Active Office processing options.</param>
    /// <param name="fileHandlingOptions">General file handling options.</param>
    public OfficeTextCodec(OfficeProcessingOptions options, FileHandlingOptions fileHandlingOptions)
    {
        _options = options;
        _fileHandlingOptions = fileHandlingOptions;
    }

    /// <summary>
    /// Encodes text template into canonical external string representation.
    /// </summary>
    /// <param name="template">Extracted unit text template.</param>
    /// <returns>Plain or structured encoded string.</returns>
    public string Encode(OfficeTextTemplate template)
    {
        if (template.Mode == UnitMode.Plain)
        {
            return template.Slots.Count > 0 ? template.Slots[0].OriginalText : string.Empty;
        }

        var sb = new StringBuilder();
        if (template.Order is not null)
        {
            var slotsById = template.Slots.ToDictionary(s => s.SlotId);
            foreach (var id in template.Order)
            {
                if (slotsById.TryGetValue(id, out var slot))
                {
                    sb.Append(TranslationTokenSyntax.Open(id));
                    TranslationTokenSyntax.AppendEscaped(sb, slot.OriginalText);
                    sb.Append(TranslationTokenSyntax.Close(id));
                }
                else sb.Append(TranslationTokenSyntax.Anchor(id));
            }
            return sb.ToString();
        }
        var slotIndex = 0;
        var anchorIndex = 0;

        // In structured mode, weave slots and anchors based on template order
        // If template specifies slots and anchors sequentially:
        while (slotIndex < template.Slots.Count || anchorIndex < template.Anchors.Count)
        {
            if (slotIndex < template.Slots.Count)
            {
                var slot = template.Slots[slotIndex];
                sb.Append(TranslationTokenSyntax.Open(slot.SlotId));
                TranslationTokenSyntax.AppendEscaped(sb, slot.OriginalText);
                sb.Append(TranslationTokenSyntax.Close(slot.SlotId));
                slotIndex++;
            }

            if (anchorIndex < template.Anchors.Count)
            {
                var anchor = template.Anchors[anchorIndex];
                sb.Append(TranslationTokenSyntax.Anchor(anchor.AnchorId));
                anchorIndex++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Validates caller translations and decodes slot contents.
    /// </summary>
    /// <param name="units">Ordered document extraction units.</param>
    /// <param name="texts">Caller-supplied translation strings.</param>
    /// <param name="format">Document format.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Decoded units or collected validation errors.</returns>
    public OfficeDecodeResult ValidateAndDecode(
        IReadOnlyList<OfficeTranslationUnit> units,
        IReadOnlyList<string> texts,
        OfficeFormat format,
        CancellationToken cancellationToken)
    {
        if (texts.Count != units.Count)
        {
            var error = new FileError("translation_count_mismatch", ProcessingMessages.TranslationCountMismatch(units.Count, texts.Count));
            return OfficeDecodeResult.Failure([error]);
        }

        var errors = new List<FileError>();
        var decodedUnits = new List<OfficeDecodedUnit>(units.Count);
        long totalTranslationChars = 0;

        for (var i = 0; i < units.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var unit = units[i];
            var rawText = texts[i];
            decodedUnits.Add(new OfficeDecodedUnit(i, unit.EncodedSource, unit.Slots.Select(s => s.OriginalText).ToArray()));

                if (rawText is null)
                {
                    errors.Add(new FileError(SkipCodes.InvalidTranslation, ProcessingMessages.NullTranslation) { Index = i });
                    continue;
                }

                if (rawText.Length > _fileHandlingOptions.MaxTranslationChars)
                {
                    errors.Add(new FileError("translation_too_long", ProcessingMessages.TranslationLengthLimit(rawText.Length, _fileHandlingOptions.MaxTranslationChars)) { Index = i });
                    continue;
                }

                totalTranslationChars += rawText.Length;
                if (totalTranslationChars > _options.MaxTotalTranslationChars)
                {
                    errors.Add(new FileError("office_translation_limit_exceeded", ProcessingMessages.TotalTranslationLimit(totalTranslationChars, _options.MaxTotalTranslationChars)) { Index = i });
                    break;
                }

                if (unit.Kind == OfficeUnitKinds.SheetName && !string.IsNullOrWhiteSpace(rawText))
                {
                    try
                    {
                        Utf8TextReader.GetByteCount(rawText.AsSpan());
                        if (!IsValidUnicodeAndXml(ExcelRenamePlanner.Normalize(rawText)))
                        {
                            errors.Add(new(SkipCodes.InvalidTranslation, ProcessingMessages.InvalidXmlText) { Index = i });
                            continue;
                        }
                        decodedUnits[i] = new(i, rawText, [rawText]);
                    }
                    catch (EncoderFallbackException)
                    {
                        errors.Add(new(SkipCodes.InvalidTranslation, ProcessingMessages.InvalidUnicode) { Index = i });
                    }
                    continue;
                }
                if (!IsValidUnicodeAndXml(rawText))
                {
                    errors.Add(new FileError(SkipCodes.InvalidTranslation, ProcessingMessages.InvalidXmlText) { Index = i });
                    continue;
                }

                if (unit.Mode == UnitMode.Plain)
                {
                    if (string.IsNullOrWhiteSpace(rawText))
                    {
                        errors.Add(new FileError(SkipCodes.EmptyTranslation, ProcessingMessages.EmptyTranslation) { Index = i });
                        continue;
                    }

                    if (format != OfficeFormat.Excel && (rawText.Contains('\r') || rawText.Contains('\n') || rawText.Contains('\t')))
                    {
                        errors.Add(new FileError(SkipCodes.InvalidTranslation, ProcessingMessages.RawOfficeWhitespace) { Index = i });
                        continue;
                    }

                    var slotText = rawText;
                    if (format == OfficeFormat.Excel)
                    {
                        slotText = NormalizeExcelNewlines(rawText);
                        if (slotText.Length > _options.MaxCellTextChars)
                        {
                            errors.Add(new FileError("office_translation_limit_exceeded", ProcessingMessages.CellTextLimit(slotText.Length, _options.MaxCellTextChars)) { Index = i });
                            continue;
                        }
                    }

                    decodedUnits[i] = new OfficeDecodedUnit(i, rawText, new[] { slotText });
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(rawText))
                    {
                        errors.Add(new FileError(SkipCodes.EmptyTranslation, ProcessingMessages.EmptyTranslation) { Index = i });
                        continue;
                    }

                    if (!TryParseStructured(rawText, unit, format, i, out var decodedSlots, out var error))
                    {
                        errors.Add(error!);
                        continue;
                    }

                    decodedUnits[i] = new OfficeDecodedUnit(i, rawText, decodedSlots!);
                }
            }

        var fatal = errors.Where(e => e.Code is "translation_too_long" or "office_translation_limit_exceeded" or "office_plan_limit_exceeded" ||
            e.Index is int index && texts[index] is null).ToArray();
        if (fatal.Length > 0) return OfficeDecodeResult.Failure(fatal);
        return OfficeDecodeResult.Success(decodedUnits) with
        {
            Skipped = errors.Select(e => new SkipMetadata(e.Code, SkipSeverity.Warning, SkipStage.Translation, SkipScope.Unit, 1,
                e.Message, OfficeMetadata.Location(units[e.Index!.Value].Location), e.Index)).ToArray()
        };
    }

    /// <summary>
    /// Parses and decodes structured tokenized translation.
    /// </summary>
    /// <param name="input">Encoded structured input.</param>
    /// <param name="unit">Original translation unit.</param>
    /// <param name="format">Office document format.</param>
    /// <param name="unitIndex">Zero-based unit index.</param>
    /// <param name="decodedSlots">Extracted decoded slot values when parsing succeeds.</param>
    /// <param name="error">File error if validation fails.</param>
    /// <returns>True when parsing succeeds; otherwise false.</returns>
    private bool TryParseStructured(
        string input,
        OfficeTranslationUnit unit,
        OfficeFormat format,
        int unitIndex,
        out IReadOnlyList<string>? decodedSlots,
        out FileError? error)
    {
        decodedSlots = null;
        error = null;

        var slots = new List<string>(unit.Slots.Count);
        var expectedOrder = ReadOrder(unit.EncodedSource);
        var tokenIndex = 0;
        var expectedSlotIndex = 0;
        var expectedAnchorIndex = 0;
        var totalTokens = unit.Slots.Count + unit.Anchors.Count;

        if (totalTokens > _options.MaxTokensPerUnit)
        {
            error = new FileError("office_plan_limit_exceeded", ProcessingMessages.UnitTokenLimit(totalTokens, _options.MaxTokensPerUnit)) { Index = unitIndex };
            return false;
        }

        var pos = 0;
        var len = input.Length;
        var totalCellChars = 0;

        while (pos < len)
        {
            if (input[pos] != '<')
            {
                error = new FileError(SkipCodes.OfficeTokenMismatch, ProcessingMessages.TextOutsideToken) { Index = unitIndex };
                return false;
            }

            // Must start with <ox:
            if (pos + 4 >= len || input[pos + 1] != 'o' || input[pos + 2] != 'x' || input[pos + 3] != ':')
            {
                error = new FileError(SkipCodes.OfficeTokenMismatch, ProcessingMessages.InvalidTokenPrefix) { Index = unitIndex };
                return false;
            }

            pos += 4; // Skip <ox:
            var tagStart = pos;

            while (pos < len && input[pos] != '>' && input[pos] != '/')
                pos++;

            if (pos >= len)
            {
                error = new FileError(SkipCodes.OfficeTokenMismatch, ProcessingMessages.UnterminatedToken) { Index = unitIndex };
                return false;
            }

            var tagName = input[tagStart..pos];
            if (tokenIndex >= expectedOrder.Count || expectedOrder[tokenIndex++] != tagName)
            {
                error = new FileError(SkipCodes.OfficeTokenMismatch, ProcessingMessages.TokenOrder) { Index = unitIndex };
                return false;
            }

            if (input[pos] == '/' && pos + 1 < len && input[pos + 1] == '>')
            {
                // Self-closing tag <ox:kN/>
                pos += 2;
                if (!tagName.StartsWith('k'))
                {
                    error = new FileError(SkipCodes.OfficeTokenMismatch, ProcessingMessages.InvalidSelfClosingToken(tagName)) { Index = unitIndex, Marker = tagName };
                    return false;
                }

                if (expectedAnchorIndex >= unit.Anchors.Count || unit.Anchors[expectedAnchorIndex].AnchorId != tagName)
                {
                    error = new FileError(SkipCodes.OfficeTokenMismatch, ProcessingMessages.MismatchedAnchor(tagName)) { Index = unitIndex, Marker = tagName };
                    return false;
                }

                expectedAnchorIndex++;
            }
            else if (input[pos] == '>')
            {
                // Opening slot tag <ox:rN>
                pos++;
                if (!tagName.StartsWith('r'))
                {
                    error = new FileError(SkipCodes.OfficeTokenMismatch, ProcessingMessages.InvalidOpeningToken(tagName)) { Index = unitIndex, Marker = tagName };
                    return false;
                }

                if (expectedSlotIndex >= unit.Slots.Count || unit.Slots[expectedSlotIndex].SlotId != tagName)
                {
                    error = new FileError(SkipCodes.OfficeTokenMismatch, ProcessingMessages.MismatchedSlot(tagName)) { Index = unitIndex, Marker = tagName };
                    return false;
                }

                // Read slot content until </ox:rN>
                var closingTag = $"</ox:{tagName}>";
                var slotContentSb = new StringBuilder();
                var closed = false;

                while (pos < len)
                {
                    if (input[pos] == '\\')
                    {
                        if (pos + 1 >= len)
                        {
                            error = new FileError(SkipCodes.OfficeTokenMismatch, ProcessingMessages.DanglingEscape) { Index = unitIndex, Marker = tagName };
                            return false;
                        }
                        var next = input[pos + 1];
                        if (next is '\\' or '<')
                        {
                            slotContentSb.Append(next);
                            pos += 2;
                        }
                        else
                        {
                            error = new FileError(SkipCodes.OfficeTokenMismatch, ProcessingMessages.InvalidEscape(next)) { Index = unitIndex, Marker = tagName };
                            return false;
                        }
                    }
                    else if (input[pos] == '<')
                    {
                        // Check if it's the closing tag
                        if (input.AsSpan(pos).StartsWith(closingTag, StringComparison.Ordinal))
                        {
                            pos += closingTag.Length;
                            closed = true;
                            break;
                        }
                        else
                        {
                            error = new FileError(SkipCodes.OfficeTokenMismatch, ProcessingMessages.UnescapedSlotTag) { Index = unitIndex, Marker = tagName };
                            return false;
                        }
                    }
                    else
                    {
                        slotContentSb.Append(input[pos]);
                        pos++;
                    }
                }

                if (!closed)
                {
                    error = new FileError(SkipCodes.OfficeTokenMismatch, ProcessingMessages.MissingClosingToken(closingTag)) { Index = unitIndex, Marker = tagName };
                    return false;
                }

                var slotText = slotContentSb.ToString();
                if (format != OfficeFormat.Excel && (slotText.Contains('\r') || slotText.Contains('\n') || slotText.Contains('\t')))
                {
                    error = new FileError(SkipCodes.InvalidTranslation, ProcessingMessages.InvalidSlotWhitespace(tagName)) { Index = unitIndex, Marker = tagName };
                    return false;
                }

                if (format == OfficeFormat.Excel)
                {
                    slotText = NormalizeExcelNewlines(slotText);
                    totalCellChars += slotText.Length;
                    if (totalCellChars > _options.MaxCellTextChars)
                    {
                        error = new FileError("office_translation_limit_exceeded", ProcessingMessages.TotalCellTextLimit(_options.MaxCellTextChars)) { Index = unitIndex, Marker = tagName };
                        return false;
                    }
                }

                slots.Add(slotText);
                expectedSlotIndex++;
            }
            else
            {
                error = new FileError(SkipCodes.OfficeTokenMismatch, ProcessingMessages.InvalidTokenSyntax) { Index = unitIndex };
                return false;
            }
        }

        if (expectedSlotIndex != unit.Slots.Count || expectedAnchorIndex != unit.Anchors.Count)
        {
            error = new FileError(SkipCodes.OfficeTokenMismatch, ProcessingMessages.TokenCount) { Index = unitIndex };
            return false;
        }

        if (slots.All(string.IsNullOrWhiteSpace))
        {
            error = new FileError(SkipCodes.EmptyTranslation, ProcessingMessages.EmptySlots) { Index = unitIndex };
            return false;
        }

        decodedSlots = slots;
        return true;
    }

    /// <summary>
    /// Reads canonical token order while skipping escaped source text.
    /// </summary>
    /// <param name="source">Canonical encoded source.</param>
    /// <returns>Opening slot and anchor identifiers.</returns>
    private static IReadOnlyList<string> ReadOrder(string source)
    {
        var result = new List<string>();
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '\\') { i++; continue; }
            if (!source.AsSpan(i).StartsWith("<ox:")) continue;
            var start = i + 4;
            var end = start;
            while (end < source.Length && source[end] != '>' && source[end] != '/') end++;
            result.Add(source[start..end]);
            i = end;
        }
        return result;
    }

    /// <summary>
    /// Validates UTF-16 surrogates and XML 1.0 character rules.
    /// </summary>
    /// <param name="text">Text to validate.</param>
    /// <returns>True when string contains only legal XML 1.0 characters and valid surrogate pairs.</returns>
    private static bool IsValidUnicodeAndXml(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsSurrogate(c))
            {
                if (!char.IsHighSurrogate(c) || i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                    return false;
                i++;
                continue;
            }

            // XML 1.0 valid chars: #x9 | #xA | #xD | [#x20-#xD7FF] | [#xE000-#xFFFD]
            if (c is '\t' or '\n' or '\r')
                continue;
            if (c < 0x20 || (c >= 0xD800 && c <= 0xDFFF) || c is '\uFFFE' or '\uFFFF')
                return false;
        }
        return true;
    }

    /// <summary>
    /// Normalizes CRLF and lone CR in Excel cell strings to standard LF.
    /// </summary>
    /// <param name="text">Cell text to normalize.</param>
    /// <returns>Normalized string using LF newlines.</returns>
    private static string NormalizeExcelNewlines(string text)
    {
        if (!text.Contains('\r'))
            return text;
        return text.Replace("\r\n", "\n").Replace('\r', '\n');
    }
}
