using System.Text;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Markdown;
using Microsoft.Extensions.Options;

namespace FileHandler.Tests.Modules.Markdown;

/// <summary>
/// Acceptance and syntax parity tests for configured Markdown profile.
/// </summary>
public sealed class MarkdownProfileTests
{

    /// <summary>
    /// Creates Markdown service for test.
    /// </summary>
    /// <param name="options">File processing limits.</param>
    /// <returns>Configured service for testing.</returns>
    private static MarkdownService Create(FileHandlingOptions? options = null) => MarkdownService.Create(Options.Create(options ?? new()));

    /// <summary>
    /// Verifies deterministic extraction and byte-perfect export of profile fixture.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task FullProfileFixtureHasDeterministicIdentityRoundTrip()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Markdown", "profile.md");
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        var service = Create();
        var first = await service.ImportAsync(new MemoryStream(bytes), TestContext.Current.CancellationToken);
        var second = await service.ImportAsync(new MemoryStream(bytes), TestContext.Current.CancellationToken);
        Assert.Empty(first.Errors);
        Assert.Equal(first.Texts, second.Texts);
        Assert.DoesNotContain(first.Texts, x => x.Contains("graph TD", StringComparison.Ordinal));
        Assert.DoesNotContain(first.Texts, x => x.Contains("Keep metadata", StringComparison.Ordinal));
        var exported = await service.ExportAsync(new MemoryStream(bytes), first.Texts, TestContext.Current.CancellationToken);
        Assert.Empty(exported.Errors);
        Assert.Equal(bytes, exported.Content);
    }

    /// <summary>
    /// Verifies that literal markers survive translation without ID collisions.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task LiteralProtocolMarkerIsCollisionSafe()
    {
        var service = Create();
        var bytes = Encoding.UTF8.GetBytes("Read <keepme1> literally and **translate me**.");
        var imported = await service.ImportAsync(new MemoryStream(bytes), TestContext.Current.CancellationToken);
        Assert.Contains("<ox:k0/>", imported.Texts.Single(), StringComparison.Ordinal);
        var changed = imported.Texts.Single().Replace("translate me", "dịch tôi", StringComparison.Ordinal);
        var exported = await service.ExportAsync(new MemoryStream(bytes), [changed], TestContext.Current.CancellationToken);
        Assert.Empty(exported.Errors);
        Assert.Equal("Read <keepme1> literally and **dịch tôi**.", Encoding.UTF8.GetString(exported.Content!));
    }

    /// <summary>
    /// Verifies rejection of invalid translation batches and marker content.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task RejectsCountEmptyDuplicateUnexpectedAndProtectedContent()
    {
        var service = Create();
        var source = Encoding.UTF8.GetBytes("Hello **world** and `code`.");
        var count = await service.ExportAsync(new MemoryStream(source), [], TestContext.Current.CancellationToken);
        Assert.Contains(count.Errors, x => x.Code == "translation_count_mismatch");

        var empty = await service.ExportAsync(new MemoryStream(source), [" "], TestContext.Current.CancellationToken);
        Assert.Contains(empty.Errors, x => x.Code == "empty_translation");

        var duplicate = await service.ExportAsync(new MemoryStream(source), ["<ox:r0>x</ox:r0><ox:r0>x</ox:r0>"], TestContext.Current.CancellationToken);
        Assert.Contains(duplicate.Errors, x => x.Code == "invalid_marker_syntax");

        var unexpected = await service.ExportAsync(new MemoryStream(source), ["<ox:r99>x</ox:r99>"], TestContext.Current.CancellationToken);
        Assert.Contains(unexpected.Errors, x => x.Code == "invalid_marker_syntax");

        var protectedContent = await service.ExportAsync(new MemoryStream(source), ["<ox:r0>x</ox:r0><ox:r1>y</ox:r1><ox:r2> and </ox:r2><ox:k0>bad</ox:k0><ox:r3>.</ox:r3>"], TestContext.Current.CancellationToken);
        Assert.Contains(protectedContent.Errors, x => x.Code == "invalid_marker_syntax");
        Assert.Null(protectedContent.Content);

        var nonCanonical = await service.ExportAsync(new MemoryStream(source), ["<ox:r00>y</ox:r00>"], TestContext.Current.CancellationToken);
        Assert.Contains(nonCanonical.Errors, x => x.Code == "invalid_marker_syntax");

        var overflow = await service.ExportAsync(new MemoryStream(source), ["<ox:r999999999999999999>x</ox:r999999999999999999>"], TestContext.Current.CancellationToken);
        Assert.Contains(overflow.Errors, x => x.Code == "invalid_marker_syntax");
    }

    /// <summary>
    /// Verifies rejection of heading changes when internal anchors exist.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task RejectsHeadingChangeWhenInternalAnchorExists()
    {
        var service = Create();
        var source = Encoding.UTF8.GetBytes("# Hello\n\n[Jump](#hello)\n");
        var imported = await service.ImportAsync(new MemoryStream(source), TestContext.Current.CancellationToken);
        var translations = imported.Texts.ToArray();
        translations[0] = "Xin chào";
        var exported = await service.ExportAsync(new MemoryStream(source), translations, TestContext.Current.CancellationToken);
        Assert.Contains(exported.Errors, x => x.Code == "internal_anchor_change_unsupported");
        Assert.Null(exported.Content);
    }

    /// <summary>
    /// Verifies configured unit, translation, and output limits.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task EnforcesUnitTranslationAndOutputLimits()
    {
        var unitLimited = Create(new() { MaxUnits = 1 });
        var tooMany = await unitLimited.ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes("One\n\nTwo")), TestContext.Current.CancellationToken);
        Assert.Equal("too_many_units", tooMany.Errors.Single().Code);

        var translationLimited = Create(new() { MaxTranslationChars = 2 });
        var tooLong = await translationLimited.ExportAsync(new MemoryStream(Encoding.UTF8.GetBytes("One")), ["Long"], TestContext.Current.CancellationToken);
        Assert.Equal("translation_too_long", tooLong.Errors.Single().Code);

        var outputLimited = Create(new() { MaxOutputBytes = 2 });
        var output = await outputLimited.ExportAsync(new MemoryStream(Encoding.UTF8.GetBytes("One")), ["Hai"], TestContext.Current.CancellationToken);
        Assert.Equal("output_too_large", output.Errors.Single().Code);
    }

    /// <summary>
    /// Verifies that translated soft breaks retain blockquote prefixes.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task PreservesQuotePrefixAcrossTranslatedSoftBreak()
    {
        var service = Create();
        var source = Encoding.UTF8.GetBytes("> First line\r\n> second line\r\n");
        var imported = await service.ImportAsync(new MemoryStream(source), TestContext.Current.CancellationToken);
        Assert.Equal(["<ox:r0>First line</ox:r0><ox:k0/><ox:r1>second line</ox:r1>"], imported.Texts);
        var exported = await service.ExportAsync(new MemoryStream(source), ["<ox:r0>Dòng một</ox:r0><ox:k0/><ox:r1>Dòng hai</ox:r1>"], TestContext.Current.CancellationToken);
        Assert.Empty(exported.Errors);
        Assert.Equal("> Dòng một\r\n> Dòng hai\r\n", Encoding.UTF8.GetString(exported.Content!));
    }

    /// <summary>
    /// Verifies rejection of translations that add Markdown blocks.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task RejectsTranslationThatCreatesAnotherBlock()
    {
        var service = Create();
        var source = Encoding.UTF8.GetBytes("A paragraph.\n");
        var exported = await service.ExportAsync(new MemoryStream(source), ["First\n\nSecond"], TestContext.Current.CancellationToken);
        Assert.Null(exported.Content);
        Assert.Contains(exported.Errors, x => x.Code == "invalid_structure");
    }
}
