namespace FileHandler.Api.Common;

public sealed record ImportResult(IReadOnlyList<string> Texts, IReadOnlyList<FileError> Errors);
