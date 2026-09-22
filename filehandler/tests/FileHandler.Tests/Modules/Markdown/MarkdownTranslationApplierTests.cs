using System.Text;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Markdown;

namespace FileHandler.Tests.Modules.Markdown;

/// <summary>
/// Unit tests for translation validation, marker restoration, and source patching.
/// </summary>
public sealed class MarkdownTranslationApplierTests
{

    /// <summary>
    /// Creates extraction data for translation tests.
    /// </summary>
    /// <param name="source">Original Markdown source.</param>
    /// <returns>Extraction data for translation tests.</returns>
    private static MarkdownExtraction Extraction(string source = "Hello") =>
        new MarkdownExtractor(MarkdownProfile.CreatePipeline()).Extract(
            MarkdownSourceReader.DecodeUtf8(Encoding.UTF8.GetBytes(source)), 100, default);

    /// <summary>
    /// Verifies reverse-order patches preserve surrounding source.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Apply_PatchesFromEndWithoutChangingSurroundingSource()
    {
        var result = MarkdownTranslationApplier.Apply(Extraction("# One\n\nTwo\n"), ["Longer heading", "X"], new(), default);
        Assert.Empty(result.Errors);
        Assert.Equal("# Longer heading\n\nX\n", result.Text);
    }

    /// <summary>
    /// Verifies unchanged translations preserve original escapes.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Apply_IdentityPreservesOriginalEscapes()
    {
        const string source = "Escaped \\*star\\* &amp; `code`\r\n";
        var extraction = Extraction(source);
        var result = MarkdownTranslationApplier.Apply(extraction, extraction.Units.Select(x => x.Text).ToArray(), new(), default);
        Assert.Empty(result.Errors);
        Assert.Equal(source, result.Text);
    }

    /// <summary>
    /// Verifies translation errors include source locations.
    /// </summary>
    /// <param name="translation">Translated unit text.</param>
    /// <param name="code">Machine-readable error code.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData(null, "invalid_translation")]
    [InlineData("", "empty_translation")]
    [InlineData(" \t", "empty_translation")]
    [InlineData("long", "translation_too_long")]
    public void Apply_ValidatesEachTranslationWithSourceLocation(string? translation, string code)
    {
        var result = MarkdownTranslationApplier.Apply(Extraction(), [translation!], new() { MaxTranslationChars = 3 }, default);
        if (code == "empty_translation")
        {
            Assert.Equal("Hello", result.Text);
            Assert.Empty(result.Errors);
            var skipped = Assert.Single(result.Skipped);
            Assert.Equal(code, skipped.Code);
            Assert.Equal(0, skipped.UnitIndex);
            Assert.Equal(new SourceLineRange(1, 1), skipped.Location.Line);
            return;
        }
        Assert.Null(result.Text);
        var error = Assert.Single(result.Errors);
        Assert.Equal(code, error.Code);
        Assert.Equal(0, error.Index);
        Assert.Equal(new SourceLineRange(1, 1), error.Line);
    }

    /// <summary>
    /// Verifies translations at character limits are accepted.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Apply_AcceptsExactCharacterLimit()
    {
        var result = MarkdownTranslationApplier.Apply(Extraction(), ["abc"], new() { MaxTranslationChars = 3 }, default);
        Assert.Empty(result.Errors);
        Assert.Equal("abc", result.Text);
    }

    /// <summary>
    /// Verifies extraction errors and translation count mismatches propagate.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Apply_PropagatesExtractionErrorsAndCountMismatch()
    {
        var extraction = Extraction() with { Errors = [new("source_error", "Invalid source")] };
        var result = MarkdownTranslationApplier.Apply(extraction, [], new(), default);
        Assert.Null(result.Text);
        Assert.Equal(new[] { "source_error", "translation_count_mismatch" }, result.Errors.Select(x => x.Code));
    }

    /// <summary>
    /// Verifies invalid markers preserve affected source unit.
    /// </summary>
    /// <param name="translation">Translated unit text.</param>
    /// <param name="code">Machine-readable error code.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData("<ox:r0>x", "invalid_marker_syntax")]
    [InlineData("<ox:r99>x</ox:r99><ox:r1>!</ox:r1>", "invalid_marker_syntax")]
    [InlineData("<ox:r0>x</ox:r0><ox:r0>y</ox:r0>", "invalid_marker_syntax")]
    [InlineData("</ox:r0>x<ox:r0>", "invalid_marker_syntax")]
    [InlineData("<ox:r00>x</ox:r00>", "invalid_marker_syntax")]
    [InlineData("<ox:r2147483648>x</ox:r2147483648>", "invalid_marker_syntax")]
    public void Apply_SkipsInvalidMarkerUnit(string translation, string code)
    {
        var result = MarkdownTranslationApplier.Apply(Extraction("**Hello**!"), [translation], new(), default);
        Assert.Equal("**Hello**!", result.Text);
        Assert.Empty(result.Errors);
        Assert.Contains(result.Skipped, x => x.Code == code);
    }

    /// <summary>
    /// Verifies formatting and protected content restoration.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Apply_RestoresFormattingAndProtectedContent()
    {
        var result = MarkdownTranslationApplier.Apply(Extraction("**Hello** `code`"),
            ["<ox:r0>Bonjour</ox:r0><ox:k0/><ox:k1/>"], new(), default);
        Assert.Empty(result.Errors);
        Assert.Equal("**Bonjour** `code`", result.Text);
        var invalid = MarkdownTranslationApplier.Apply(Extraction("Read `code`"),
            ["<ox:r0>Lire </ox:r0><ox:k0>changed</ox:k0>"], new(), default);
        Assert.Equal("Read `code`", invalid.Text);
        Assert.Empty(invalid.Errors);
        Assert.Contains(invalid.Skipped, x => x.Code == "invalid_marker_syntax");
    }

    /// <summary>
    /// Verifies raw newlines cannot replace protected soft-break tokens.
    /// </summary>
    /// <param name="translation">Translated unit text.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    [InlineData("a\r\nb")]
    public void Apply_RejectsMissingSoftBreakTokens(string translation)
    {
        var result = MarkdownTranslationApplier.Apply(Extraction("> One\r\n> Two"), [translation], new(), default);
        Assert.Contains(result.Skipped, e => e.Code == "invalid_marker_syntax");
        Assert.Empty(result.Errors);
        Assert.Equal("> One\r\n> Two", result.Text);
    }

    /// <summary>
    /// Verifies translated Markdown punctuation is escaped.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Apply_EscapesMarkdownPunctuation()
    {
        var result = MarkdownTranslationApplier.Apply(Extraction(), ["\\`*_{}[]()<>#!|+-=~"], new(), default);
        Assert.Empty(result.Errors);
        Assert.Equal("\\\\\\`\\*\\_\\{\\}\\[\\]\\(\\)\\<\\>\\#\\!\\|\\+\\-\\=\\~", result.Text);
    }

    /// <summary>
    /// Verifies rejection of heading newlines and overlapping patches.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Apply_RejectsHeadingNewlinesAndOverlappingPatches()
    {
        var heading = MarkdownTranslationApplier.Apply(Extraction("# Hello"), ["a\nb"], new(), default);
        Assert.Equal("invalid_structure", Assert.Single(heading.Skipped).Code);
        Assert.Equal("# Hello", heading.Text);
        Assert.Empty(heading.Errors);
        var extraction = Extraction();
        extraction = extraction with { Units = [extraction.Units[0], extraction.Units[0]] };
        var conflict = MarkdownTranslationApplier.Apply(extraction, ["a", "b"], new(), default);
        Assert.Equal("patch_conflict", Assert.Single(conflict.Errors).Code);
        Assert.Null(conflict.Text);
    }

    /// <summary>
    /// Verifies translation cancellation propagates to callers.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Apply_PropagatesCancellation() => Assert.Throws<OperationCanceledException>(() =>
        MarkdownTranslationApplier.Apply(Extraction(), ["Bonjour"], new(), new CancellationToken(true)));

    /// <summary>
    /// Verifies documents with internal anchors allow translating non-heading units when headings remain unchanged.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Apply_WithInternalAnchors_AllowsTranslatingPrecedingParagraph()
    {
        const string source = "Paragraph intro\n\n# Heading 1\n\n[link](#heading-1)\n";
        var extraction = Extraction(source);
        Assert.True(extraction.HasInternalLinks);
        var translations = new[] { "Paragraph translated", "Heading 1", extraction.Units[2].Text };
        var result = MarkdownTranslationApplier.Apply(extraction, translations, new(), default);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Text);
    }
}
