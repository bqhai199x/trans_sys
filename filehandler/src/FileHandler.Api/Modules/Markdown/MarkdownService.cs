using FileHandler.Api.Common;
using Microsoft.Extensions.Options;

namespace FileHandler.Api.Modules.Markdown;

public sealed class MarkdownService : IFileHandler
{
    public const string ContentType = "text/markdown; charset=utf-8";
    private readonly FileHandlingOptions _options;
    private readonly MarkdownExtractor _extractor;

    public MarkdownService(IOptions<FileHandlingOptions> options)
    {
        _options = options.Value;
        _extractor = new(MarkdownProfile.CreatePipeline());
    }

    public async Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default)
    {
        var (document, error) = await MarkdownSourceReader.ReadAsync(source, _options.MaxFileBytes, cancellationToken);
        if (error is not null) return new([], [error]);
        var extraction = _extractor.Extract(document!, _options.MaxUnits, cancellationToken);
        return extraction.Errors.Count > 0
            ? new([], extraction.Errors)
            : new(extraction.Units.Select(x => x.Text).ToArray(), []);
    }

    public async Task<ExportResult> ExportAsync(Stream source, IReadOnlyList<string> translatedTexts, CancellationToken cancellationToken = default)
    {
        var (document, error) = await MarkdownSourceReader.ReadAsync(source, _options.MaxFileBytes, cancellationToken);
        if (error is not null) return new(null, ContentType, [error]);
        var extraction = _extractor.Extract(document!, _options.MaxUnits, cancellationToken);
        var (text, errors) = MarkdownTranslationApplier.Apply(extraction, translatedTexts, _options, cancellationToken);
        if (errors.Count > 0) return new(null, ContentType, errors);
        var structureErrors = _extractor.ValidateStructure(document!.Text, text!);
        if (structureErrors.Count > 0) return new(null, ContentType, structureErrors);
        var bytes = MarkdownSourceReader.Encode(text!, document!.HasBom);
        if (bytes.LongLength > _options.MaxOutputBytes)
            return new(null, ContentType, [new("output_too_large", $"Kết quả vượt giới hạn {_options.MaxOutputBytes} byte.")]);
        return new(bytes, ContentType, []);
    }
}
