namespace FileHandler.Api.Common;

/// <summary>
/// Counts actual consumed bytes without closing caller-owned input.
/// </summary>
internal sealed class LimitedReadStream : Stream
{

    /// <summary>
    /// Caller-owned input.
    /// </summary>
    private readonly Stream _inner;

    /// <summary>
    /// Remaining permitted bytes.
    /// </summary>
    private long _remaining;

    /// <summary>
    /// Stable quota error code.
    /// </summary>
    private readonly string _code;

    /// <summary>
    /// Creates a counting input stream.
    /// </summary>
    /// <param name="inner">Caller-owned readable stream.</param>
    /// <param name="maxBytes">Maximum consumed bytes.</param>
    /// <param name="code">Quota error code.</param>
    internal LimitedReadStream(Stream inner, long maxBytes, string code)
    {
        _inner = inner;
        _remaining = maxBytes;
        _code = code;
    }

    /// <summary>
    /// Indicates readable stream.
    /// </summary>
    public override bool CanRead => true;

    /// <summary>
    /// Disables seeking to prevent resetting accounting.
    /// </summary>
    public override bool CanSeek => false;

    /// <summary>
    /// Disables writes.
    /// </summary>
    public override bool CanWrite => false;

    /// <summary>
    /// Length is unavailable for streaming input.
    /// </summary>
    public override long Length => throw new NotSupportedException();

    /// <summary>
    /// Position is unavailable for streaming input.
    /// </summary>
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    /// <summary>
    /// Reads at most one byte beyond remaining budget to detect overflow.
    /// </summary>
    /// <param name="buffer">Destination bytes.</param>
    /// <param name="offset">Destination offset.</param>
    /// <param name="count">Requested byte count.</param>
    /// <returns>Consumed byte count.</returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, (int)Math.Min(count, _remaining + 1));
        _remaining -= read;
        if (_remaining < 0) throw new FileLimitException(_code);
        return read;
    }

    /// <summary>
    /// Reads asynchronously with actual byte accounting.
    /// </summary>
    /// <param name="buffer">Destination memory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Consumed byte count.</returns>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _remaining + 1)], cancellationToken).ConfigureAwait(false);
        _remaining -= read;
        if (_remaining < 0) throw new FileLimitException(_code);
        return read;
    }

    /// <summary>
    /// Rejects seeking.
    /// </summary>
    /// <param name="offset">Ignored offset.</param>
    /// <param name="origin">Ignored origin.</param>
    /// <returns>Always throws.</returns>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <summary>
    /// Rejects resizing.
    /// </summary>
    /// <param name="value">Ignored length.</param>
    /// <returns>No return value.</returns>
    /// <exception cref="NotSupportedException">Read-only stream cannot be resized.</exception>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>
    /// Rejects writes.
    /// </summary>
    /// <param name="buffer">Ignored buffer.</param>
    /// <param name="offset">Ignored offset.</param>
    /// <param name="count">Ignored count.</param>
    /// <returns>No return value.</returns>
    /// <exception cref="NotSupportedException">Read-only stream cannot be written.</exception>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>
    /// Does nothing for read-only input.
    /// </summary>
    /// <returns>No return value.</returns>
    public override void Flush() { }
}
