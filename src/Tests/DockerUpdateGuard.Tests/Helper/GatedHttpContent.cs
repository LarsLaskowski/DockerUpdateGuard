using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// JSON HTTP content whose serialization waits for a gate, so the buffered read of a response body stays pending until the test opens it
/// </summary>
internal sealed class GatedHttpContent : HttpContent
{
    #region Fields

    /// <summary>
    /// Task that completes when the content may be read
    /// </summary>
    private readonly Task _gate;

    /// <summary>
    /// UTF-8 bytes of the JSON content
    /// </summary>
    private readonly byte[] _bytes;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="gate">Task that completes when the content may be read</param>
    /// <param name="json">JSON text that is serialized as UTF-8</param>
    public GatedHttpContent(Task gate, string json)
    {
        _gate = gate;
        _bytes = Encoding.UTF8.GetBytes(json);
        Headers.ContentType = new MediaTypeHeaderValue("application/json");
    }

    #endregion // Constructors

    #region HttpContent

    /// <inheritdoc/>
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        await _gate.ConfigureAwait(false);
        await stream.WriteAsync(_bytes).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override bool TryComputeLength(out long length)
    {
        length = _bytes.Length;

        return true;
    }

    #endregion // HttpContent
}