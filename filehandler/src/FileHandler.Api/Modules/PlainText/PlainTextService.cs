using System.Text;
using FileHandler.Api.Common;
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
        ImportAsync(source, false, cancellationToken);

    /// <summary>
    /// Imports source while constructing public unit metadata only when requested.
    /// </summary>
    /// <param name="source">Caller-owned readable source stream.</param>
    /// <param name="debug">Whether to return informational skips and build diagnostic unit mapping.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Texts, skips and optional public unit mapping, or fatal errors.</returns>
    public async Task<ImportResult> ImportAsync(Stream source, bool debug, CancellationToken cancellationToken)
    {
        var result = await ImportCoreAsync(source, debug, cancellationToken).ConfigureAwait(false);
        return result with { Metadata = result.Metadata.ForResponse(debug) };
    }

    /// <summary>
    /// Runs import with complete internal skip facts for preservation validation.
    /// </summary>
    /// <param name="source">Caller-owned source stream.</param>
    /// <param name="debug">Whether to construct diagnostic unit mapping.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Processing result before public skip projection.</returns>
    private async Task<ImportResult> ImportCoreAsync(Stream source, bool debug, CancellationToken cancellationToken)
    {
        var metadata = FileMetadata.Create("plaintext");
        var (document, error) = await Utf8TextReader.ReadAsync(source, _options.MaxFileBytes, cancellationToken);
        if (error is not null)
            return new([], [error]) { Metadata = metadata.ForExport(true) };
        var (units, segmentError) = PlainTextSegmenter.Segment(document!.Text, _options.MaxUnits, cancellationToken);
        if (segmentError is not null)
            return new([], [segmentError]) { Metadata = metadata.ForExport(true) };
        metadata = Describe(units, debug, cancellationToken);
        var texts = new string[units.Count];
        for (var i = 0; i < units.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var unit = units[i];
            texts[i] = document.Text[unit.Start..unit.End];
        }
        return new(texts, []) { Metadata = metadata };
    }

    /// <summary>
    /// Replaces paragraph spans literally while preserving BOM and source separators.
    /// </summary>
    /// <param name="source">Readable source stream, left open.</param>
    /// <param name="translations">Nonempty translations in original paragraph order.</param>
    /// <param name="cancellationToken">Token for cancelling export.</param>
    /// <returns>UTF-8 output bytes, or validation errors with null content.</returns>
    public async Task<ExportResult> ExportAsync(Stream source, IReadOnlyList<string> translations, CancellationToken cancellationToken = default)
    {
        var result = await ExportCoreAsync(source, translations, cancellationToken).ConfigureAwait(false);
        return result with { Metadata = result.Metadata.ForResponse(false) };
    }

    /// <summary>
    /// Runs export with complete internal skip facts for preservation validation.
    /// </summary>
    /// <param name="source">Caller-owned source stream.</param>
    /// <param name="translations">Translations in source mapping order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Processing result before public skip projection.</returns>
    private async Task<ExportResult> ExportCoreAsync(Stream source, IReadOnlyList<string> translations, CancellationToken cancellationToken)
    {
        var metadata = FileMetadata.Create("plaintext");
        var (document, error) = await Utf8TextReader.ReadAsync(source, _options.MaxFileBytes, cancellationToken);
        if (error is not null)
            return new(null, ContentType, [error]) { Metadata = metadata.ForExport(true) };
        var (units, segmentError) = PlainTextSegmenter.Segment(document!.Text, _options.MaxUnits, cancellationToken);
        if (segmentError is not null)
            return new(null, ContentType, [segmentError]) { Metadata = metadata.ForExport(true) };
        metadata = Describe(units, false, cancellationToken).ForExport();
        var errors = ValidateTranslations(units, translations, cancellationToken);
        var fatal = errors.Where(e => e.Code is "translation_count_mismatch" or "translation_too_long" ||
            e.Index is int i && translations[i] is null).ToArray();
        if (fatal.Length > 0)
            return new(null, ContentType, fatal) { Metadata = metadata.ForExport(true) };
        var effective = errors.Count == 0 ? translations : translations.ToArray();
        var skipped = new List<SkipMetadata>();
        foreach (var issue in errors)
        {
            var index = issue.Index!.Value;
            ((string[])effective)[index] = document.Text[units[index].Start..units[index].End];
            skipped.Add(new(issue.Code, SkipSeverity.Warning, SkipStage.Translation, SkipScope.Unit, 1, issue.Message,
                new(Line: units[index].Line), index));
        }
        metadata = (metadata with { Skipped = skipped }).ForExport();
        if (!FitsOutputLimit(document, units, effective, cancellationToken))
            return new(null, ContentType, [new("output_too_large", ProcessingMessages.OutputLimit(_options.MaxOutputBytes))]) { Metadata = metadata.ForExport(true) };
        var bytes = Compose(document, units, effective, cancellationToken);
        return new(bytes, ContentType, []) { Metadata = metadata };
    }

    /// <summary>
    /// Projects paragraph spans without exposing internal offsets or BOM.
    /// </summary>
    /// <param name="units">Source paragraphs in mapping order.</param>
    /// <param name="debug">Whether to build public unit metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Metadata with mapping size and optional public source mapping.</returns>
    private static FileMetadata Describe(IReadOnlyList<PlainTextUnit> units, bool debug, CancellationToken cancellationToken) => FileMetadata.Create("plaintext") with
    {
        UnitCount = units.Count,
        Units = debug ? units.Select((unit, index) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new UnitMetadata(index, "paragraph", new(Line: unit.Line));
        }).ToArray() : null
    };

    /// <summary>
    /// Checks translation count, nonempty text, UTF-16 validity and character limits.
    /// </summary>
    /// <param name="units">Original paragraph spans.</param>
    /// <param name="translations">Translated strings in source order.</param>
    /// <param name="cancellationToken">Token for cancelling validation.</param>
    /// <returns>Errors with zero-based indices and one-based source line ranges.</returns>
    private IReadOnlyList<FileError> ValidateTranslations(IReadOnlyList<PlainTextUnit> units, IReadOnlyList<string> translations, CancellationToken cancellationToken)
    {
        var errors = new List<FileError>();
        if (translations.Count != units.Count)
            return [new("translation_count_mismatch", ProcessingMessages.TranslationCountMismatch(units.Count, translations.Count))];
        for (var i = 0; i < units.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var translation = translations[i];
            if (translation is null)
                errors.Add(new(SkipCodes.InvalidTranslation, ProcessingMessages.NullTranslation, i, units[i].Line));
            else if (translation.Length > _options.MaxTranslationChars)
                errors.Add(new("translation_too_long", ProcessingMessages.TranslationLimit(_options.MaxTranslationChars), i, units[i].Line));
            else if (string.IsNullOrWhiteSpace(translation))
                errors.Add(new(SkipCodes.EmptyTranslation, ProcessingMessages.EmptyTranslation, i, units[i].Line));
            else
            {
                try
                {
                    Utf8TextReader.GetByteCount(translation.AsSpan());
                }
                catch (EncoderFallbackException)
                {
                    errors.Add(new(SkipCodes.InvalidTranslation, ProcessingMessages.InvalidUnicode, i, units[i].Line));
                }
            }
        }
        return errors;
    }

    /// <summary>
    /// Checks total UTF-8 bytes before allocating output, including BOM and separators.
    /// </summary>
    /// <param name="source">Decoded source and original bytes.</param>
    /// <param name="units">Ordered source paragraph spans.</param>
    /// <param name="translations">Validated translations.</param>
    /// <param name="cancellationToken">Token for cancelling byte counting.</param>
    /// <returns>True when complete output fits configured byte limit.</returns>
    private bool FitsOutputLimit(Utf8TextSource source, IReadOnlyList<PlainTextUnit> units, IReadOnlyList<string> translations, CancellationToken cancellationToken)
    {
        long bytes = source.HasBom ? Encoding.UTF8.Preamble.Length : 0;
        var offset = 0;
        for (var i = 0; i < units.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var unit = units[i];
            bytes += Utf8TextReader.GetByteCount(source.Text.AsSpan(offset, unit.Start - offset));
            bytes += Utf8TextReader.GetByteCount(translations[i].AsSpan());
            if (bytes > _options.MaxOutputBytes)
                return false;
            offset = unit.End;
        }
        cancellationToken.ThrowIfCancellationRequested();
        bytes += Utf8TextReader.GetByteCount(source.Text.AsSpan(offset));
        return bytes <= _options.MaxOutputBytes;
    }

    /// <summary>
    /// Composes translations and untouched source spans in one forward pass.
    /// </summary>
    /// <param name="source">Decoded source and original BOM.</param>
    /// <param name="units">Ordered paragraph spans.</param>
    /// <param name="translations">Validated translations within output budget.</param>
    /// <param name="cancellationToken">Token for cancelling output composition.</param>
    /// <returns>Original bytes for identity export, otherwise composed UTF-8 bytes.</returns>
    private static byte[] Compose(Utf8TextSource source, IReadOnlyList<PlainTextUnit> units, IReadOnlyList<string> translations, CancellationToken cancellationToken)
    {
        StringBuilder? output = null;
        var offset = 0;
        for (var i = 0; i < units.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var unit = units[i];
            var translation = translations[i];
            if (source.Text.AsSpan(unit.Start, unit.End - unit.Start).SequenceEqual(translation.AsSpan()))
                continue;
            output ??= new StringBuilder();
            output.Append(source.Text, offset, unit.Start - offset);
            output.Append(translation);
            offset = unit.End;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (output is null)
            return source.Bytes;
        output.Append(source.Text, offset, source.Text.Length - offset);
        var bytes = Utf8TextReader.Encode(output.ToString(), source.HasBom);
        cancellationToken.ThrowIfCancellationRequested();
        return bytes;
    }
}
