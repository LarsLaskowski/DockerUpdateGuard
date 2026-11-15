namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Read-only stream that reads the response body under the request's deadline and releases the response content when disposed
/// </summary>
internal sealed class DeadlineReadStream : Stream
{
    #region Fields

    private readonly Stream _innerStream;
    private readonly HttpContent _innerContent;
    private readonly CancellationTokenSource _deadlineSource;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="innerStream">Response body stream to read</param>
    /// <param name="innerContent">Response content released together with the stream</param>
    /// <param name="deadlineSource">Source of the request deadline, disposed together with the stream</param>
    public DeadlineReadStream(Stream innerStream, HttpContent innerContent, CancellationTokenSource deadlineSource)
    {
        _innerStream = innerStream;
        _innerContent = innerContent;
        _deadlineSource = deadlineSource;
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
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.CanBeCanceled == false)
        {
            return await _innerStream.ReadAsync(buffer, _deadlineSource.Token).ConfigureAwait(false);
        }

        using (var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _deadlineSource.Token))
        {
            return await _innerStream.ReadAsync(buffer, linkedSource.Token).ConfigureAwait(false);
        }
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

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _innerStream.Dispose();
            _innerContent.Dispose();
            _deadlineSource.Dispose();
        }
    }

    #endregion // Stream
}