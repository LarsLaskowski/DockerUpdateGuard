namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Read-only stream that reads the response body under the request's deadline and releases the response content when disposed
/// </summary>
internal sealed class DeadlineReadStream : Stream
{
    #region Const fields

    /// <summary>
    /// Message of the skeleton bodies
    /// </summary>
    private const string SkeletonMessage = "Skeleton: not implemented";

    #endregion // Const fields

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
    public override bool CanRead => throw new InvalidOperationException(SkeletonMessage);

    /// <inheritdoc/>
    public override bool CanSeek => throw new InvalidOperationException(SkeletonMessage);

    /// <inheritdoc/>
    public override bool CanWrite => throw new InvalidOperationException(SkeletonMessage);

    /// <inheritdoc/>
    public override long Length => throw new InvalidOperationException(SkeletonMessage);

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new InvalidOperationException(SkeletonMessage);
        set => throw new InvalidOperationException(SkeletonMessage);
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        throw new InvalidOperationException(SkeletonMessage);
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new InvalidOperationException(SkeletonMessage);
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException(SkeletonMessage);
    }

    /// <inheritdoc/>
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(SkeletonMessage);
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new InvalidOperationException(SkeletonMessage);
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        throw new InvalidOperationException(SkeletonMessage);
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new InvalidOperationException(SkeletonMessage);
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