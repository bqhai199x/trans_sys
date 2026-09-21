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
    /// Checks mixed scripts and equivalent adjacent formatting share one translation slot.
    /// </summary>
    /// <param name="source">Original Markdown source.</param>
    /// <param name="expected">Expected public import text.</param>
    /// <returns>Task completing after import and translated round-trip assertions.</returns>
    [Theory]
    [InlineData("2026年3月31日", "2026年3月31日")]
    [InlineData("**.NET Framework/JAVA換装について**", ".NET Framework/JAVA換装について")]
    [InlineData("Track2：JAVA21/25互換対応", "Track2：JAVA21/25互換対応")]
    [InlineData("**Track**__2：JAVA21/25互換対応__", "Track2：JAVA21/25互換対応")]
    [InlineData("**Track**__2__**対応**", "Track2対応")]
    public async Task SameStyleMixedScripts_MergeAndRoundTrip(string source, string expected)
    {
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var bytes = Encoding.UTF8.GetBytes(source);
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Empty(imported.Errors);
        Assert.Equal(expected, Assert.Single(imported.Texts));
        var identity = await service.ExportAsync(new MemoryStream(bytes), imported.Texts);
        Assert.Empty(identity.Errors);
        Assert.Equal(bytes, identity.Content);
        var exported = await service.ExportAsync(new MemoryStream(bytes), ["Bản dịch"]);
        Assert.Empty(exported.Errors);
        Assert.DoesNotContain("keepme", Encoding.UTF8.GetString(exported.Content!));
        var reimported = await service.ImportAsync(new MemoryStream(exported.Content!));
        Assert.Empty(reimported.Errors);
        Assert.Equal("Bản dịch", Assert.Single(reimported.Texts));
    }

    /// <summary>
    /// Checks empty slots remove original text and omit empty emphasis delimiters.
    /// </summary>
    /// <param name="source">Original Markdown source.</param>
    /// <param name="translation">Public translation retaining every token.</param>
    /// <param name="expected">Expected exported Markdown.</param>
    /// <returns>Task completing after export assertions.</returns>
    [Theory]
    [InlineData("Track**2：JAVA21/25互換**対応", "<ox:r0>Track</ox:r0><ox:r1>2: Hỗ trợ tương thích JAVA21/25</ox:r1><ox:r2></ox:r2>", "Track**2: Hỗ trợ tương thích JAVA21/25**")]
    [InlineData("**One** after", "<ox:r0></ox:r0><ox:r1>Après</ox:r1>", "Après")]
    [InlineData("**One** after", "<ox:r0> </ox:r0><ox:r1>Après</ox:r1>", " Après")]
    [InlineData("**One *inner*** after", "<ox:r0></ox:r0><ox:r1></ox:r1><ox:r2>Après</ox:r2>", "Après")]
    [InlineData("**One `code`** after", "<ox:r0></ox:r0><ox:k0/><ox:r1> après</ox:r1>", "**`code`** après")]
    public async Task EmptySlots_PreserveRemainingContent(string source, string translation, string expected)
    {
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var output = await service.ExportAsync(new MemoryStream(Encoding.UTF8.GetBytes(source)), [translation]);
        Assert.Empty(output.Errors);
        Assert.Equal(expected, Encoding.UTF8.GetString(output.Content!));
    }

    /// <summary>
    /// Ensures empty emphasis does not allow injected blocks or omitted anchors.
    /// </summary>
    /// <param name="source">Original Markdown source.</param>
    /// <param name="translation">Invalid translated content.</param>
    /// <param name="code">Expected error code.</param>
    /// <returns>Task completing after source preservation and skip assertions.</returns>
    [Theory]
    [InlineData("**One** after", "<ox:r0></ox:r0><ox:r1>A\n\nB</ox:r1>", "invalid_structure")]
    [InlineData("**One** after", "<ox:r0></ox:r0>", "invalid_marker_syntax")]
    [InlineData("**One `code`** after", "<ox:r0></ox:r0><ox:r1>after</ox:r1>", "invalid_marker_syntax")]
    public async Task EmptySlots_StillValidateStructure(string source, string translation, string code)
    {
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var output = await service.ExportAsync(new MemoryStream(Encoding.UTF8.GetBytes(source)), [translation]);
        Assert.Equal(Encoding.UTF8.GetBytes(source), output.Content);
        Assert.Empty(output.Errors);
        Assert.Contains(output.Metadata.Skipped, error => error.Code == code);
    }

    /// <summary>
    /// Checks visually similar link labels keep separate ownership and protected targets.
    /// </summary>
    /// <returns>Task completing after import assertions.</returns>
    [Fact]
    public async Task AdjacentLinks_KeepSeparateSlots()
    {
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var imported = await service.ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes("[Track](https://a.test)[2](https://b.test)")));
        Assert.Empty(imported.Errors);
        Assert.Equal("<ox:r0>Track</ox:r0><ox:r1>2</ox:r1>", Assert.Single(imported.Texts));
    }

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
    /// Verifies malformed, reordered, nested or empty run tokens preserve source unit.
    /// </summary>
    /// <param name="translation">Invalid structured translation.</param>
    /// <param name="code">Expected validation code.</param>
    /// <returns>Task representing completed assertions.</returns>
    [Theory]
    [InlineData("<ox:r1>B</ox:r1><ox:r0>A</ox:r0>", "invalid_marker_syntax")]
    [InlineData("<ox:r0><ox:r1>A</ox:r1></ox:r0><ox:r1>B</ox:r1>", "invalid_marker_syntax")]
    [InlineData("<ox:r0>A</ox:r0><ox:r1>B</ox:r1>extra", "invalid_marker_syntax")]
    [InlineData("<ox:r0>\\q</ox:r0><ox:r1>B</ox:r1>", "invalid_marker_syntax")]
    [InlineData("<ox:r0> </ox:r0><ox:r1></ox:r1>", "empty_translation")]
    [InlineData("<keepme1>A<keepme1/>B", "invalid_marker_syntax")]
    public async Task InvalidWireTokensPreserveSource(string translation, string code)
    {
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var bytes = Encoding.UTF8.GetBytes("**One** after");
        var result = await service.ExportAsync(new MemoryStream(bytes), [translation]);
        Assert.Equal(bytes, result.Content);
        Assert.Empty(result.Errors);
        var error = Assert.Single(result.Metadata.Skipped);
        Assert.Equal(code, error.Code);
        Assert.Equal(0, error.UnitIndex);
        Assert.Equal(new SourceLineRange(1, 1), error.Location.Line);
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
        Assert.Equal("invalid_translation", Assert.Single(invalid.Metadata.Skipped).Code);
        Assert.Equal(bytes, invalid.Content);
        Assert.Empty(invalid.Errors);
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
