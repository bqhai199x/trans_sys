namespace FileHandler.Api.Common;

/// <summary>
/// Common contract for format-specific file importers and exporters.
/// </summary>
public interface IFileHandler
{

    /// <summary>
    /// Extracts translatable text from source stream.
    /// </summary>
    /// <param name="source">Readable source stream.</param>
    /// <param name="cancellationToken">Token for cancelling this operation.</param>
    /// <returns>Task containing extracted texts and validation errors.</returns>
    Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies translations to source stream and returns exported file.
    /// </summary>
    /// <param name="source">Readable source stream.</param>
    /// <param name="translatedTexts">Translated units in source order.</param>
    /// <param name="cancellationToken">Token for cancelling this operation.</param>
    /// <returns>Task containing exported bytes, media type, and validation errors.</returns>
    Task<ExportResult> ExportAsync(Stream source, IReadOnlyList<string> translatedTexts, CancellationToken cancellationToken = default);
}
