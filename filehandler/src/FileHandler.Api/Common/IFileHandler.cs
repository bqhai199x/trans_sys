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
    /// <returns>Extracted texts, metadata without public units, and validation errors.</returns>
    Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports source with optional diagnostic unit mapping and informational skips.
    /// </summary>
    /// <param name="source">Caller-owned readable source stream.</param>
    /// <param name="debug">Whether to return informational skips and build diagnostic unit mapping.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Texts and metadata; public units and informational skips are absent when debug is false.</returns>
    Task<ImportResult> ImportAsync(Stream source, bool debug, CancellationToken cancellationToken);

    /// <summary>
    /// Applies translations to source stream and returns exported file.
    /// </summary>
    /// <param name="source">Readable source stream.</param>
    /// <param name="translations">Translated units in source order.</param>
    /// <param name="cancellationToken">Token for cancelling this operation.</param>
    /// <returns>Task containing exported bytes, media type, and validation errors.</returns>
    Task<ExportResult> ExportAsync(Stream source, IReadOnlyList<string> translations, CancellationToken cancellationToken = default);
}
