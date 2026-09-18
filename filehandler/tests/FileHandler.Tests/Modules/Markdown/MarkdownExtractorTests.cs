using System.Text;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Markdown;
using Markdig.Syntax;

namespace FileHandler.Tests.Modules.Markdown;

/// <summary>
/// Unit tests for Markdown AST traversal and translation unit extraction.
/// </summary>
public sealed class MarkdownExtractorTests
{

    /// <summary>
    /// Extractor for Markdown translation units.
    /// </summary>
    private readonly MarkdownExtractor _extractor = new(MarkdownProfile.CreatePipeline());

    /// <summary>
    /// Extracts translation units while preserving Markdown syntax with markers.
    /// </summary>
    /// <param name="text">Text to process.</param>
    /// <param name="maxUnits">Maximum translation unit count.</param>
    /// <returns>Translation units, source metadata, and extraction errors.</returns>
    private MarkdownExtraction Extract(string text, int maxUnits = 100) =>
        _extractor.Extract(MarkdownSourceReader.DecodeUtf8(Encoding.UTF8.GetBytes(text)), maxUnits, default);

    /// <summary>
    /// Verifies source spans for headings and paragraphs.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void ParseDocument_PreservesHeadingAndParagraphSpans()
    {
        var document = _extractor.ParseDocument("# Hello\n\nWorld");
        Assert.Equal(2, document.Count);
        Assert.Equal(1, Assert.IsType<HeadingBlock>(document[0]).Level);
        Assert.IsType<ParagraphBlock>(document[1]);
        Assert.Equal(9, document[1].Span.Start);
    }

    /// <summary>
    /// Verifies unit source mapping and internal link detection.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Extract_MapsUnitsToSourceAndDetectsInternalLinks()
    {
        var result = Extract("# Hello\n\n[Jump](#hello)");
        Assert.Empty(result.Errors);
        Assert.True(result.HasInternalLinks);
        Assert.Equal(2, result.Units.Count);
        var heading = result.Units[0];
        Assert.True(heading.IsHeading);
        Assert.Equal("Hello", result.Source.Text[heading.Start..heading.End]);
        Assert.Equal(new SourceLineRange(1, 1), heading.Line);
        var paragraph = result.Units[1];
        Assert.False(paragraph.IsHeading);
        Assert.Equal(new SourceLineRange(3, 3), paragraph.Line);
        Assert.Equal(new MarkerDefinition(1, MarkerKind.Formatting, "[", "](#hello)"), paragraph.Markers[1]);
    }

    /// <summary>
    /// Verifies non-translatable content produces no units.
    /// </summary>
    /// <param name="text">Text to process.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData("")]
    [InlineData("---\ntitle: Metadata\n---\n")]
    [InlineData("```cs\nvar a = 1;\n```\n")]
    [InlineData("---\n")]
    [InlineData("`only code`")]
    public void Extract_SkipsNonTranslatableContent(string text) => Assert.Empty(Extract(text).Units);

    /// <summary>
    /// Verifies source newline and prefix detection.
    /// </summary>
    /// <param name="source">Original Markdown source.</param>
    /// <param name="replacement">Expected newline sequence and prefix.</param>
    /// <param name="softBreak">Expected soft line break flag.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData("One\nTwo", "\n", true)]
    [InlineData("> One\r\n> Two", "\r\n> ", true)]
    [InlineData("One\r\n", "\r\n", false)]
    [InlineData("One", "\n", false)]
    public void Extract_RecordsNewlinePolicy(string source, string replacement, bool softBreak)
    {
        var unit = Assert.Single(Extract(source).Units);
        Assert.Equal(replacement, unit.NewlineReplacement);
        Assert.Equal(softBreak, unit.HasSoftBreak);
    }

    /// <summary>
    /// Verifies protected inline content retains original syntax.
    /// </summary>
    /// <param name="source">Original Markdown source.</param>
    /// <param name="protectedSource">Expected protected source content.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData("Read `code`", "`code`")]
    [InlineData("Read <https://example.com>", "<https://example.com>")]
    [InlineData("Read <b>text</b>", "<b>")]
    public void Extract_ProtectsInlineSource(string source, string protectedSource)
    {
        var unit = Assert.Single(Extract(source).Units);
        Assert.Contains(unit.Markers.Values, marker => marker.Kind == MarkerKind.Protected && marker.OpenSource == protectedSource);
        Assert.Contains("<ox:k0/>", unit.Text);
    }

    /// <summary>
    /// Verifies nested formatting markers and decoded literal text.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Extract_EncodesNestedFormattingAndDecodedLiterals()
    {
        var unit = Assert.Single(Extract("Read **bold *inner*** &amp; \\*literal\\*").Units);
        Assert.Equal("<ox:r0>Read </ox:r0><ox:r1>bold </ox:r1><ox:r2>inner</ox:r2><ox:k0/><ox:k1/><ox:r3> *literal*</ox:r3>", unit.Text);
        Assert.Equal("&amp;", unit.Markers[3].OpenSource);
        Assert.Equal("**", unit.Markers[1].OpenSource);
        Assert.Equal("*", unit.Markers[2].CloseSource);
    }

    /// <summary>
    /// Verifies unit limits and extraction cancellation.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Extract_EnforcesUnitLimitAndCancellation()
    {
        Assert.Single(Extract("One", 1).Units);
        var result = Extract("One\n\nTwo", 1);
        Assert.Empty(result.Units);
        Assert.Equal("too_many_units", Assert.Single(result.Errors).Code);
        Assert.Throws<OperationCanceledException>(() => _extractor.Extract(
            MarkdownSourceReader.DecodeUtf8(Encoding.UTF8.GetBytes("One")), 10, new CancellationToken(true)));
    }

    /// <summary>
    /// Verifies block structure and inline target validation.
    /// </summary>
    /// <param name="before">Original Markdown text.</param>
    /// <param name="after">Candidate Markdown text.</param>
    /// <param name="valid">Expected structure validation result.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData("# Hello", "# Bonjour", true)]
    [InlineData("[Hello](https://a.test)", "[Bonjour](https://a.test)", true)]
    [InlineData("Hello", "Hello\n\nWorld", false)]
    [InlineData("[Hello](https://a.test)", "[Hello](https://b.test)", false)]
    [InlineData("Read `one`", "Read `two`", false)]
    [InlineData("Read <b>x</b>", "Read <i>x</i>", false)]
    public void ValidateStructure_ProtectsBlocksAndInlineTargets(string before, string after, bool valid)
    {
        var errors = _extractor.ValidateStructure(before, after);
        if (valid) Assert.Empty(errors);
        else Assert.Equal("invalid_structure", Assert.Single(errors).Code);
    }
}
