namespace FileHandler.Api.Common;

/// <summary>
/// Extracted texts and validation errors.
/// </summary>
/// <param name="Texts">Extracted translation units in source order.</param>
/// <param name="Errors">Errors encountered during import.</param>
public sealed record ImportResult(IReadOnlyList<string> Texts, IReadOnlyList<FileError> Errors)
{

    /// <summary>
    /// Source mapping and processing facts collected during import.
    /// </summary>
    public FileMetadata Metadata { get; init; } = FileMetadata.Create("unknown");
}
