using FileHandler.Api.Common;

namespace FileHandler.Api.Modules.Markdown;

/// <summary>
/// Extracts translation units and validates Markdown document structure.
/// </summary>
internal interface IMarkdownExtractor
{

    /// <summary>
    /// Extracts translation units while preserving Markdown syntax with markers.
    /// </summary>
    /// <param name="source">Original Markdown source.</param>
    /// <param name="maxUnits">Maximum translation unit count.</param>
    /// <param name="cancellationToken">Token for cancelling this operation.</param>
    /// <returns>Translation units, source metadata, and extraction errors.</returns>
    MarkdownExtraction Extract(MarkdownSource source, int maxUnits, CancellationToken cancellationToken);

    /// <summary>
    /// Validates translated Markdown structural parity against source.
    /// </summary>
    /// <param name="original">Original Markdown source.</param>
    /// <param name="candidate">Translated Markdown text.</param>
    /// <returns>Structural validation errors, if any.</returns>
    IReadOnlyList<FileError> ValidateStructure(string original, string candidate);
}
