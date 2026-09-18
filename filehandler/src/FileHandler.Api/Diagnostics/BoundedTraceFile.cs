namespace FileHandler.Api.Diagnostics;

/// <summary>
/// Enforces a disk byte ceiling during synchronous JSON serialization.
/// </summary>
internal sealed class BoundedTraceFile : FileStream
{

    /// <summary>
    /// Maximum permitted file length in bytes.
    /// </summary>
    private readonly long _maxBytes;

    /// <summary>
    /// Creates an empty bounded trace file.
    /// </summary>
    /// <param name="path">Trace destination.</param>
    /// <param name="maxBytes">Maximum output bytes.</param>
    internal BoundedTraceFile(string path, long maxBytes) : base(path, FileMode.Create, FileAccess.Write, FileShare.Read)
    {
        _maxBytes = maxBytes;
    }

    /// <summary>
    /// Writes a JSON byte segment within file budget.
    /// </summary>
    /// <param name="buffer">Serialized bytes.</param>
    /// <returns>No return value.</returns>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (Position + buffer.Length > _maxBytes) throw new TraceSizeException();
        var bytes = buffer.ToArray();
        base.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Writes an array segment within file budget.
    /// </summary>
    /// <param name="buffer">Serialized bytes.</param>
    /// <param name="offset">First byte offset.</param>
    /// <param name="count">Byte count.</param>
    /// <returns>No return value.</returns>
    public override void Write(byte[] buffer, int offset, int count)
    {
        if (Position + count > _maxBytes) throw new TraceSizeException();
        base.Write(buffer, offset, count);
    }

    /// <summary>
    /// Writes serialized bytes asynchronously within file budget.
    /// </summary>
    /// <param name="buffer">Serialized bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task completing after bytes are written.</returns>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (Position + buffer.Length > _maxBytes) throw new TraceSizeException();
        return base.WriteAsync(buffer, cancellationToken);
    }
}

/// <summary>
/// Signals a trace-only byte ceiling without failing document processing.
/// </summary>
internal sealed class TraceSizeException : IOException;
