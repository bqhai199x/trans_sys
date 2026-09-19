using FileHandler.Api.Common;
using Microsoft.Extensions.Options;

namespace FileHandler.Api.Modules.Markdown;

/// <summary>
/// Orchestrates Markdown extraction, translation validation, and export workflows.
/// </summary>
public sealed class MarkdownService : IFileHandler
{

    /// <summary>
    /// UTF-8 Markdown response media type.
    /// </summary>
    public const string ContentType = "text/markdown; charset=utf-8";

    /// <summary>
    /// Configured processing limits.
    /// </summary>
    private readonly FileHandlingOptions _options;

    /// <summary>
    /// Extractor for Markdown translation units.
    /// </summary>
    private readonly IMarkdownExtractor _extractor;

    /// <summary>
    /// Creates Markdown service with configured limits and parsing rules.
    /// </summary>
    /// <param name="options">File processing limits.</param>
    /// <param name="extractor">Markdown extractor for translation unit extraction and structure validation.</param>
    internal MarkdownService(IOptions<FileHandlingOptions> options, IMarkdownExtractor extractor)
    {
        _options = options.Value;
        _extractor = extractor;
    }

    /// <summary>
    /// Creates Markdown service with default pipeline extractor for testing.
    /// </summary>
    /// <param name="options">File processing limits.</param>
    /// <returns>Configured service with default Markdown extractor.</returns>
    internal static MarkdownService Create(IOptions<FileHandlingOptions> options) =>
        new(options, new MarkdownExtractor(MarkdownProfile.CreatePipeline()));

    /// <summary>
    /// Extracts translatable text from source stream.
    /// </summary>
    /// <param name="source">Readable source stream.</param>
    /// <param name="cancellationToken">Token for cancelling this operation.</param>
    /// <returns>Task containing extracted texts and validation errors.</returns>
    public async Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default)
    {
        var (document, error) = await MarkdownSourceReader.ReadAsync(source, _options.MaxFileBytes, cancellationToken);
        if (error is not null)
            return new([], [error]);
        var extraction = _extractor.Extract(document!, _options.MaxUnits, cancellationToken);
        return extraction.Errors.Count > 0 ? new([], extraction.Errors) : new(extraction.Units.Select(x => x.Text).ToArray(), []);
    }

    /// <summary>
    /// Applies translations to source stream and returns exported file.
    /// </summary>
    /// <param name="source">Readable source stream.</param>
    /// <param name="translations">Translated units in source order.</param>
    /// <param name="cancellationToken">Token for cancelling this operation.</param>
    /// <returns>Task containing exported bytes, media type, and validation errors.</returns>
    public async Task<ExportResult> ExportAsync(Stream source, IReadOnlyList<string> translations, CancellationToken cancellationToken = default)
    {
        var (document, error) = await MarkdownSourceReader.ReadAsync(source, _options.MaxFileBytes, cancellationToken);
        if (error is not null)
            return new(null, ContentType, [error]);
        var extraction = _extractor.Extract(document!, _options.MaxUnits, cancellationToken);
        var (text, errors) = MarkdownTranslationApplier.Apply(extraction, translations, _options, cancellationToken);
        if (errors.Count > 0)
            return new(null, ContentType, errors);
        var structureErrors = _extractor.ValidateStructure(document!.Text, text!);
        if (structureErrors.Count > 0)
        {
            var baseline = MarkdownTranslationApplier.Apply(extraction, translations, _options, cancellationToken, validationBaseline: true);
            if (baseline.Errors.Count > 0)
                return new(null, ContentType, baseline.Errors);
            structureErrors = _extractor.ValidateStructure(baseline.Text!, text!);
        }
        if (structureErrors.Count > 0)
            return new(null, ContentType, structureErrors);
        var bytes = MarkdownSourceReader.Encode(text!, document!.HasBom);
        if (bytes.LongLength > _options.MaxOutputBytes)
            return new(null, ContentType, [new("output_too_large", $"Kết quả vượt giới hạn {_options.MaxOutputBytes} byte.")]);
        return new(bytes, ContentType, []);
    }
}
