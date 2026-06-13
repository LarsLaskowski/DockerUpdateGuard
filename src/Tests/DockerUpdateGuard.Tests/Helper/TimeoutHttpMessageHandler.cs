namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// HTTP-message handler that waits for cancellation to simulate a request timeout
/// </summary>
internal sealed class TimeoutHttpMessageHandler : HttpMessageHandler
{
    #region HttpMessageHandler

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);

        throw new InvalidOperationException("The timeout handler must be canceled before returning a response");
    }

    #endregion // HttpMessageHandler
}