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

        var slots = template.Slots.ToDictionary(s => s.SlotId);
        var anchors = template.Anchors.ToDictionary(a => a.AnchorId);
        var order = template.Order ?? Enumerable.Range(0, Math.Max(template.Slots.Count, template.Anchors.Count))
            .SelectMany(i => (i < template.Slots.Count ? new[] { template.Slots[i].SlotId } : [])
                .Concat(i < template.Anchors.Count ? new[] { template.Anchors[i].AnchorId } : [])).ToArray();
        var parts = order.Select(id => new TranslationTokenPart(id, slots.TryGetValue(id, out var slot) ? slot.OriginalText : null)).ToArray();
        var scopes = order.Select(id => slots.TryGetValue(id, out var slot) ? slot.Scope ?? "s0" : anchors[id].Scope).ToArray();
        var encoded = TranslationTokenSyntax.Encode(parts, scopes);
        if (TranslationTokenParser.Parse(encoded).Count > _options.MaxTokensPerUnit)
            throw new FileLimitException("office_plan_limit_exceeded");
        return encoded;
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

                    decodedUnits[i] = new OfficeDecodedUnit(i, rawText, decodedSlots!) { Parts = TranslationTokenParser.Parse(rawText) };
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

        var failure = TranslationTokenParser.Validate(unit.EncodedSource, input);
        if (failure is not null)
        {
            error = new FileError(failure == "empty_translation" ? SkipCodes.EmptyTranslation : SkipCodes.OfficeTokenMismatch, ProcessingMessages.TokenOrder) { Index = unitIndex };
            return false;
        }
        var parts = TranslationTokenParser.Parse(input);
        if (parts.Count > _options.MaxTokensPerUnit)
        {
            error = new FileError("office_plan_limit_exceeded", ProcessingMessages.UnitTokenLimit(parts.Count, _options.MaxTokensPerUnit)) { Index = unitIndex };
            return false;
        }
        var values = parts.Where(p => p.Text is not null).ToDictionary(p => p.Id, p => p.Text!);
        if (format != OfficeFormat.Excel && values.Values.Any(t => t.IndexOfAny(['\r', '\n', '\t']) >= 0))
        {
            error = new FileError(SkipCodes.InvalidTranslation, ProcessingMessages.RawOfficeWhitespace) { Index = unitIndex };
            return false;
        }
        decodedSlots = unit.Slots.Select(s => format == OfficeFormat.Excel ? NormalizeExcelNewlines(values[s.SlotId]) : values[s.SlotId]).ToArray();
        if (format == OfficeFormat.Excel && decodedSlots.Sum(s => s.Length) > _options.MaxCellTextChars)
        {
            error = new FileError("office_translation_limit_exceeded", ProcessingMessages.TotalCellTextLimit(_options.MaxCellTextChars)) { Index = unitIndex };
            return false;
        }
        return true;
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
