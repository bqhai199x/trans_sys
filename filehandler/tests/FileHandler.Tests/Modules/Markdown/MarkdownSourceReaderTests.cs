using System.Text;
using FileHandler.Api.Modules.Markdown;

namespace FileHandler.Tests.Modules.Markdown;

public sealed class MarkdownSourceReaderTests
{
    [Fact]
    public void PreservesBomAndMixedLines()
    {
        var bytes = Encoding.UTF8.Preamble.ToArray().Concat(Encoding.UTF8.GetBytes("a\r\nb\nc\rd")).ToArray();
        var source = MarkdownSourceReader.DecodeUtf8(bytes);
        Assert.True(source.HasBom);
        Assert.Equal(4, source.Lines.Starts.Length);
        Assert.Equal(bytes, MarkdownSourceReader.Encode(source.Text, source.HasBom));
    }

    [Fact]
    public async Task RejectsInvalidUtf8()
    {
        var result = await MarkdownSourceReader.ReadAsync(new MemoryStream([0xff]), 10, default);
        Assert.Equal("invalid_encoding", result.Error?.Code);
    }

    [Fact]
    public async Task EnforcesActualStreamLimit()
    {
        var result = await MarkdownSourceReader.ReadAsync(new MemoryStream(new byte[11]), 10, default);
        Assert.Equal("file_too_large", result.Error?.Code);
    }
}
