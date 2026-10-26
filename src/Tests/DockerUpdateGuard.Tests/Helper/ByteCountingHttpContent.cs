using System.Net;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// HTTP content of repeated bytes whose read stream counts the bytes handed out
/// </summary>
internal sealed class ByteCountingHttpContent : HttpContent
{
    #region Fields

    private readonly byte _value;
    private readonly int _length;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="value">Byte the content repeats</param>
    /// <param name="length">Number of bytes of the content</param>
    public ByteCountingHttpContent(byte value, int length)
    {
        _value = value;
        _length = length;
    }

    #endregion // Constructors

    #region Properties

    /// <summary>
    /// Number of bytes read from the content stream so far
    /// </summary>
    public long BytesRead { get; private set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Count bytes handed out by the read stream
    /// </summary>
    /// <param name="count">Number of bytes read</param>
    private void AddBytesRead(int count)
    {
        BytesRead += count;
    }

    #endregion // Methods

    #region HttpContent

    /// <inheritdoc/>
    protected override Task<Stream> CreateContentReadStreamAsync()
    {
        return Task.FromResult<Stream>(new CountingReadStream(_value, _length, AddBytesRead));
    }

    /// <inheritdoc/>
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var buffer = new byte[_length];

        Array.Fill(buffer, _value);

        await stream.WriteAsync(buffer).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override bool TryComputeLength(out long length)
    {
        length = _length;

        return true;
    }

    #endregion // HttpContent
}