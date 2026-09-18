using System.Text;
using FileHandler.Api.Common;
using FileHandler.Api.Diagnostics;

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
        using var trace = DebugTrace.Enter("OfficeTextCodec", "Encode", () => new
        {
            mode = template.Mode.ToString(),
            slotCount = template.Slots.Count,
            anchorCount = template.Anchors.Count
        });

        try
        {
            if (template.Mode == UnitMode.Plain)
            {
                var text = template.Slots.Count > 0 ? template.Slots[0].OriginalText : string.Empty;
                trace.State("sourceText", () => text);
                return trace.Return<string>(text);
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
                return trace.Return<string>(sb.ToString());
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

            var encoded = sb.ToString();
            trace.State("sourceText", () => encoded);
            return trace.Return<string>(encoded);
        }
        catch (Exception ex)
        {
            trace.Error(ex);
            throw;
        }
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
        using var trace = DebugTrace.Enter("OfficeTextCodec", "ValidateAndDecode", () => new
        {
            unitCount = units.Count,
            translationCount = texts.Count
        });

        try
        {
            trace.State("stage", () => "checkCount");
            if (texts.Count != units.Count)
            {
                var error = new FileError("translation_count_mismatch", $"Số lượng bản dịch ({texts.Count}) không khớp với số lượng đơn vị ({units.Count}).");
                trace.Return(new { outcome = "failed", errorCodes = new[] { error.Code } });
                return OfficeDecodeResult.Failure(new[] { error });
            }

            trace.State("stage", () => "decodeUnits");
            var errors = new List<FileError>();
            var decodedUnits = new List<OfficeDecodedUnit>(units.Count);
            long totalTranslationChars = 0;

            for (var i = 0; i < units.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (errors.Count >= _options.MaxErrors)
                    break;

                var unit = units[i];
                var rawText = texts[i];

                using var item = DebugTrace.Item(i + 1);
                item.State("encodedInput", () => rawText);

                if (rawText is null)
                {
                    errors.Add(new FileError("invalid_translation", "Bản dịch không được là null.") { Index = i });
                    continue;
                }

                if (rawText.Length > _fileHandlingOptions.MaxTranslationChars)
                {
                    errors.Add(new FileError("translation_too_long", $"Độ dài bản dịch ({rawText.Length}) vượt quá giới hạn ({_fileHandlingOptions.MaxTranslationChars}).") { Index = i });
                    continue;
                }

                totalTranslationChars += rawText.Length;
                if (totalTranslationChars > _options.MaxTotalTranslationChars)
                {
                    errors.Add(new FileError("office_translation_limit_exceeded", $"Tổng độ dài các chuỗi dịch ({totalTranslationChars}) vượt quá giới hạn ({_options.MaxTotalTranslationChars}).") { Index = i });
                    break;
                }

                if (!IsValidUnicodeAndXml(rawText))
                {
                    errors.Add(new FileError("invalid_translation", "Chuỗi dịch chứa ký tự Unicode hoặc ký tự điều khiển XML không hợp lệ.") { Index = i });
                    continue;
                }

                if (unit.Mode == UnitMode.Plain)
                {
                    if (string.IsNullOrWhiteSpace(rawText))
                    {
                        errors.Add(new FileError("empty_translation", "Bản dịch không được để trống hoặc chỉ chứa khoảng trắng.") { Index = i });
                        continue;
                    }

                    if (format != OfficeFormat.Excel && (rawText.Contains('\r') || rawText.Contains('\n') || rawText.Contains('\t')))
                    {
                        errors.Add(new FileError("invalid_translation", "Văn bản Word và PowerPoint không được chứa ký tự xuống dòng hoặc tab thô trong bản dịch.") { Index = i });
                        continue;
                    }

                    var slotText = rawText;
                    if (format == OfficeFormat.Excel)
                    {
                        slotText = NormalizeExcelNewlines(rawText);
                        if (slotText.Length > _options.MaxCellTextChars)
                        {
                            errors.Add(new FileError("office_translation_limit_exceeded", $"Độ dài ô Excel ({slotText.Length}) vượt quá giới hạn ({_options.MaxCellTextChars}).") { Index = i });
                            continue;
                        }
                    }

                    decodedUnits.Add(new OfficeDecodedUnit(i, rawText, new[] { slotText }));
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(rawText))
                    {
                        errors.Add(new FileError("empty_translation", "Bản dịch có cấu trúc không được rỗng.") { Index = i });
                        continue;
                    }

                    if (!TryParseStructured(rawText, unit, format, i, out var decodedSlots, out var error))
                    {
                        errors.Add(error!);
                        continue;
                    }

                    decodedUnits.Add(new OfficeDecodedUnit(i, rawText, decodedSlots!));
                }
            }

            if (errors.Count > 0)
            {
                trace.Return(new { outcome = "failed", errorCount = errors.Count });
                return OfficeDecodeResult.Failure(errors);
            }

            trace.Return(new { outcome = "success", decodedCount = decodedUnits.Count });
            return OfficeDecodeResult.Success(decodedUnits);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            trace.Error(ex);
            throw;
        }
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
            error = new FileError("office_plan_limit_exceeded", $"Số lượng token của đơn vị ({totalTokens}) vượt quá giới hạn ({_options.MaxTokensPerUnit}).") { Index = unitIndex };
            return false;
        }

        var pos = 0;
        var len = input.Length;
        var totalCellChars = 0;

        while (pos < len)
        {
            if (input[pos] != '<')
            {
                error = new FileError("office_token_mismatch", "Ký tự văn bản nằm ngoài thẻ token không được cho phép.") { Index = unitIndex };
                return false;
            }

            // Must start with <ox:
            if (pos + 4 >= len || input[pos + 1] != 'o' || input[pos + 2] != 'x' || input[pos + 3] != ':')
            {
                error = new FileError("office_token_mismatch", "Thẻ không hợp lệ; mọi token phải bắt đầu bằng '<ox:'.") { Index = unitIndex };
                return false;
            }

            pos += 4; // Skip <ox:
            var tagStart = pos;

            while (pos < len && input[pos] != '>' && input[pos] != '/')
                pos++;

            if (pos >= len)
            {
                error = new FileError("office_token_mismatch", "Thẻ token không được đóng đúng cú pháp.") { Index = unitIndex };
                return false;
            }

            var tagName = input[tagStart..pos];
            if (tokenIndex >= expectedOrder.Count || expectedOrder[tokenIndex++] != tagName)
            {
                error = new FileError("office_token_mismatch", "Thứ tự token không khớp với nguồn.") { Index = unitIndex };
                return false;
            }

            if (input[pos] == '/' && pos + 1 < len && input[pos + 1] == '>')
            {
                // Self-closing tag <ox:kN/>
                pos += 2;
                if (!tagName.StartsWith('k'))
                {
                    error = new FileError("office_token_mismatch", $"Thẻ tự đóng không hợp lệ: <ox:{tagName}/>.") { Index = unitIndex, Marker = tagName };
                    return false;
                }

                if (expectedAnchorIndex >= unit.Anchors.Count || unit.Anchors[expectedAnchorIndex].AnchorId != tagName)
                {
                    error = new FileError("office_token_mismatch", $"Anchor token không khớp với mẫu gốc: gặp {tagName}.") { Index = unitIndex, Marker = tagName };
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
                    error = new FileError("office_token_mismatch", $"Thẻ mở không hợp lệ: <ox:{tagName}>.") { Index = unitIndex, Marker = tagName };
                    return false;
                }

                if (expectedSlotIndex >= unit.Slots.Count || unit.Slots[expectedSlotIndex].SlotId != tagName)
                {
                    error = new FileError("office_token_mismatch", $"Slot token không khớp với mẫu gốc: gặp {tagName}.") { Index = unitIndex, Marker = tagName };
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
                            error = new FileError("office_token_mismatch", "Ký tự escape '\\' bị bỏ lửng ở cuối chuỗi.") { Index = unitIndex, Marker = tagName };
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
                            error = new FileError("office_token_mismatch", $"Ký tự escape '\\{next}' không hợp lệ; chỉ '\\\\' và '\\<' được cho phép.") { Index = unitIndex, Marker = tagName };
                            return false;
                        }
                    }
                    else if (input[pos] == '<')
                    {
                        // Check if it's the closing tag
                        if (pos + closingTag.Length <= len && input.Substring(pos, closingTag.Length) == closingTag)
                        {
                            pos += closingTag.Length;
                            closed = true;
                            break;
                        }
                        else
                        {
                            error = new FileError("office_token_mismatch", "Ký tự '<' bên trong slot phải được escape dạng '\\<'.") { Index = unitIndex, Marker = tagName };
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
                    error = new FileError("office_token_mismatch", $"Thiếu thẻ đóng {closingTag}.") { Index = unitIndex, Marker = tagName };
                    return false;
                }

                var slotText = slotContentSb.ToString();
                if (string.IsNullOrWhiteSpace(slotText))
                {
                    error = new FileError("empty_translation", $"Slot {tagName} không được để trống hoặc chỉ chứa khoảng trắng.") { Index = unitIndex, Marker = tagName };
                    return false;
                }

                if (format != OfficeFormat.Excel && (slotText.Contains('\r') || slotText.Contains('\n') || slotText.Contains('\t')))
                {
                    error = new FileError("invalid_translation", $"Slot {tagName} chứa ký tự xuống dòng hoặc tab không được phép trong Word/PowerPoint.") { Index = unitIndex, Marker = tagName };
                    return false;
                }

                if (format == OfficeFormat.Excel)
                {
                    slotText = NormalizeExcelNewlines(slotText);
                    totalCellChars += slotText.Length;
                    if (totalCellChars > _options.MaxCellTextChars)
                    {
                        error = new FileError("office_translation_limit_exceeded", $"Tổng độ dài ô Excel vượt quá giới hạn ({_options.MaxCellTextChars}).") { Index = unitIndex, Marker = tagName };
                        return false;
                    }
                }

                slots.Add(slotText);
                expectedSlotIndex++;
            }
            else
            {
                error = new FileError("office_token_mismatch", "Cú pháp token không hợp lệ.") { Index = unitIndex };
                return false;
            }
        }

        if (expectedSlotIndex != unit.Slots.Count || expectedAnchorIndex != unit.Anchors.Count)
        {
            error = new FileError("office_token_mismatch", "Số lượng hoặc thứ tự token không khớp với mẫu gốc của đơn vị dịch.") { Index = unitIndex };
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
