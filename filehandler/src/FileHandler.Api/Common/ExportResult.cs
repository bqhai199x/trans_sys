namespace FileHandler.Api.Common;

public sealed record ExportResult(byte[]? Content, string ContentType, IReadOnlyList<FileError> Errors);
