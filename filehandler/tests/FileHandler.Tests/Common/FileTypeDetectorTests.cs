using FileHandler.Api.Common;

namespace FileHandler.Tests.Common;

/// <summary>
/// Unit tests for file type detection and translated file name generation.
/// </summary>
public sealed class FileTypeDetectorTests
{

    /// <summary>
    /// Verifies that supported Markdown file names are detected.
    /// </summary>
    /// <param name="name">Client-supplied file name.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData("a.md")]
    [InlineData("A.MD")]
    [InlineData("C:\\fake\\guide.md")]
    [InlineData("../../guide.md")]
    public void DetectsMarkdown(string name) => Assert.True(FileTypeDetector.TryDetect(name, out var type) && type == FileType.Markdown);

    /// <summary>
    /// Verifies that unsupported or missing file names are rejected.
    /// </summary>
    /// <param name="name">Client-supplied file name.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData("a.md.exe")]
    [InlineData("a.txt.exe")]
    [InlineData("a.pdf")]
    [InlineData("a")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsOtherNames(string? name) => Assert.False(FileTypeDetector.TryDetect(name, out _));

    /// <summary>
    /// Verifies that output names omit client directory paths.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void SanitizesOutputName() => Assert.Equal("guide.translated.md", FileTypeDetector.GetTranslatedFileName("C:\\secret\\guide.md"));

    /// <summary>
    /// Verifies plain text detection ignores extension case and client directory paths.
    /// </summary>
    /// <param name="name">Client-supplied file name.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData("guide.txt")]
    [InlineData("guide.TXT")]
    [InlineData("C:\\fake\\guide.txt")]
    [InlineData("../../guide.txt")]
    public void DetectsPlainText(string name)
    {
        Assert.True(FileTypeDetector.TryDetect(name, out var type));
        Assert.Equal(FileType.PlainText, type);
        Assert.Equal("guide.translated.txt", FileTypeDetector.GetTranslatedFileName(name, type));
        Assert.Equal("guide.translated.txt", FileTypeDetector.GetTranslatedFileName(name));
    }

    /// <summary>
    /// Verifies missing or empty stems use format-specific fallback names.
    /// </summary>
    /// <param name="name">Missing name or empty stem.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".txt")]
    public void UsesPlainTextFallbackName(string? name) =>
        Assert.Equal("document.translated.txt", FileTypeDetector.GetTranslatedFileName(name, FileType.PlainText));
}
