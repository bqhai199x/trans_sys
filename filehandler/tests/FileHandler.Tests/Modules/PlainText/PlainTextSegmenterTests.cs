using FileHandler.Api.Common;
using FileHandler.Api.Modules.PlainText;

namespace FileHandler.Tests.Modules.PlainText;

/// <summary>
/// Paragraph scanning tests for literal text and exact source locations.
/// </summary>
public sealed class PlainTextSegmenterTests
{

    /// <summary>
    /// Verifies CR, LF and CRLF delimiters preserve paragraph contents and line ranges.
    /// </summary>
    /// <param name="newline">Physical line terminator.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void PreservesParagraphSpansAndLineRanges(string newline)
    {
        var paragraph = $"  A\t{newline}B  ";
        var source = $"{newline}{paragraph}{newline} \t{newline}C{newline}";
        var (units, error) = PlainTextSegmenter.Segment(source, 2, default);
        Assert.Null(error);
        Assert.Equal(2, units.Count);
        Assert.Equal(paragraph, source[units[0].Start..units[0].End]);
        Assert.Equal(new SourceLineRange(2, 3), units[0].Line);
        Assert.Equal(new SourceLineRange(5, 5), units[1].Line);
        Assert.Equal("C", source[units[1].Start..units[1].End]);
        Assert.Equal(newline.Length, units[0].Start);
        Assert.Equal(newline.Length + paragraph.Length, units[0].End);
    }

    /// <summary>
    /// Verifies mixed line endings and Unicode whitespace separate paragraphs correctly.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void HandlesMixedEndingsAndUnicodeWhitespace()
    {
        const string source = "\r\nA\rB\n\u00a0\u2003\r\nC\u2028D\r\n\r\n";
        var (units, error) = PlainTextSegmenter.Segment(source, 2, default);
        Assert.Null(error);
        Assert.Equal(new[] { "A\rB", "C\u2028D" }, units.Select(x => source[x.Start..x.End]));
        Assert.Equal(new SourceLineRange(2, 3), units[0].Line);
        Assert.Equal(new SourceLineRange(5, 5), units[1].Line);
    }

    /// <summary>
    /// Verifies empty and whitespace-only files do not consume unit quota.
    /// </summary>
    /// <param name="source">Source without translatable paragraphs.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n\u00a0\n\r")]
    [InlineData("\n\n")]
    public void AllowsEmptySourceWithZeroQuota(string source)
    {
        var result = PlainTextSegmenter.Segment(source, 0, default);
        Assert.Empty(result.Units);
        Assert.Null(result.Error);
    }

    /// <summary>
    /// Verifies unit quota rejects excess paragraphs without returning a prefix.
    /// </summary>
    /// <param name="source">Source exceeding configured paragraph count.</param>
    /// <param name="limit">Allowed paragraph count.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData("A", 0)]
    [InlineData("A\n\nB", 1)]
    [InlineData("A\n\nB\n", 1)]
    public void RejectsExcessUnits(string source, int limit)
    {
        var result = PlainTextSegmenter.Segment(source, limit, default);
        Assert.Empty(result.Units);
        Assert.Equal("too_many_units", result.Error?.Code);
    }

    /// <summary>
    /// Verifies syntax-like content remains one literal paragraph.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void TreatsSyntaxAndNumbersAsLiteralText()
    {
        const string source = "# Heading\n```\n**bold** [link](url) <b> &copy; <keepme01> \\\n123!?";
        var (units, error) = PlainTextSegmenter.Segment(source, 1, default);
        Assert.Null(error);
        Assert.Equal(source, source[Assert.Single(units).Start..units[0].End]);
    }

    /// <summary>
    /// Verifies cancellation is observed before scanning empty and long sources.
    /// </summary>
    /// <param name="length">Source character count.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1_000_000)]
    public void ObservesCancellation(int length) =>
        Assert.ThrowsAny<OperationCanceledException>(() =>
            PlainTextSegmenter.Segment(new string('a', length), 10, new CancellationToken(true)));
}
