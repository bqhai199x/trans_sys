using FileHandler.Api.Common;

namespace FileHandler.Tests.Common;

public sealed class FileTypeDetectorTests
{
    [Theory]
    [InlineData("a.md")]
    [InlineData("A.MD")]
    [InlineData("C:\\fake\\guide.md")]
    [InlineData("../../guide.md")]
    public void DetectsMarkdown(string name) => Assert.True(FileTypeDetector.TryDetect(name, out var type) && type == FileType.Markdown);

    [Theory]
    [InlineData("a.md.exe")]
    [InlineData("a")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsOtherNames(string? name) => Assert.False(FileTypeDetector.TryDetect(name, out _));

    [Fact]
    public void SanitizesOutputName() => Assert.Equal("guide.translated.md", FileTypeDetector.GetTranslatedFileName("C:\\secret\\guide.md"));
}
