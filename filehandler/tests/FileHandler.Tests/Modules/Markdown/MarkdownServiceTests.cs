using System.Text;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Markdown;
using Microsoft.Extensions.Options;

namespace FileHandler.Tests.Modules.Markdown;

public sealed class MarkdownServiceTests
{
    private static MarkdownService Create() => new(Options.Create(new FileHandlingOptions()));

    [Fact]
    public async Task AcceptanceExampleImportsAndExports()
    {
        const string markdown = "# Quick start\n\nRead **the guide** at [our website](https://example.com), then run `npm install`.\n";
        var service = Create();
        var imported = await service.ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(markdown)));
        Assert.Empty(imported.Errors);
        Assert.Equal(new[] { "Quick start", "Read <keepme1>the guide<keepme1/> at <keepme2>our website<keepme2/>, then run <keepme3><keepme3/>." }, imported.Texts);

        var translated = new[] { "Bắt đầu nhanh", "Đọc <keepme1>hướng dẫn<keepme1/> tại <keepme2>trang web của chúng tôi<keepme2/>, sau đó chạy <keepme3><keepme3/>." };
        var exported = await service.ExportAsync(new MemoryStream(Encoding.UTF8.GetBytes(markdown)), translated);
        Assert.Empty(exported.Errors);
        Assert.Equal("# Bắt đầu nhanh\n\nĐọc **hướng dẫn** tại [trang web của chúng tôi](https://example.com), sau đó chạy `npm install`.\n", Encoding.UTF8.GetString(exported.Content!));
    }

    [Fact]
    public async Task IdentityIsBytePerfectWithBomAndCrLf()
    {
        var original = Encoding.UTF8.Preamble.ToArray().Concat(Encoding.UTF8.GetBytes("# Héllo\r\n\r\nEscaped \\*star\\* &amp; `x`.\r\n")).ToArray();
        var service = Create();
        var imported = await service.ImportAsync(new MemoryStream(original));
        var exported = await service.ExportAsync(new MemoryStream(original), imported.Texts);
        Assert.Empty(exported.Errors);
        Assert.Equal(original, exported.Content);
    }

    [Fact]
    public async Task MissingMarkerReturnsNoContent()
    {
        var source = Encoding.UTF8.GetBytes("Hello **world**.");
        var service = Create();
        var result = await service.ExportAsync(new MemoryStream(source), ["Xin chào <keepme1>thế giới."]);
        Assert.Null(result.Content);
        Assert.Contains(result.Errors, e => e.Code == "missing_marker");
    }

    [Fact]
    public async Task EmptyFileRoundTrips()
    {
        var service = Create();
        var imported = await service.ImportAsync(new MemoryStream());
        var exported = await service.ExportAsync(new MemoryStream(), imported.Texts);
        Assert.Empty(imported.Texts);
        Assert.Empty(exported.Content!);
    }

    [Fact]
    public async Task CallsAreDeterministicAndConcurrent()
    {
        var service = Create();
        var bytes = Encoding.UTF8.GetBytes("One **two** and `three`.");
        var tasks = Enumerable.Range(0, 20).Select(_ => service.ImportAsync(new MemoryStream(bytes)));
        var results = await Task.WhenAll(tasks);
        Assert.All(results, x => Assert.Equal(results[0].Texts, x.Texts));
    }
}
