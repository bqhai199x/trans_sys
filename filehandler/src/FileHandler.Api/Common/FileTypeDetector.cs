using FileHandler.Api.Diagnostics;

namespace FileHandler.Api.Common;

/// <summary>
/// Detects supported file types and builds translated output file names.
/// </summary>
public static class FileTypeDetector
{

    /// <summary>
    /// Detects Markdown or plain text from client file extension.
    /// </summary>
    /// <param name="clientFileName">Client-supplied file name.</param>
    /// <param name="fileType">Detected file type when detection succeeds.</param>
    /// <returns>True for supported .md or .txt extension; otherwise false.</returns>
    public static bool TryDetect(string? clientFileName, out FileType fileType)
    {
        using var trace = DebugTrace.Enter("FileTypeDetector", "TryDetect", () => new { clientFileName });
        try
        {
            fileType = default;
            if (string.IsNullOrWhiteSpace(clientFileName))
                return trace.Return<bool>(false);
            var safeName = GetFileName(clientFileName);
            var extension = Path.GetExtension(safeName);
            trace.State("extension", () => extension);
            if (string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase))
                fileType = FileType.Markdown;
            else if (string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase))
                fileType = FileType.PlainText;
            else
                return trace.Return<bool>(false);
            var detectedType = fileType;
            trace.State("fileType", () => detectedType);
            return trace.Return<bool>(true);
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
    }

    /// <summary>
    /// Builds translated file name, defaulting unsupported or missing names to Markdown format.
    /// </summary>
    /// <param name="clientFileName">Client-supplied file name.</param>
    /// <returns>Safe file name ending in .translated.md or .translated.txt.</returns>
    public static string GetTranslatedFileName(string? clientFileName) =>
        GetTranslatedFileName(clientFileName, TryDetect(clientFileName, out var fileType) ? fileType : FileType.Markdown);

    /// <summary>
    /// Builds safe translated file name using detected source format.
    /// </summary>
    /// <param name="clientFileName">Client-supplied file name.</param>
    /// <param name="fileType">Previously detected supported source format.</param>
    /// <returns>Safe attachment name with format-specific translated extension.</returns>
    /// <exception cref="ArgumentOutOfRangeException">File type is unsupported.</exception>
    public static string GetTranslatedFileName(string? clientFileName, FileType fileType)
    {
        using var trace = DebugTrace.Enter("FileTypeDetector", "GetTranslatedFileName", () => new { clientFileName, fileType });
        try
        {
            var extension = fileType switch
            {
                FileType.Markdown => ".md",
                FileType.PlainText => ".txt",
                _ => throw new ArgumentOutOfRangeException(nameof(fileType))
            };
            if (string.IsNullOrWhiteSpace(clientFileName))
                return trace.Return<string>($"document.translated{extension}");
            var safeName = GetFileName(clientFileName);
            var stem = Path.GetFileNameWithoutExtension(safeName);
            return trace.Return<string>($"{(string.IsNullOrWhiteSpace(stem) ? "document" : stem)}.translated{extension}");
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
    }

    /// <summary>
    /// Extracts trailing file name component from relative or absolute client path without string splitting.
    /// </summary>
    /// <param name="path">Client-supplied file path.</param>
    /// <returns>Trailing file name segment.</returns>
    private static string GetFileName(string path) => Path.GetFileName(path);
}
