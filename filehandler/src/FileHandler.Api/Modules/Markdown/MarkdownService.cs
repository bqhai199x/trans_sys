using FileHandler.Api.Common;
using FileHandler.Api.Diagnostics;
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
        using var trace = DebugTrace.Enter("MarkdownService", "ImportAsync", () => new { source, cancellationToken });
        try
        {
            trace.State("stage", () => "readSource");
            var (document, error) = await MarkdownSourceReader.ReadAsync(source, _options.MaxFileBytes, cancellationToken);
            if (error is not null)
                return trace.Return<ImportResult>(new([], [error]));
            trace.State("stage", () => "extractUnits");
            var extraction = _extractor.Extract(document!, _options.MaxUnits, cancellationToken);
            return trace.Return<ImportResult>(extraction.Errors.Count > 0 ? new([], extraction.Errors) : new(extraction.Units.Select(x => x.Text).ToArray(), []));
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
    }

    /// <summary>
    /// Applies translations to source stream and returns exported file.
    /// </summary>
    /// <param name="source">Readable source stream.</param>
    /// <param name="translatedTexts">Translated units in source order.</param>
    /// <param name="cancellationToken">Token for cancelling this operation.</param>
    /// <returns>Task containing exported bytes, media type, and validation errors.</returns>
    public async Task<ExportResult> ExportAsync(Stream source, IReadOnlyList<string> translatedTexts, CancellationToken cancellationToken = default)
    {
        using var trace = DebugTrace.Enter("MarkdownService", "ExportAsync", () => new { source, translatedTexts, cancellationToken });
        try
        {
            trace.State("stage", () => "readSource");
            var (document, error) = await MarkdownSourceReader.ReadAsync(source, _options.MaxFileBytes, cancellationToken);
            if (error is not null)
                return trace.Return<ExportResult>(new(null, ContentType, [error]));
            trace.State("stage", () => "extractUnits");
            var extraction = _extractor.Extract(document!, _options.MaxUnits, cancellationToken);
            trace.State("stage", () => "applyTranslations");
            var (text, errors) = MarkdownTranslationApplier.Apply(extraction, translatedTexts, _options, cancellationToken);
            if (errors.Count > 0)
                return trace.Return<ExportResult>(new(null, ContentType, errors));
            trace.State("stage", () => "validateStructure");
            var structureErrors = _extractor.ValidateStructure(document!.Text, text!);
            if (structureErrors.Count > 0)
                return trace.Return<ExportResult>(new(null, ContentType, structureErrors));
            trace.State("stage", () => "encodeOutput");
            var bytes = MarkdownSourceReader.Encode(text!, document!.HasBom);
            trace.State("outputBytes", () => bytes.LongLength);
            trace.State("maxOutputBytes", () => _options.MaxOutputBytes);
            if (bytes.LongLength > _options.MaxOutputBytes)
                return trace.Return<ExportResult>(new(null, ContentType, [new("output_too_large", $"Kết quả vượt giới hạn {_options.MaxOutputBytes} byte.")]));
            return trace.Return<ExportResult>(new(bytes, ContentType, []));
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
    }
}
