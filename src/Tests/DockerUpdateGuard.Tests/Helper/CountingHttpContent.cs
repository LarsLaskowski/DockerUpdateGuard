using System.Net;
using System.Text;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// HTTP content that counts its serializations, can refuse a second one and can hide its length
/// </summary>
internal sealed class CountingHttpContent : HttpContent
{
    #region Fields

    private readonly byte[] _bytes;
    private readonly bool _throwOnSecondSerialization;
    private readonly bool _hasKnownLength;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="text">Text that is serialized as UTF-8</param>
    /// <param name="throwOnSecondSerialization">Whether a second serialization throws</param>
    /// <param name="hasKnownLength">Whether the content reports its length up front</param>
    public CountingHttpContent(string text, bool throwOnSecondSerialization, bool hasKnownLength)
    {
        _bytes = Encoding.UTF8.GetBytes(text);
        _throwOnSecondSerialization = throwOnSecondSerialization;
        _hasKnownLength = hasKnownLength;
    }

    #endregion // Constructors

    #region Properties

    /// <summary>
    /// Number of times the content was serialized
    /// </summary>
    public int SerializationCount { get; private set; }

    /// <summary>
    /// Byte length of the serialized content
    /// </summary>
    public int ByteLength => _bytes.Length;

    #endregion // Properties

    #region HttpContent

    /// <inheritdoc/>
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        SerializationCount++;

        if (_throwOnSecondSerialization && SerializationCount > 1)
        {
            throw new InvalidOperationException("The content can be serialized only once");
        }

        await stream.WriteAsync(_bytes).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override bool TryComputeLength(out long length)
    {
        length = _hasKnownLength ? _bytes.Length : 0;

        return _hasKnownLength;
    }

    #endregion // HttpContent
}