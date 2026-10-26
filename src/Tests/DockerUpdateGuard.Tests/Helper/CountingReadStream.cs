namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Read-only stream of repeated bytes that reports every read
/// </summary>
internal sealed class CountingReadStream : Stream
{
    #region Fields

    private readonly byte _value;
    private readonly long _length;
    private readonly Action<int> _onRead;
    private long _position;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="value">Byte the stream repeats</param>
    /// <param name="length">Number of bytes of the stream</param>
    /// <param name="onRead">Callback that receives the number of bytes of each read</param>
    public CountingReadStream(byte value, long length, Action<int> onRead)
    {
        _value = value;
        _length = length;
        _onRead = onRead;
    }

    #endregion // Constructors

    #region Stream

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => _length;

    /// <inheritdoc/>
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        var count = (int)Math.Min(buffer.Length, _length - _position);

        buffer[..count].Fill(_value);
        _position += count;
        _onRead(count);

        return count;
    }

    /// <inheritdoc/>
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Read(buffer.Span));
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return Task.FromResult(Read(buffer.AsSpan(offset, count)));
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    #endregion // Stream
}