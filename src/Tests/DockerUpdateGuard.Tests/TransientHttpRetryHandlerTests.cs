using System.Net;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="TransientHttpRetryHandler"/>
/// </summary>
[TestClass]
public partial class TransientHttpRetryHandlerTests
{
    #region Static methods

    /// <summary>
    /// Create an options monitor whose scanning section uses the supplied retry count
    /// </summary>
    /// <param name="retryCount">Retry count</param>
    /// <returns>Options monitor</returns>
    private static TestOptionsMonitor<DockerUpdateGuardOptions> CreateOptions(int retryCount)
    {
        var options = new DockerUpdateGuardOptions
                      {
                          Scanning = new ScanningOptions
                                     {
                                         RetryCount = retryCount,
                                     },
                      };

        return new TestOptionsMonitor<DockerUpdateGuardOptions>(options);
    }

    /// <summary>
    /// Send a single GET request through a retry handler wrapping the supplied probe
    /// </summary>
    /// <param name="retryCount">Retry count</param>
    /// <param name="probe">Inner probe handler</param>
    /// <returns>Resulting response</returns>
    private static async Task<HttpResponseMessage> SendThroughRetryHandlerAsync(int retryCount, RetryProbeHttpMessageHandler probe)
    {
        var handler = new TransientHttpRetryHandler(CreateOptions(retryCount),
                                                    new TestLogger<TransientHttpRetryHandler>())
                      {
                          InnerHandler = probe,
                      };

        using (var invoker = new HttpMessageInvoker(handler))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://registry.example.test/v2/library/nginx/tags/list");

            return await invoker.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Verify a transient status code is retried until it succeeds
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerRetriesTransientStatusCodeUntilSuccessAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK],
                                                     HttpStatusCode.OK);

        using var response = await SendThroughRetryHandlerAsync(2, probe).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK,
                        response.StatusCode,
                        "The retry handler must return the successful response after a transient status code");
        Assert.AreEqual(2,
                        probe.AttemptCount,
                        "The retry handler must retry the request exactly once before succeeding");
    }

    /// <summary>
    /// Verify a transient transport exception is retried until it succeeds
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerRetriesTransientExceptionUntilSuccessAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([null, HttpStatusCode.OK], HttpStatusCode.OK);

        using var response = await SendThroughRetryHandlerAsync(2, probe).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK,
                        response.StatusCode,
                        "The retry handler must return the successful response after a transient transport failure");
        Assert.AreEqual(2,
                        probe.AttemptCount,
                        "The retry handler must retry the request exactly once after a transient transport failure");
    }

    /// <summary>
    /// Verify the configured retry count bounds the number of attempts
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerReturnsTransientResponseWhenRetriesExhaustedAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([], HttpStatusCode.ServiceUnavailable);

        using var response = await SendThroughRetryHandlerAsync(1, probe).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable,
                        response.StatusCode,
                        "The retry handler must surface the transient response once the retry budget is exhausted");
        Assert.AreEqual(2,
                        probe.AttemptCount,
                        "The retry handler must perform the initial attempt plus the configured number of retries");
    }

    /// <summary>
    /// Verify a non-transient status code is not retried
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerDoesNotRetryNonTransientStatusAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([], HttpStatusCode.NotFound);

        using var response = await SendThroughRetryHandlerAsync(3, probe).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.NotFound,
                        response.StatusCode,
                        "The retry handler must surface a non-transient response unchanged");
        Assert.AreEqual(1,
                        probe.AttemptCount,
                        "The retry handler must not retry a non-transient response");
    }

    /// <summary>
    /// Verify a retry count of zero disables retries
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerWithZeroRetryCountDoesNotRetryAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([], HttpStatusCode.ServiceUnavailable);

        using var response = await SendThroughRetryHandlerAsync(0, probe).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable,
                        response.StatusCode,
                        "The retry handler must surface the transient response when retries are disabled");
        Assert.AreEqual(1,
                        probe.AttemptCount,
                        "The retry handler must perform only the initial attempt when the retry count is zero");
    }

    #endregion // Methods
}
