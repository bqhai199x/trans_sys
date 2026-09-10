using System.Text;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Markdown;
using Microsoft.Extensions.Options;

namespace FileHandler.Tests.Modules.Markdown;

public sealed class MarkdownProfileTests
{
    private static MarkdownService Create(FileHandlingOptions? options = null) => new(Options.Create(options ?? new()));

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

    [Fact]
    public async Task LiteralProtocolMarkerIsCollisionSafe()
    {
        var service = Create();
        var bytes = Encoding.UTF8.GetBytes("Read <keepme1> literally and **translate me**.");
        var imported = await service.ImportAsync(new MemoryStream(bytes), TestContext.Current.CancellationToken);
        Assert.Contains("<keepme2>", imported.Texts.Single(), StringComparison.Ordinal);
        var changed = imported.Texts.Single().Replace("translate me", "dịch tôi", StringComparison.Ordinal);
        var exported = await service.ExportAsync(new MemoryStream(bytes), [changed], TestContext.Current.CancellationToken);
        Assert.Empty(exported.Errors);
        Assert.Equal("Read <keepme1> literally and **dịch tôi**.", Encoding.UTF8.GetString(exported.Content!));
    }

    [Fact]
    public async Task RejectsCountEmptyDuplicateUnexpectedAndProtectedContent()
    {
        var service = Create();
        var source = Encoding.UTF8.GetBytes("Hello **world** and `code`.");
        var count = await service.ExportAsync(new MemoryStream(source), [], TestContext.Current.CancellationToken);
        Assert.Contains(count.Errors, x => x.Code == "translation_count_mismatch");

        var empty = await service.ExportAsync(new MemoryStream(source), [" "], TestContext.Current.CancellationToken);
        Assert.Contains(empty.Errors, x => x.Code == "empty_translation");

        var duplicate = await service.ExportAsync(new MemoryStream(source), ["<keepme1>x<keepme1/><keepme1>x<keepme1/> <keepme2><keepme2/>"], TestContext.Current.CancellationToken);
        Assert.Contains(duplicate.Errors, x => x.Code == "duplicate_marker");

        var unexpected = await service.ExportAsync(new MemoryStream(source), ["x <keepme99><keepme99/>"], TestContext.Current.CancellationToken);
        Assert.Contains(unexpected.Errors, x => x.Code == "unexpected_marker");

        var protectedContent = await service.ExportAsync(new MemoryStream(source), ["x <keepme1>y<keepme1/> <keepme2>bad<keepme2/>"], TestContext.Current.CancellationToken);
        Assert.Contains(protectedContent.Errors, x => x.Code == "protected_marker_not_empty");
        Assert.Null(protectedContent.Content);

        var nonCanonical = await service.ExportAsync(new MemoryStream(source), ["x <keepme01>y<keepme01/> <keepme2><keepme2/>"], TestContext.Current.CancellationToken);
        Assert.Contains(nonCanonical.Errors, x => x.Code == "invalid_marker_syntax");

        var overflow = await service.ExportAsync(new MemoryStream(source), ["x <keepme999999999999999999><keepme999999999999999999/>"], TestContext.Current.CancellationToken);
        Assert.Contains(overflow.Errors, x => x.Code == "invalid_marker_syntax");
    }

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

    [Fact]
    public async Task PreservesQuotePrefixAcrossTranslatedSoftBreak()
    {
        var service = Create();
        var source = Encoding.UTF8.GetBytes("> First line\r\n> second line\r\n");
        var imported = await service.ImportAsync(new MemoryStream(source), TestContext.Current.CancellationToken);
        Assert.Equal(["First line\nsecond line"], imported.Texts);
        var exported = await service.ExportAsync(new MemoryStream(source), ["Dòng một\nDòng hai"], TestContext.Current.CancellationToken);
        Assert.Empty(exported.Errors);
        Assert.Equal("> Dòng một\r\n> Dòng hai\r\n", Encoding.UTF8.GetString(exported.Content!));
    }

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
