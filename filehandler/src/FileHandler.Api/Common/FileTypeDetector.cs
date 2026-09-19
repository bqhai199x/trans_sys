namespace FileHandler.Api.Common;

/// <summary>
/// Detects supported file types and preserves safe source attachment names.
/// </summary>
public static class FileTypeDetector
{

    /// <summary>
    /// Detects supported text and Office formats from client file extension.
    /// </summary>
    /// <param name="clientFileName">Client-supplied file name.</param>
    /// <param name="fileType">Detected file type when detection succeeds.</param>
    /// <returns>True for supported source extension; otherwise false.</returns>
    public static bool TryDetect(string? clientFileName, out FileType fileType)
    {
        fileType = default;
        if (string.IsNullOrWhiteSpace(clientFileName))
            return false;
        var safeName = GetFileName(clientFileName);
        var extension = Path.GetExtension(safeName);
        if (string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase))
            fileType = FileType.Markdown;
        else if (string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase))
            fileType = FileType.PlainText;
        else if (string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase))
            fileType = FileType.Word;
        else if (string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
            fileType = FileType.Excel;
        else if (string.Equals(extension, ".pptx", StringComparison.OrdinalIgnoreCase))
            fileType = FileType.PowerPoint;
        else
            return false;
        return true;
    }

    /// <summary>
    /// Preserves source file name, defaulting missing names to Markdown format.
    /// </summary>
    /// <param name="clientFileName">Client-supplied file name.</param>
    /// <returns>Source basename, or format-specific fallback for missing name.</returns>
    public static string GetTranslatedFileName(string? clientFileName) =>
        GetTranslatedFileName(clientFileName, TryDetect(clientFileName, out var fileType) ? fileType : FileType.Markdown);

    /// <summary>
    /// Builds safe attachment name preserving source spelling and extension.
    /// </summary>
    /// <param name="clientFileName">Client-supplied file name.</param>
    /// <param name="fileType">Previously detected supported source format.</param>
    /// <returns>Unchanged source basename, or format-specific fallback for missing name.</returns>
    /// <exception cref="ArgumentOutOfRangeException">File type is unsupported.</exception>
    public static string GetTranslatedFileName(string? clientFileName, FileType fileType)
    {
        var extension = fileType switch
        {
            FileType.Markdown => ".md",
            FileType.PlainText => ".txt",
            FileType.Word => ".docx",
            FileType.Excel => ".xlsx",
            FileType.PowerPoint => ".pptx",
            _ => throw new ArgumentOutOfRangeException(nameof(fileType))
        };
        if (string.IsNullOrWhiteSpace(clientFileName))
            return $"document{extension}";
        var safeName = GetFileName(clientFileName);
        return string.IsNullOrWhiteSpace(safeName) ? $"document{extension}" : safeName;
    }

    /// <summary>
    /// Extracts trailing file name component from relative or absolute client path without string splitting.
    /// </summary>
    /// <param name="path">Client-supplied file path.</param>
    /// <returns>Trailing file name segment.</returns>
    private static string GetFileName(string path)
    {
        var offset = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\')) + 1;
        return path[offset..];
    }
}
