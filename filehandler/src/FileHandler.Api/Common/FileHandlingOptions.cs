namespace FileHandler.Api.Common;

public sealed class FileHandlingOptions
{
    public const string SectionName = "FileHandling";
    public long MaxFileBytes { get; set; } = 5 * 1024 * 1024;
    public long MaxMultipartBytes { get; set; } = 25 * 1024 * 1024;
    public int MaxUnits { get; set; } = 10_000;
    public int MaxTranslationChars { get; set; } = 100_000;
    public long MaxOutputBytes { get; set; } = 20 * 1024 * 1024;
}
