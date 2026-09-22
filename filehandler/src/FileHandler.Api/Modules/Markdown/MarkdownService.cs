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
        var metadata = FileMetadata.Create("markdown");
        var (document, error) = await MarkdownSourceReader.ReadAsync(source, _options.MaxFileBytes, cancellationToken);
        if (error is not null)
            return new([], [error]) { Metadata = metadata.ForExport(true) };
        var extraction = _extractor.Extract(document!, _options.MaxUnits, cancellationToken);
        metadata = Describe(extraction, debug && extraction.Errors.Count == 0, cancellationToken);
        return extraction.Errors.Count > 0
            ? new([], extraction.Errors) { Metadata = metadata with { Status = ProcessingStatus.Failed } }
            : new(extraction.Units.Select(x => x.Text).ToArray(), []) { Metadata = metadata };
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
        var metadata = FileMetadata.Create("markdown");
        var (document, error) = await MarkdownSourceReader.ReadAsync(source, _options.MaxFileBytes, cancellationToken);
        if (error is not null)
            return new(null, ContentType, [error]) { Metadata = metadata.ForExport(true) };
        var extraction = _extractor.Extract(document!, _options.MaxUnits, cancellationToken);
        metadata = Describe(extraction, false, cancellationToken).ForExport();
        var (text, errors, skipped) = MarkdownTranslationApplier.Apply(extraction, translations, _options, cancellationToken, validateStructure: _extractor.ValidateStructure);
        metadata = (metadata with { Skipped = metadata.Skipped.Concat(skipped).ToArray() }).ForExport();
        if (errors.Count > 0)
            return new(null, ContentType, errors) { Metadata = metadata.ForExport(true) };
        var structureErrors = _extractor.ValidateStructure(document!.Text, text!);
        if (structureErrors.Count > 0)
        {
            var baseline = MarkdownTranslationApplier.Apply(extraction, translations, _options, cancellationToken, validationBaseline: true);
            if (baseline.Errors.Count > 0)
                return new(null, ContentType, baseline.Errors) { Metadata = metadata.ForExport(true) };
            structureErrors = _extractor.ValidateStructure(baseline.Text!, text!);
        }
        if (structureErrors.Count > 0)
            return new(null, ContentType, structureErrors) { Metadata = metadata.ForExport(true) };
        var bytes = text == document!.Text ? document.Bytes : MarkdownSourceReader.Encode(text!, document.HasBom);
        if (bytes.LongLength > _options.MaxOutputBytes)
            return new(null, ContentType, [new("output_too_large", ProcessingMessages.OutputLimit(_options.MaxOutputBytes))]) { Metadata = metadata.ForExport(true) };
        return new(bytes, ContentType, []) { Metadata = metadata };
    }

    /// <summary>
    /// Projects source units and exclusions without internal markers.
    /// </summary>
    /// <param name="extraction">Source extraction facts.</param>
    /// <param name="debug">Whether to build public unit metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Metadata with skips and optional public source mapping.</returns>
    private static FileMetadata Describe(MarkdownExtraction extraction, bool debug, CancellationToken cancellationToken) => FileMetadata.Create("markdown") with
    {
        UnitCount = extraction.Errors.Count == 0 ? extraction.Units.Count : null,
        Units = debug ? extraction.Units.Select((unit, index) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new UnitMetadata(index, unit.IsHeading ? "heading" : unit.IsMermaidLabel ? "mermaidLabel" : "paragraph", new(Line: unit.Line));
        }).ToArray() : null,
        Skipped = extraction.Skipped,
        Status = ProcessingStatus.Resolve(extraction.Skipped)
    };
}
