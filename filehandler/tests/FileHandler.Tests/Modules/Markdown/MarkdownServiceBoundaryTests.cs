using FileHandler.Api.Common;
using FileHandler.Api.Modules.Markdown;
using Microsoft.Extensions.Options;

namespace FileHandler.Tests.Modules.Markdown;

/// <summary>
/// Unit tests for Markdown service size limit and stream boundary conditions.
/// </summary>
public sealed class MarkdownServiceBoundaryTests
{

    /// <summary>
    /// Verifies read failures prevent partial import and export results.
    /// </summary>
    /// <param name="sizeLimit">Whether to trigger size validation instead of encoding validation.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportAndExport_ReturnReadErrorsWithoutPartialResults(bool sizeLimit)
    {
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions { MaxFileBytes = sizeLimit ? 0 : 10 }));
        using var source = new MemoryStream([0xff]);
        var imported = await service.ImportAsync(source);
        source.Position = 0;
        var exported = await service.ExportAsync(source, []);
        var code = sizeLimit ? "file_too_large" : "invalid_encoding";
        Assert.Empty(imported.Texts);
        Assert.Equal(code, Assert.Single(imported.Errors).Code);
        Assert.Null(exported.Content);
        Assert.Equal(code, Assert.Single(exported.Errors).Code);
        Assert.Equal(MarkdownService.ContentType, exported.ContentType);
    }

    /// <summary>
    /// Verifies output byte limits include BOM bytes.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task ExportAsync_AcceptsExactOutputByteLimitIncludingBom()
    {
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions { MaxOutputBytes = 5 }));
        using var source = new MemoryStream(MarkdownSourceReader.Encode("A", true));
        var result = await service.ExportAsync(source, ["é"]);
        Assert.Empty(result.Errors);
        Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf, 0xc3, 0xa9 }, result.Content);
    }
}
