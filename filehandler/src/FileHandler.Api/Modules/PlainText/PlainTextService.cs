using System.Text;
using FileHandler.Api.Common;
using FileHandler.Api.Diagnostics;
using Microsoft.Extensions.Options;

namespace FileHandler.Api.Modules.PlainText;

/// <summary>
/// Imports literal paragraphs and applies translations to UTF-8 plain text.
/// </summary>
public sealed class PlainTextService : IFileHandler
{

    /// <summary>
    /// UTF-8 plain text response media type.
    /// </summary>
    public const string ContentType = "text/plain; charset=utf-8";

    /// <summary>
    /// Configured file and translation limits.
    /// </summary>
    private readonly FileHandlingOptions _options;

    /// <summary>
    /// Creates plain text service with shared file handling limits.
    /// </summary>
    /// <param name="options">Source, unit, translation and output limits.</param>
    public PlainTextService(IOptions<FileHandlingOptions> options) => _options = options.Value;

    /// <summary>
    /// Extracts literal paragraphs separated by whitespace-only lines.
    /// </summary>
    /// <param name="source">Readable source stream, left open.</param>
    /// <param name="cancellationToken">Token for cancelling import.</param>
    /// <returns>Ordered paragraph strings, or errors with no partial texts.</returns>
    public Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default) =>
        DebugTrace.TraceAsync<ImportResult>("PlainTextService", "ImportAsync", () => new { source, cancellationToken }, async trace =>
        {
            trace.State("stage", () => "readSource");
            var (document, error) = await Utf8TextReader.ReadAsync(source, _options.MaxFileBytes, cancellationToken);
            if (error is not null)
                return new([], [error]);
            trace.State("stage", () => "segmentUnits");
            var (units, segmentError) = PlainTextSegmenter.Segment(document!.Text, _options.MaxUnits, cancellationToken);
            if (segmentError is not null)
                return new([], [segmentError]);
            trace.State("stage", () => "extractTexts");
            var texts = new string[units.Count];
            for (var i = 0; i < units.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var unit = units[i];
                texts[i] = document.Text[unit.Start..unit.End];
            }
            return new(texts, []);
        });

    /// <summary>
    /// Replaces paragraph spans literally while preserving BOM and source separators.
    /// </summary>
    /// <param name="source">Readable source stream, left open.</param>
    /// <param name="translatedTexts">Nonempty translations in original paragraph order.</param>
    /// <param name="cancellationToken">Token for cancelling export.</param>
    /// <returns>UTF-8 output bytes, or validation errors with null content.</returns>
    public Task<ExportResult> ExportAsync(Stream source, IReadOnlyList<string> translatedTexts, CancellationToken cancellationToken = default) =>
        DebugTrace.TraceAsync<ExportResult>("PlainTextService", "ExportAsync", () => new { source, translatedTexts, cancellationToken }, async trace =>
        {
            trace.State("stage", () => "readSource");
            var (document, error) = await Utf8TextReader.ReadAsync(source, _options.MaxFileBytes, cancellationToken);
            if (error is not null)
                return new(null, ContentType, [error]);
            trace.State("stage", () => "segmentUnits");
            var (units, segmentError) = PlainTextSegmenter.Segment(document!.Text, _options.MaxUnits, cancellationToken);
            if (segmentError is not null)
                return new(null, ContentType, [segmentError]);
            trace.State("stage", () => "validateTranslations");
            var errors = ValidateTranslations(units, translatedTexts, cancellationToken);
            if (errors.Count > 0)
                return new(null, ContentType, errors);
            trace.State("stage", () => "checkOutputLimit");
            if (!FitsOutputLimit(document, units, translatedTexts, cancellationToken))
                return new(null, ContentType, [new("output_too_large", $"Kết quả vượt giới hạn {_options.MaxOutputBytes} byte.")]);
            trace.State("stage", () => "composeOutput");
            var bytes = Compose(document, units, translatedTexts, cancellationToken);
            return new(bytes, ContentType, []);
        });

    /// <summary>
    /// Checks translation count, nonempty text, UTF-16 validity and character limits.
    /// </summary>
    /// <param name="units">Original paragraph spans.</param>
    /// <param name="translations">Translated strings in source order.</param>
    /// <param name="cancellationToken">Token for cancelling validation.</param>
    /// <returns>Errors with zero-based indices and one-based source line ranges.</returns>
    private IReadOnlyList<FileError> ValidateTranslations(IReadOnlyList<PlainTextUnit> units, IReadOnlyList<string> translations, CancellationToken cancellationToken) =>
        DebugTrace.Trace<IReadOnlyList<FileError>>("PlainTextService", "ValidateTranslations", () => new { unitCount = units.Count, translationCount = translations.Count, _options.MaxTranslationChars }, _ =>
        {
            var errors = new List<FileError>();
            if (translations.Count != units.Count)
                return [new("translation_count_mismatch", $"Cần {units.Count} bản dịch nhưng nhận được {translations.Count}.")];
            for (var i = 0; i < units.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var translation = translations[i];
                using var item = DebugTrace.Item(i + 1);
                item.State("unit", () => units[i]);
                item.State("translation", () => translation);
                var errorCount = errors.Count;
                if (translation is null)
                    errors.Add(new("invalid_translation", "Bản dịch không được null.", i, units[i].Line));
                else if (translation.Length > _options.MaxTranslationChars)
                    errors.Add(new("translation_too_long", $"Bản dịch vượt giới hạn {_options.MaxTranslationChars} ký tự.", i, units[i].Line));
                else if (string.IsNullOrWhiteSpace(translation))
                    errors.Add(new("empty_translation", "Bản dịch không được rỗng hoặc chỉ chứa khoảng trắng.", i, units[i].Line));
                else
                {
                    try
                    {
                        Utf8TextReader.GetByteCount(translation.AsSpan());
                    }
                    catch (EncoderFallbackException)
                    {
                        errors.Add(new("invalid_translation", "Bản dịch chứa chuỗi Unicode không hợp lệ.", i, units[i].Line));
                    }
                }
                if (errors.Count > errorCount)
                    item.State("error", () => errors[^1]);
            }
            return errors;
        });

    /// <summary>
    /// Checks total UTF-8 bytes before allocating output, including BOM and separators.
    /// </summary>
    /// <param name="source">Decoded source and original bytes.</param>
    /// <param name="units">Ordered source paragraph spans.</param>
    /// <param name="translations">Validated translations.</param>
    /// <param name="cancellationToken">Token for cancelling byte counting.</param>
    /// <returns>True when complete output fits configured byte limit.</returns>
    private bool FitsOutputLimit(Utf8TextSource source, IReadOnlyList<PlainTextUnit> units, IReadOnlyList<string> translations, CancellationToken cancellationToken) =>
        DebugTrace.Trace("PlainTextService", "FitsOutputLimit", () => new { unitCount = units.Count, source.HasBom, _options.MaxOutputBytes }, trace =>
        {
            long bytes = source.HasBom ? Encoding.UTF8.Preamble.Length : 0;
            trace.State("outputBytes", () => bytes);
            var offset = 0;
            for (var i = 0; i < units.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var unit = units[i];
                bytes += Utf8TextReader.GetByteCount(source.Text.AsSpan(offset, unit.Start - offset));
                bytes += Utf8TextReader.GetByteCount(translations[i].AsSpan());
                trace.State("outputBytes", () => bytes);
                if (bytes > _options.MaxOutputBytes)
                {
                    trace.State("unitIndex", () => i);
                    return false;
                }
                offset = unit.End;
            }
            cancellationToken.ThrowIfCancellationRequested();
            bytes += Utf8TextReader.GetByteCount(source.Text.AsSpan(offset));
            trace.State("outputBytes", () => bytes);
            return bytes <= _options.MaxOutputBytes;
        });

    /// <summary>
    /// Composes translations and untouched source spans in one forward pass.
    /// </summary>
    /// <param name="source">Decoded source and original BOM.</param>
    /// <param name="units">Ordered paragraph spans.</param>
    /// <param name="translations">Validated translations within output budget.</param>
    /// <param name="cancellationToken">Token for cancelling output composition.</param>
    /// <returns>Original bytes for identity export, otherwise composed UTF-8 bytes.</returns>
    private static byte[] Compose(Utf8TextSource source, IReadOnlyList<PlainTextUnit> units, IReadOnlyList<string> translations, CancellationToken cancellationToken) =>
        DebugTrace.Trace("PlainTextService", "Compose", () => new { source, unitCount = units.Count }, trace =>
        {
            StringBuilder? output = null;
            var offset = 0;
            for (var i = 0; i < units.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var unit = units[i];
                var translation = translations[i];
                using var item = DebugTrace.Item(i + 1);
                item.State("unit", () => unit);
                item.State("translation", () => translation);
                if (source.Text.AsSpan(unit.Start, unit.End - unit.Start).SequenceEqual(translation.AsSpan()))
                {
                    item.State("outcome", () => "unchanged");
                    continue;
                }
                output ??= new StringBuilder();
                output.Append(source.Text, offset, unit.Start - offset);
                output.Append(translation);
                offset = unit.End;
                item.State("outcome", () => "replaced");
            }
            cancellationToken.ThrowIfCancellationRequested();
            trace.State("identity", () => output is null);
            if (output is null)
                return source.Bytes;
            output.Append(source.Text, offset, source.Text.Length - offset);
            var bytes = Utf8TextReader.Encode(output.ToString(), source.HasBom);
            cancellationToken.ThrowIfCancellationRequested();
            return bytes;
        });
}
