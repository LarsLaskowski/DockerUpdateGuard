using System.Net;
using System.Net.Http.Headers;
using System.Text;

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
    /// Send a request through a retry handler that records backoff delays without waiting
    /// </summary>
    /// <param name="retryCount">Retry count</param>
    /// <param name="probe">Inner probe handler</param>
    /// <param name="capturedDelays">Optional list that receives the backoff delays</param>
    /// <param name="request">Optional request; a GET request is used when null</param>
    /// <returns>Resulting response</returns>
    private static async Task<HttpResponseMessage> SendThroughRetryHandlerAsync(int retryCount,
                                                                                RetryProbeHttpMessageHandler probe,
                                                                                List<TimeSpan>? capturedDelays = null,
                                                                                HttpRequestMessage? request = null)
    {
        var handler = new TransientHttpRetryHandler(CreateOptions(retryCount),
                                                    new TestLogger<TransientHttpRetryHandler>(),
                                                    (delay, cancellationToken) =>
                                                    {
                                                        capturedDelays?.Add(delay);

                                                        return Task.CompletedTask;
                                                    })
                      {
                          InnerHandler = probe,
                      };

        using (var invoker = new HttpMessageInvoker(handler))
        {
            var effectiveRequest = request ?? new HttpRequestMessage(HttpMethod.Get, "https://registry.example.test/v2/library/nginx/tags/list");

            try
            {
                return await invoker.SendAsync(effectiveRequest, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                effectiveRequest.Dispose();
            }
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
    /// Verify the request is retried up to the configured retry count before succeeding
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerRetriesUpToConfiguredRetryCountAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([HttpStatusCode.BadGateway, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK],
                                                     HttpStatusCode.OK);

        using var response = await SendThroughRetryHandlerAsync(2, probe).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK,
                        response.StatusCode,
                        "The retry handler must keep retrying transient failures within the retry budget");
        Assert.AreEqual(3,
                        probe.AttemptCount,
                        "The retry handler must perform the initial attempt plus two retries");
    }

    /// <summary>
    /// Verify a Too Many Requests response is treated as transient and retried
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerRetriesTooManyRequestsStatusAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([HttpStatusCode.TooManyRequests, HttpStatusCode.OK],
                                                     HttpStatusCode.OK);

        using var response = await SendThroughRetryHandlerAsync(2, probe).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK,
                        response.StatusCode,
                        "The retry handler must retry a Too Many Requests response");
        Assert.AreEqual(2,
                        probe.AttemptCount,
                        "The retry handler must retry once after a Too Many Requests response");
    }

    /// <summary>
    /// Verify the configured retry count bounds the number of attempts for transient responses
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
    /// Verify a transient exception is rethrown once the retry budget is exhausted
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerRethrowsTransientExceptionWhenRetriesExhaustedAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([null, null], HttpStatusCode.OK);

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => SendThroughRetryHandlerAsync(1, probe),
                                                              "The retry handler must rethrow the transient exception when retries are exhausted")
                    .ConfigureAwait(false);
        Assert.AreEqual(2,
                        probe.AttemptCount,
                        "The retry handler must perform the initial attempt plus the configured number of retries before failing");
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

    /// <summary>
    /// Verify a negative retry count is clamped to zero
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerWithNegativeRetryCountDoesNotRetryAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([], HttpStatusCode.ServiceUnavailable);

        using var response = await SendThroughRetryHandlerAsync(-5, probe).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable,
                        response.StatusCode,
                        "The retry handler must surface the transient response when the retry count is negative");
        Assert.AreEqual(1,
                        probe.AttemptCount,
                        "The retry handler must clamp a negative retry count to a single attempt");
    }

    /// <summary>
    /// Verify the backoff between retries grows exponentially
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerUsesExponentialBackoffBetweenRetriesAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([], HttpStatusCode.ServiceUnavailable);
        var capturedDelays = new List<TimeSpan>();

        using var response = await SendThroughRetryHandlerAsync(2, probe, capturedDelays).ConfigureAwait(false);

        CollectionAssert.AreEqual(new[] { TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1000) },
                                  capturedDelays,
                                  "The retry handler must double the backoff delay before each retry");
    }

    /// <summary>
    /// Verify the backoff delay is capped at the configured maximum
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerCapsBackoffAtMaximumAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([], HttpStatusCode.ServiceUnavailable);
        var capturedDelays = new List<TimeSpan>();

        using var response = await SendThroughRetryHandlerAsync(7, probe, capturedDelays).ConfigureAwait(false);

        Assert.HasCount(7,
                        capturedDelays,
                        "The retry handler must compute one delay per retry attempt");
        Assert.AreEqual(TimeSpan.FromMilliseconds(30000),
                        capturedDelays[^1],
                        "The retry handler must cap the exponential backoff at the configured maximum");
    }

    /// <summary>
    /// Verify a Retry-After delta header overrides the exponential backoff
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerHonorsRetryAfterDeltaHeaderAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK], HttpStatusCode.OK)
                    {
                        RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7)),
                    };
        var capturedDelays = new List<TimeSpan>();

        using var response = await SendThroughRetryHandlerAsync(1, probe, capturedDelays).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK,
                        response.StatusCode,
                        "The retry handler must succeed after honoring the Retry-After header");
        Assert.HasCount(1,
                        capturedDelays,
                        "The retry handler must apply a single Retry-After delay");
        Assert.AreEqual(TimeSpan.FromSeconds(7),
                        capturedDelays[0],
                        "The retry handler must use the Retry-After delta value as the backoff delay");
    }

    /// <summary>
    /// Verify a Retry-After date header is honored as an absolute backoff target
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerHonorsRetryAfterDateHeaderAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK], HttpStatusCode.OK)
                    {
                        RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(30)),
                    };
        var capturedDelays = new List<TimeSpan>();

        using var response = await SendThroughRetryHandlerAsync(1, probe, capturedDelays).ConfigureAwait(false);

        Assert.HasCount(1,
                        capturedDelays,
                        "The retry handler must apply a single Retry-After delay");
        Assert.IsGreaterThan(TimeSpan.FromSeconds(27),
                             capturedDelays[0],
                             "The retry handler must wait close to the Retry-After date before retrying");
        Assert.IsLessThanOrEqualTo(TimeSpan.FromSeconds(30),
                                   capturedDelays[0],
                                   "The retry handler must not wait longer than the Retry-After date");
    }

    /// <summary>
    /// Verify a Retry-After date in the past falls back to exponential backoff
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerWithPastRetryAfterDateUsesExponentialBackoffAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK], HttpStatusCode.OK)
                    {
                        RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(-30)),
                    };
        var capturedDelays = new List<TimeSpan>();

        using var response = await SendThroughRetryHandlerAsync(1, probe, capturedDelays).ConfigureAwait(false);

        CollectionAssert.AreEqual(new[] { TimeSpan.FromMilliseconds(500) },
                                  capturedDelays,
                                  "The retry handler must fall back to exponential backoff for a past Retry-After date");
    }

    /// <summary>
    /// Verify a request carrying content is buffered and retried successfully
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerBuffersAndRetriesRequestWithContentAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK], HttpStatusCode.OK);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://registry.example.test/api/auth")
                            {
                                Content = new StringContent("{\"username\":\"probe\"}", Encoding.UTF8, "application/json"),
                            };

        using var response = await SendThroughRetryHandlerAsync(2, probe, request: request).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK,
                        response.StatusCode,
                        "The retry handler must retry a request with buffered content");
        Assert.AreEqual(2,
                        probe.AttemptCount,
                        "The retry handler must resend the buffered request content on retry");
    }

    #endregion // Methods
}