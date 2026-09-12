using System.Text;
using FileHandler.Api.Common;

namespace FileHandler.Tests.Common;

/// <summary>
/// Shared UTF-8 reader tests for byte preservation, limits and stream ownership.
/// </summary>
public sealed class Utf8TextReaderTests
{

    /// <summary>
    /// Verifies chunked non-seekable reads preserve BOM, Unicode and line endings.
    /// </summary>
    /// <param name="bom">Whether source contains UTF-8 BOM.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadsChunkedNonSeekableSourceWithoutClosingIt(bool bom)
    {
        const string text = "Chào 😀 e\u0301\r\n世界\nlast\rend";
        var bytes = Utf8TextReader.Encode(text, bom);
        using var stream = new ChunkedStream(bytes);
        var (source, error) = await Utf8TextReader.ReadAsync(stream, bytes.Length, default);
        Assert.Null(error);
        Assert.NotNull(source);
        Assert.Equal(text, source.Text);
        Assert.Equal(bom, source.HasBom);
        Assert.Equal(bytes, source.Bytes);
        Assert.Equal(bytes, Utf8TextReader.Encode(source.Text, source.HasBom));
        Assert.True(stream.CanRead);
        Assert.Equal(bytes.Length - (bom ? 3 : 0), Utf8TextReader.GetByteCount(text.AsSpan()));
    }

    /// <summary>
    /// Verifies actual byte limits include BOM and return no partial source.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task RejectsBytesBeyondLimitIncludingBom()
    {
        using var stream = new ChunkedStream(Utf8TextReader.Encode("é", true));
        var (source, error) = await Utf8TextReader.ReadAsync(stream, 4, default);
        Assert.Null(source);
        Assert.Equal("file_too_large", error?.Code);
        Assert.True(stream.CanRead);
    }

    /// <summary>
    /// Verifies malformed UTF-8 and UTF-16 BOM input are rejected.
    /// </summary>
    /// <param name="bytes">Invalid source bytes.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData(new byte[] { 0xc3 })]
    [InlineData(new byte[] { 0xff, 0xfe, 0x41, 0 })]
    [InlineData(new byte[] { 0xed, 0xa0, 0x80 })]
    public async Task RejectsInvalidEncoding(byte[] bytes)
    {
        using var stream = new ChunkedStream(bytes);
        var (source, error) = await Utf8TextReader.ReadAsync(stream, 100, default);
        Assert.Null(source);
        Assert.Equal("invalid_encoding", error?.Code);
    }

    /// <summary>
    /// Verifies strict encode and byte counting reject unpaired surrogates.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void RejectsMalformedUtf16()
    {
        Assert.Throws<EncoderFallbackException>(() => Utf8TextReader.Encode("\ud800", false));
        Assert.Throws<EncoderFallbackException>(() => Utf8TextReader.GetByteCount("\udc00".AsSpan()));
    }

    /// <summary>
    /// Verifies pre-cancelled reads fail even for an empty source.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task PropagatesCancellationWithoutClosingStream()
    {
        using var stream = new ChunkedStream([]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Utf8TextReader.ReadAsync(stream, 0, new CancellationToken(true)));
        Assert.True(stream.CanRead);
    }

    /// <summary>
    /// Simulates a non-seekable stream yielding split UTF-8 characters and BOM bytes.
    /// </summary>
    /// <param name="bytes">Readable source bytes.</param>
    private sealed class ChunkedStream(byte[] bytes) : MemoryStream(bytes)
    {

        /// <summary>
        /// Whether stream supports seeking; always false.
        /// </summary>
        public override bool CanSeek => false;

        /// <summary>
        /// Unsupported stream length.
        /// </summary>
        public override long Length => throw new NotSupportedException();

        /// <summary>
        /// Unsupported stream position.
        /// </summary>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <summary>
        /// Reads at most two bytes to exercise chunk boundaries.
        /// </summary>
        /// <param name="buffer">Destination buffer.</param>
        /// <param name="cancellationToken">Token for cancelling reads.</param>
        /// <returns>Number of bytes read, or zero at end of stream.</returns>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(2, buffer.Length)], cancellationToken);
    }
}
