using FileHandler.Api.Common;

namespace FileHandler.Api.Modules.Office;

/// <summary>
/// Seekable stream wrapper enforcing high-water byte limit during serialization.
/// </summary>
public sealed class OfficeBoundedStream : Stream
{

    /// <summary>
    /// Underlying target stream.
    /// </summary>
    private readonly Stream _inner;

    /// <summary>
    /// Maximum allowed high-water length in bytes.
    /// </summary>
    private readonly long _maxBytes;

    /// <summary>
    /// Highest observed stream length during lifetime.
    /// </summary>
    private long _highWaterLength;

    /// <summary>
    /// Gets highest byte length observed during streaming.
    /// </summary>
    public long HighWaterLength => _highWaterLength;

    /// <summary>
    /// Creates bounded stream wrapper.
    /// </summary>
    /// <param name="inner">Seekable underlying stream.</param>
    /// <param name="maxBytes">Maximum allowable high-water length.</param>
    /// <exception cref="ArgumentNullException">Inner stream is null.</exception>
    /// <exception cref="ArgumentException">Inner stream is not seekable.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Max bytes is negative.</exception>
    public OfficeBoundedStream(Stream inner, long maxBytes)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (!inner.CanSeek)
            throw new ArgumentException("Underlying stream must support seek operations.", nameof(inner));
        if (maxBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "Maximum bytes limit must be non-negative.");
        _maxBytes = maxBytes;
        _highWaterLength = inner.Length;
        if (_highWaterLength > _maxBytes)
            throw new FileLimitException("output_too_large");
    }

    /// <summary>
    /// Whether underlying stream supports reads.
    /// </summary>
    public override bool CanRead => _inner.CanRead;

    /// <summary>
    /// Whether underlying stream supports seeking.
    /// </summary>
    public override bool CanSeek => _inner.CanSeek;

    /// <summary>
    /// Whether underlying stream supports writes.
    /// </summary>
    public override bool CanWrite => _inner.CanWrite;

    /// <summary>
    /// Current underlying stream length in bytes.
    /// </summary>
    public override long Length => _inner.Length;

    /// <summary>
    /// Current position in bytes, bounded by maximum output size.
    /// </summary>
    public override long Position
    {
        get => _inner.Position;
        set
        {
            if (value > _maxBytes)
                throw new FileLimitException("output_too_large");
            _inner.Position = value;
            if (value > _highWaterLength)
                _highWaterLength = value;
        }
    }

    /// <summary>
    /// Flushes buffered bytes.
    /// </summary>
    /// <returns>No return value.</returns>
    public override void Flush() => _inner.Flush();

    /// <summary>
    /// Flushes buffered bytes asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task completing after operation finishes.</returns>
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    /// <summary>
    /// Reads bytes from underlying stream.
    /// </summary>
    /// <param name="buffer">Byte buffer.</param>
    /// <param name="offset">Buffer offset or seek displacement in bytes.</param>
    /// <param name="count">Byte count.</param>
    /// <returns>Number of bytes read; zero at end of stream.</returns>
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    /// <summary>
    /// Reads bytes asynchronously from underlying stream.
    /// </summary>
    /// <param name="buffer">Byte buffer.</param>
    /// <param name="offset">Buffer offset or seek displacement in bytes.</param>
    /// <param name="count">Byte count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of bytes read; zero at end of stream.</returns>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _inner.ReadAsync(buffer, offset, count, cancellationToken);

    /// <summary>
    /// Reads bytes asynchronously from underlying stream.
    /// </summary>
    /// <param name="buffer">Byte buffer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of bytes read; zero at end of stream.</returns>
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _inner.ReadAsync(buffer, cancellationToken);

    /// <summary>
    /// Moves position within output byte budget.
    /// </summary>
    /// <param name="offset">Buffer offset or seek displacement in bytes.</param>
    /// <param name="origin">Seek reference point.</param>
    /// <returns>New position in bytes.</returns>
    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _inner.Position + offset,
            SeekOrigin.End => _inner.Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        if (target > _maxBytes)
            throw new FileLimitException("output_too_large");
        var result = _inner.Seek(offset, origin);
        if (result > _highWaterLength)
            _highWaterLength = result;
        return result;
    }

    /// <summary>
    /// Resizes underlying stream within output byte budget.
    /// </summary>
    /// <param name="value">Requested stream length in bytes.</param>
    /// <returns>No return value.</returns>
    public override void SetLength(long value)
    {
        if (value > _maxBytes)
            throw new FileLimitException("output_too_large");
        _inner.SetLength(value);
        if (value > _highWaterLength)
            _highWaterLength = value;
    }

    /// <summary>
    /// Writes bytes after checking output byte budget.
    /// </summary>
    /// <param name="buffer">Byte buffer.</param>
    /// <param name="offset">Buffer offset or seek displacement in bytes.</param>
    /// <param name="count">Byte count.</param>
    /// <returns>No return value.</returns>
    public override void Write(byte[] buffer, int offset, int count)
    {
        var target = _inner.Position + count;
        if (target > _maxBytes)
            throw new FileLimitException("output_too_large");
        _inner.Write(buffer, offset, count);
        if (target > _highWaterLength)
            _highWaterLength = target;
    }

    /// <summary>
    /// Writes bytes after checking output byte budget.
    /// </summary>
    /// <param name="buffer">Byte buffer.</param>
    /// <returns>No return value.</returns>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var target = _inner.Position + buffer.Length;
        if (target > _maxBytes)
            throw new FileLimitException("output_too_large");
        _inner.Write(buffer);
        if (target > _highWaterLength)
            _highWaterLength = target;
    }

    /// <summary>
    /// Writes bytes asynchronously after checking output byte budget.
    /// </summary>
    /// <param name="buffer">Byte buffer.</param>
    /// <param name="offset">Buffer offset or seek displacement in bytes.</param>
    /// <param name="count">Byte count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task completing after operation finishes.</returns>
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var target = _inner.Position + count;
        if (target > _maxBytes)
            throw new FileLimitException("output_too_large");
        await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        if (target > _highWaterLength)
            _highWaterLength = target;
    }

    /// <summary>
    /// Writes bytes asynchronously after checking output byte budget.
    /// </summary>
    /// <param name="buffer">Byte buffer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task completing after operation finishes.</returns>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var target = _inner.Position + buffer.Length;
        if (target > _maxBytes)
            throw new FileLimitException("output_too_large");
        await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (target > _highWaterLength)
            _highWaterLength = target;
    }

    /// <summary>
    /// Releases underlying stream resources.
    /// </summary>
    /// <param name="disposing">True when disposing managed resources.</param>
    /// <returns>No return value.</returns>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>
    /// Releases underlying stream resources asynchronously.
    /// </summary>
    /// <returns>Task completing after operation finishes.</returns>
    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
