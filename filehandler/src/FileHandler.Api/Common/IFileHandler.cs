namespace FileHandler.Api.Common;

public interface IFileHandler
{
    Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default);
    Task<ExportResult> ExportAsync(Stream source, IReadOnlyList<string> translatedTexts, CancellationToken cancellationToken = default);
}
