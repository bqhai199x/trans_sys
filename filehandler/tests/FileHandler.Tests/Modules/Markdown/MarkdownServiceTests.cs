using System.Text;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Markdown;
using Microsoft.Extensions.Options;

namespace FileHandler.Tests.Modules.Markdown;

/// <summary>
/// Functional and roundtrip tests for Markdown import and export service.
/// </summary>
public sealed class MarkdownServiceTests
{

    /// <summary>
    /// Creates Markdown service for test.
    /// </summary>
    /// <returns>Configured service for testing.</returns>
    private static MarkdownService Create() => MarkdownService.Create(Options.Create(new FileHandlingOptions()));

    /// <summary>
    /// Verifies extraction and translated export of acceptance example.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
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

    /// <summary>
    /// Verifies byte-perfect identity export with BOM and CRLF endings.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
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

    /// <summary>
    /// Verifies that missing markers prevent exported content.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task MissingMarkerReturnsNoContent()
    {
        var source = Encoding.UTF8.GetBytes("Hello **world**.");
        var service = Create();
        var result = await service.ExportAsync(new MemoryStream(source), ["Xin chào <keepme1>thế giới."]);
        Assert.Null(result.Content);
        Assert.Contains(result.Errors, e => e.Code == "missing_marker");
    }

    /// <summary>
    /// Verifies that empty file imports and exports successfully.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task EmptyFileRoundTrips()
    {
        var service = Create();
        var imported = await service.ImportAsync(new MemoryStream());
        var exported = await service.ExportAsync(new MemoryStream(), imported.Texts);
        Assert.Empty(imported.Texts);
        Assert.Empty(exported.Content!);
    }

    /// <summary>
    /// Verifies deterministic extraction across concurrent calls with synchronized overlap.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task CallsAreDeterministicAndConcurrent()
    {
        var service = Create();
        var bytes = Encoding.UTF8.GetBytes("One **two** and `three`.");
        var startTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;
        var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            if (Interlocked.Increment(ref readyCount) == 20)
                startTcs.SetResult();
            await startTcs.Task;
            return await service.ImportAsync(new MemoryStream(bytes));
        })).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.All(results, x => Assert.Equal(results[0].Texts, x.Texts));
    }

    /// <summary>
    /// Verifies that hard line breaks with spaces or backslash and CRLF or LF are preserved during export.
    /// </summary>
    /// <param name="source">Original source with hard line break.</param>
    /// <param name="expected">Expected translated output with hard line break preserved.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData("One  \r\nTwo", "Mot  \r\nHai")]
    [InlineData("One  \nTwo", "Mot  \nHai")]
    [InlineData("One\\\r\nTwo", "Mot\\\r\nHai")]
    public async Task Export_PreservesHardBreak_SpacesAndBackslash_WithCrlfAndLf(string source, string expected)
    {
        var service = Create();
        var imported = await service.ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(source)));
        var translations = imported.Texts.Select(t => t.Replace("One", "Mot").Replace("Two", "Hai")).ToArray();
        var exported = await service.ExportAsync(new MemoryStream(Encoding.UTF8.GetBytes(source)), translations);
        Assert.Empty(exported.Errors);
        var output = Encoding.UTF8.GetString(exported.Content!);
        Assert.Equal(expected, output);
        var html = Markdig.Markdown.ToHtml(output, MarkdownProfile.CreatePipeline());
        Assert.Contains("<br />", html);
    }

    /// <summary>
    /// Verifies that emphasis and strong formatting are preserved when translations contain whitespace padding.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Export_PreservesEmphasis_WhenTranslationHasPaddingSpaces()
    {
        const string source = "Before **hello** after";
        var service = Create();
        var imported = await service.ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(source)));
        var translations = new[] { "Before <keepme1> bonjour <keepme1/> after" };
        var exported = await service.ExportAsync(new MemoryStream(Encoding.UTF8.GetBytes(source)), translations);
        Assert.Empty(exported.Errors);
        var html = Markdig.Markdown.ToHtml(Encoding.UTF8.GetString(exported.Content!), MarkdownProfile.CreatePipeline());
        Assert.Contains("<strong>bonjour</strong>", html);
    }

    /// <summary>
    /// Verifies that literal HTML entities and numbered list prefixes in translations are preserved as literal text.
    /// </summary>
    /// <param name="translation">Translation containing special character sequences.</param>
    /// <param name="expectedHtmlSubstring">Substring expected in rendered HTML.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData("A &copy; B &#65;", "A &amp;copy; B &amp;#65;")]
    [InlineData("1. Bonjour", "<p>1. Bonjour</p>")]
    public async Task Export_PreservesLiteralHtmlEntitiesAndNumberedLists(string translation, string expectedHtmlSubstring)
    {
        const string source = "Hello";
        var service = Create();
        var exported = await service.ExportAsync(new MemoryStream(Encoding.UTF8.GetBytes(source)), [translation]);
        Assert.Empty(exported.Errors);
        var html = Markdig.Markdown.ToHtml(Encoding.UTF8.GetString(exported.Content!), MarkdownProfile.CreatePipeline());
        Assert.Contains(expectedHtmlSubstring, html);
    }

    /// <summary>
    /// Verifies that source text containing literal marker prefix does not trigger syntax validation errors.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Export_AllowsLiteralMarkerPrefixInSource()
    {
        const string source = "Read <keepme literally";
        var service = Create();
        var imported = await service.ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(source)));
        var translations = imported.Texts.Select(t => t.Replace("Read", "Lire")).ToArray();
        var exported = await service.ExportAsync(new MemoryStream(Encoding.UTF8.GetBytes(source)), translations);
        Assert.Empty(exported.Errors);
        Assert.Equal("Lire <keepme literally", Encoding.UTF8.GetString(exported.Content!));
    }

    /// <summary>
    /// Verifies that blockquote prefix is preserved for multiline inline containers.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task Export_PreservesQuotePrefix_InNestedEmphasisMultiline()
    {
        const string source = "> **One\r\n> Two**\r\n";
        var service = Create();
        var imported = await service.ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(source)));
        var translations = imported.Texts.Select(t => t.Replace("One", "Mot").Replace("Two", "Hai")).ToArray();
        var exported = await service.ExportAsync(new MemoryStream(Encoding.UTF8.GetBytes(source)), translations);
        Assert.Empty(exported.Errors);
        Assert.Equal("> **Mot\r\n> Hai**\r\n", Encoding.UTF8.GetString(exported.Content!));
    }
}
