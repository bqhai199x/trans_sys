namespace FileHandler.Api.Common;

public static class FileTypeDetector
{
    public static bool TryDetect(string? clientFileName, out FileType fileType)
    {
        fileType = default;
        if (string.IsNullOrWhiteSpace(clientFileName)) return false;
        var safeName = clientFileName.Replace('\\', '/').Split('/').Last();
        if (!string.Equals(Path.GetExtension(safeName), ".md", StringComparison.OrdinalIgnoreCase)) return false;
        fileType = FileType.Markdown;
        return true;
    }

    public static string GetTranslatedFileName(string clientFileName)
    {
        var safeName = clientFileName.Replace('\\', '/').Split('/').Last();
        var stem = Path.GetFileNameWithoutExtension(safeName);
        return $"{(string.IsNullOrWhiteSpace(stem) ? "document" : stem)}.translated.md";
    }
}
