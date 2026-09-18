using System.Text;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Markdown;
using Microsoft.Extensions.Options;

namespace FileHandler.Tests.Modules.Markdown;

/// <summary>
/// Regression coverage for shared Office-style Markdown wire tokens.
/// </summary>
public sealed class MarkdownUnifiedTokenTests
{

    /// <summary>
    /// Verifies nested formatting uses flat scoped runs and preserves Markdown syntax.
    /// </summary>
    /// <returns>Task representing completed assertions.</returns>
    [Fact]
    public async Task NestedFormattingUsesFlatRuns()
    {
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var bytes = Encoding.UTF8.GetBytes("Read **bold *inner*** now.");
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Empty(imported.Errors);
        Assert.Equal("<ox:r0>Read </ox:r0><ox:r1>bold </ox:r1><ox:r2>inner</ox:r2><ox:r3> now.</ox:r3>", Assert.Single(imported.Texts));
        var output = await service.ExportAsync(new MemoryStream(bytes),
            ["<ox:r0>Lire </ox:r0><ox:r1>gras </ox:r1><ox:r2>dedans</ox:r2><ox:r3> maintenant.</ox:r3>"]);
        Assert.Empty(output.Errors);
        Assert.Equal("Read **bold *inner*** now.", Encoding.UTF8.GetString(bytes));
        Assert.Equal("Lire **gras *dedans*** maintenant.", Encoding.UTF8.GetString(output.Content!));
    }

    /// <summary>
    /// Verifies literal protocol syntax and backslashes round-trip through escaping.
    /// </summary>
    /// <returns>Task representing completed assertions.</returns>
    [Fact]
    public async Task StructuredSlotsAcceptEscapedTokenLiterals()
    {
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var bytes = Encoding.UTF8.GetBytes("Before **red** after");
        var output = await service.ExportAsync(new MemoryStream(bytes),
            ["<ox:r0>Literal \\<ox:k0/> and \\\\ </ox:r0><ox:r1>rouge</ox:r1><ox:r2> after</ox:r2>"]);
        Assert.Empty(output.Errors);
        var html = Markdig.Markdown.ToHtml(Encoding.UTF8.GetString(output.Content!), MarkdownProfile.CreatePipeline());
        Assert.Contains("&lt;ox:k0/&gt;", html);
        Assert.Contains("<strong>rouge</strong>", html);
    }

    /// <summary>
    /// Verifies source HTML color tags remain protected around translated text.
    /// </summary>
    /// <returns>Task representing completed assertions.</returns>
    [Fact]
    public async Task RedInlineHtmlRemainsAroundTranslatedText()
    {
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var bytes = Encoding.UTF8.GetBytes("Before <span style=\"color:red\">red</span> after");
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Equal("<ox:r0>Before </ox:r0><ox:k0/><ox:r1>red</ox:r1><ox:k1/><ox:r2> after</ox:r2>", Assert.Single(imported.Texts));
        var output = await service.ExportAsync(new MemoryStream(bytes), [imported.Texts[0].Replace(">red<", ">đỏ<", StringComparison.Ordinal)]);
        Assert.Empty(output.Errors);
        Assert.Equal("Before <span style=\"color:red\">đỏ</span> after", Encoding.UTF8.GetString(output.Content!));
    }

    /// <summary>
    /// Verifies tokens reset per unit and preserve adjacent protected anchors.
    /// </summary>
    /// <returns>Task representing completed assertions.</returns>
    [Fact]
    public async Task AnchorsAndRunIdsAreScopedToEachUnit()
    {
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var bytes = Encoding.UTF8.GetBytes("`a``b` **One**\n\nBefore **Two** after");
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Empty(imported.Errors);
        Assert.All(imported.Texts, text => Assert.Contains("<ox:r0>", text));
        Assert.Contains("<ox:k0/>", imported.Texts[0]);
        var output = await service.ExportAsync(new MemoryStream(bytes), imported.Texts.Select(x => x.Replace("One", "Un").Replace("Two", "Deux")).ToArray());
        Assert.Empty(output.Errors);
        Assert.Equal("`a``b` **Un**\n\nBefore **Deux** after", Encoding.UTF8.GetString(output.Content!));
    }

    /// <summary>
    /// Verifies malformed, reordered, nested or empty run tokens fail atomically.
    /// </summary>
    /// <param name="translation">Invalid structured translation.</param>
    /// <param name="code">Expected validation code.</param>
    /// <returns>Task representing completed assertions.</returns>
    [Theory]
    [InlineData("<ox:r1>B</ox:r1><ox:r0>A</ox:r0>", "invalid_marker_syntax")]
    [InlineData("<ox:r0><ox:r1>A</ox:r1></ox:r0><ox:r1>B</ox:r1>", "invalid_marker_syntax")]
    [InlineData("<ox:r0>A</ox:r0><ox:r1>B</ox:r1>extra", "invalid_marker_syntax")]
    [InlineData("<ox:r0>\\q</ox:r0><ox:r1>B</ox:r1>", "invalid_marker_syntax")]
    [InlineData("<ox:r0> </ox:r0><ox:r1>B</ox:r1>", "empty_translation")]
    [InlineData("<keepme1>A<keepme1/>B", "invalid_marker_syntax")]
    public async Task InvalidWireTokensReturnNoOutput(string translation, string code)
    {
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var bytes = Encoding.UTF8.GetBytes("**One** after");
        var result = await service.ExportAsync(new MemoryStream(bytes), [translation]);
        Assert.Null(result.Content);
        var error = Assert.Single(result.Errors);
        Assert.Equal(code, error.Code);
        Assert.Equal(0, error.Index);
        Assert.Equal(new SourceLineRange(1, 1), error.Line);
        Assert.Equal("**One** after", Encoding.UTF8.GetString(bytes));
    }

    /// <summary>
    /// Verifies single-format units use literal plain strings and validate Unicode.
    /// </summary>
    /// <returns>Task representing completed assertions.</returns>
    [Fact]
    public async Task SingleRunIsPlainAndInvalidUnicodeIsRejected()
    {
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var bytes = Encoding.UTF8.GetBytes("**Hello**");
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Equal("Hello", Assert.Single(imported.Texts));
        var output = await service.ExportAsync(new MemoryStream(bytes), ["Bonjour"]);
        Assert.Empty(output.Errors);
        Assert.Equal("**Bonjour**", Encoding.UTF8.GetString(output.Content!));
        var invalid = await service.ExportAsync(new MemoryStream(bytes), ["\uD800"]);
        Assert.Equal("invalid_translation", Assert.Single(invalid.Errors).Code);
        Assert.Null(invalid.Content);
    }

    /// <summary>
    /// Verifies literal source tokens cannot collide with generated run identifiers.
    /// </summary>
    /// <returns>Task representing completed assertions.</returns>
    [Fact]
    public async Task SourceTokenLiteralsRemainCollisionSafe()
    {
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var bytes = Encoding.UTF8.GetBytes(@"Read \<ox:r0>literal\</ox:r0> and **bold**.");
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Empty(imported.Errors);
        Assert.Contains(@"\<ox:r0>", Assert.Single(imported.Texts));
        var identity = await service.ExportAsync(new MemoryStream(bytes), imported.Texts);
        Assert.Equal(bytes, identity.Content);
        var output = await service.ExportAsync(new MemoryStream(bytes), [imported.Texts[0].Replace("bold", "gras")]);
        Assert.Empty(output.Errors);
        var html = Markdig.Markdown.ToHtml(Encoding.UTF8.GetString(output.Content!), MarkdownProfile.CreatePipeline());
        Assert.Contains("&lt;ox:r0&gt;literal&lt;/ox:r0&gt;", html);
        Assert.Contains("<strong>gras</strong>", html);
    }
}
