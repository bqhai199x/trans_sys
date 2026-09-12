namespace FileHandler.Api.Common;

/// <summary>
/// Exported file content and validation errors.
/// </summary>
/// <param name="Content">Exported bytes, or null when export fails.</param>
/// <param name="ContentType">Response media type.</param>
/// <param name="Errors">Errors encountered during export.</param>
public sealed record ExportResult(byte[]? Content, string ContentType, IReadOnlyList<FileError> Errors);
