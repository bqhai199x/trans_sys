namespace FileHandler.Api.Common;

/// <summary>
/// Extends default file operations with native object selection.
/// </summary>
/// <typeparam name="TSelection">Format-specific sheet or slide selection.</typeparam>
public interface ISelectableFileHandler<in TSelection> : IFileHandler
{

    /// <summary>
    /// Imports selected native objects in source order.
    /// </summary>
    /// <param name="stream">Caller-owned readable source stream.</param>
    /// <param name="selection">Native object selection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Selected texts and metadata without public units, or fatal errors.</returns>
    Task<ImportResult> ImportAsync(Stream stream, TSelection selection, CancellationToken cancellationToken);

    /// <summary>
    /// Imports selected objects with optional diagnostic mapping and informational skips.
    /// </summary>
    /// <param name="stream">Caller-owned readable source stream.</param>
    /// <param name="selection">Native object selection.</param>
    /// <param name="debug">Whether to return informational skips and build diagnostic unit mapping.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Selected texts and metadata; public units and informational skips are absent when debug is false.</returns>
    Task<ImportResult> ImportAsync(Stream stream, TSelection selection, bool debug, CancellationToken cancellationToken);

    /// <summary>
    /// Exports selected translations while preserving all other source objects.
    /// </summary>
    /// <param name="stream">Caller-owned readable source stream.</param>
    /// <param name="translations">Translations in selected source order.</param>
    /// <param name="selection">Native object selection matching import.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Output bytes and metadata, or fatal errors without file content.</returns>
    Task<ExportResult> ExportAsync(Stream stream, IReadOnlyList<string> translations, TSelection selection, CancellationToken cancellationToken);
}
