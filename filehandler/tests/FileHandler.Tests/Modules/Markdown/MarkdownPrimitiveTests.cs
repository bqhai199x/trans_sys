using System.Text;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Markdown;

namespace FileHandler.Tests.Modules.Markdown;

/// <summary>
/// Unit tests for Markdown primitive data structures, line map, and token records.
/// </summary>
public sealed class MarkdownPrimitiveTests
{

    /// <summary>
    /// Verifies line start offsets across newline styles.
    /// </summary>
    /// <param name="text">Text to process.</param>
    /// <param name="expected">Expected line start offsets.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData("", new int[] { 0 })]
    [InlineData("a\r\nb\nc\rd", new int[] { 0, 3, 5, 7 })]
    [InlineData("\n\n", new int[] { 0, 1, 2 })]
    public void BuildLineMap_RecordsEveryLineStart(string text, int[] expected) =>
        Assert.Equal(expected, MarkdownSourceReader.BuildLineMap(text).Starts);

    /// <summary>
    /// Verifies one-based line lookup.
    /// </summary>
    /// <param name="offset">Zero-based character offset.</param>
    /// <param name="expected">Expected one-based line number.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(99, 3)]
    public void GetLine_UsesOneBasedLines(int offset, int expected) =>
        Assert.Equal(expected, new LineMap([0, 3, 5]).GetLine(offset));

    /// <summary>
    /// Verifies line ranges use exclusive end offsets.
    /// </summary>
    /// <param name="start">Inclusive start character offset.</param>
    /// <param name="end">Exclusive end character offset.</param>
    /// <param name="first">Expected first line number.</param>
    /// <param name="last">Expected last line number.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData(0, 3, 1, 1)]
    [InlineData(0, 4, 1, 2)]
    [InlineData(3, 3, 2, 2)]
    public void GetRange_UsesExclusiveEnd(int start, int end, int first, int last) =>
        Assert.Equal(new SourceLineRange(first, last), new LineMap([0, 3, 5]).GetRange(start, end));

    /// <summary>
    /// Verifies Unicode and BOM preservation.
    /// </summary>
    /// <param name="bom">Whether to include UTF-8 BOM.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EncodeAndDecode_PreserveUnicodeAndBom(bool bom)
    {
        const string text = "Xin chào 🌍\r\n";
        var bytes = MarkdownSourceReader.Encode(text, bom);
        var expected = (bom ? Encoding.UTF8.Preamble.ToArray() : []).Concat(Encoding.UTF8.GetBytes(text));
        Assert.Equal(expected, bytes);
        var source = MarkdownSourceReader.DecodeUtf8(bytes);
        Assert.Equal(text, source.Text);
        Assert.Equal(bom, source.HasBom);
        Assert.Equal(bytes, source.Bytes);
    }

    /// <summary>
    /// Verifies strict encoding rejects malformed input.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void StrictEncoding_RejectsMalformedInput()
    {
        Assert.Throws<DecoderFallbackException>(() => MarkdownSourceReader.DecodeUtf8([0xc3]));
        Assert.Throws<EncoderFallbackException>(() => MarkdownSourceReader.Encode("\ud800", false));
    }

    /// <summary>
    /// Verifies exact byte limits and caller-owned stream lifetime.
    /// </summary>
    /// <param name="length">Source byte count and read limit.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(100000)]
    public async Task ReadAsync_AcceptsExactByteLimitAndLeavesStreamOpen(int length)
    {
        using var stream = new MemoryStream(Enumerable.Repeat((byte)'a', length).ToArray());
        var result = await MarkdownSourceReader.ReadAsync(stream, length, default);
        Assert.Null(result.Error);
        Assert.Equal(length, result.Source!.Bytes.Length);
        Assert.True(stream.CanRead);
    }

    /// <summary>
    /// Verifies read cancellation propagates to callers.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task ReadAsync_PropagatesCancellation()
    {
        using var stream = new MemoryStream([65]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MarkdownSourceReader.ReadAsync(stream, 10, new CancellationToken(true)));
    }

    /// <summary>
    /// Verifies matching marker delimiters parse correctly.
    /// </summary>
    /// <param name="id">Marker ID.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void MarkerDelimiters_ParseAsMatchingPair(int id)
    {
        Assert.Equal($"<keepme{id}>", MarkdownMarkerCodec.Open(id));
        Assert.Equal($"<keepme{id}/>", MarkdownMarkerCodec.Close(id));
        Assert.Equal(new[]
        {
            new MarkerToken(false, 0, "before", false),
            new MarkerToken(true, id, $"<keepme{id}>", false),
            new MarkerToken(false, 0, "inside", false),
            new MarkerToken(true, id, $"<keepme{id}/>", true),
            new MarkerToken(false, 0, "after", false)
        }, MarkdownMarkerCodec.Parse($"before<keepme{id}>inside<keepme{id}/>after"));
    }

    /// <summary>
    /// Verifies malformed markers remain literal text.
    /// </summary>
    /// <param name="text">Text to process.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData("<keepme>")]
    [InlineData("<keepme0>")]
    [InlineData("<keepme-1>")]
    [InlineData("<keepme1")]
    [InlineData("<keepme1/")]
    [InlineData("<keepme2147483648>")]
    [InlineData("<Keepme1>")]
    public void Parse_PreservesMalformedMarkersAsText(string text)
    {
        Assert.Equal(new MarkerToken(false, 0, text, false), Assert.Single(MarkdownMarkerCodec.Parse(text)));
        Assert.Empty(MarkdownMarkerCodec.FindReservedIds(text));
    }

    /// <summary>
    /// Verifies empty input and noncanonical marker parsing.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Parse_HandlesEmptyAndNonCanonicalMarkers()
    {
        Assert.Empty(MarkdownMarkerCodec.Parse(""));
        Assert.Equal(new MarkerToken(true, 1, "<keepme01>", false), Assert.Single(MarkdownMarkerCodec.Parse("<keepme01>")));
    }

    /// <summary>
    /// Verifies allocation skips reserved and previously allocated IDs.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void AllocateId_SkipsReservedAndPreviouslyAllocatedIds()
    {
        var reserved = MarkdownMarkerCodec.FindReservedIds("<keepme1><keepme0003/><keepme1><keepme2147483647>");
        Assert.Equal(new[] { 1, 3, int.MaxValue }, reserved.Order());
        var allocator = new MarkerAllocationContext(reserved);
        Assert.Equal(2, allocator.AllocateId());
        Assert.Equal(4, allocator.AllocateId());
        Assert.Equal(5, allocator.AllocateId());
    }
}
