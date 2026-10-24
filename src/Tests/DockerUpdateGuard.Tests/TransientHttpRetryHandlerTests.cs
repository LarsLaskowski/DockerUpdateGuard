using System.Net;
using System.Net.Http.Headers;
using System.Text;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

using Microsoft.Extensions.Logging;

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
    /// Create a POST request with a small body
    /// </summary>
    /// <returns>POST request</returns>
    private static HttpRequestMessage CreatePostRequest()
    {
        return new HttpRequestMessage(HttpMethod.Post, "https://registry.example.test/api/auth")
               {
                   Content = new StringContent("{\"username\":\"probe\"}", Encoding.UTF8, "application/json"),
               };
    }

    /// <summary>
    /// Send a request through a retry handler that records backoff delays without waiting
    /// </summary>
    /// <param name="retryCount">Retry count</param>
    /// <param name="probe">Inner probe handler</param>
    /// <param name="capturedDelays">Optional list that receives the backoff delays</param>
    /// <param name="request">Optional request; a GET request is used when null</param>
    /// <param name="nextJitterMilliseconds">Optional jitter provider; no jitter is applied when null</param>
    /// <param name="logger">Optional logger that captures the handler's log entries; a new logger is used when null</param>
    /// <returns>Resulting response</returns>
    private static async Task<HttpResponseMessage> SendThroughRetryHandlerAsync(int retryCount,
                                                                                RetryProbeHttpMessageHandler probe,
                                                                                List<TimeSpan>? capturedDelays = null,
                                                                                HttpRequestMessage? request = null,
                                                                                Func<int, int>? nextJitterMilliseconds = null,
                                                                                TestLogger<TransientHttpRetryHandler>? logger = null)
    {
        var handler = new TransientHttpRetryHandler(CreateOptions(retryCount),
                                                    logger ?? new TestLogger<TransientHttpRetryHandler>(),
                                                    (delay, cancellationToken) =>
                                                    {
                                                        capturedDelays?.Add(delay);

                                                        return Task.CompletedTask;
                                                    },
                                                    nextJitterMilliseconds ?? (_ => 0))
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

        Assert.AreSequenceEqual([TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1000)], capturedDelays, "The retry handler must double the backoff delay before each retry");
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

        Assert.AreSequenceEqual([TimeSpan.FromMilliseconds(500)], capturedDelays, "The retry handler must fall back to exponential backoff for a past Retry-After date");
    }

    /// <summary>
    /// Verify a Retry-After value longer than the maximum backoff is clamped
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerClampsRetryAfterToMaximumBackoffAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK], HttpStatusCode.OK)
                    {
                        RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1)),
                    };
        var capturedDelays = new List<TimeSpan>();

        using var response = await SendThroughRetryHandlerAsync(1, probe, capturedDelays).ConfigureAwait(false);

        Assert.HasCount(1,
                        capturedDelays,
                        "The retry handler must apply a single Retry-After delay");
        Assert.AreEqual(TimeSpan.FromMilliseconds(30000),
                        capturedDelays[0],
                        "The retry handler must clamp an excessive Retry-After value to the maximum backoff");
    }

    /// <summary>
    /// Verify random jitter is added on top of the exponential backoff
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerAppliesJitterToExponentialBackoffAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([], HttpStatusCode.ServiceUnavailable);
        var capturedDelays = new List<TimeSpan>();

        using var response = await SendThroughRetryHandlerAsync(1, probe, capturedDelays, nextJitterMilliseconds: _ => 50).ConfigureAwait(false);

        Assert.AreSequenceEqual([TimeSpan.FromMilliseconds(550)], capturedDelays, "The retry handler must add the jitter value on top of the exponential backoff");
    }

    /// <summary>
    /// Verify a request carrying content is buffered once and resent with the identical body after a pre-send failure
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerBuffersAndRetriesRequestWithContentAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([null, HttpStatusCode.OK], HttpStatusCode.OK)
                    {
                        FailureFactory = () => new HttpRequestException(HttpRequestError.ConnectionError, "refused"),
                    };
        using var content = new CountingHttpContent("{\"username\":\"probe\"}", true, true);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://registry.example.test/api/auth")
                            {
                                Content = content,
                            };

        using var response = await SendThroughRetryHandlerAsync(2, probe, request: request).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK,
                        response.StatusCode,
                        "The retry handler must retry a request with buffered content after a connection error");
        Assert.AreEqual(2,
                        probe.AttemptCount,
                        "The retry handler must resend the buffered request content on retry");
        Assert.AreSequenceEqual(["{\"username\":\"probe\"}", "{\"username\":\"probe\"}"],
                                probe.RequestBodies,
                                "Both attempts must carry the identical body");
        Assert.AreEqual(1,
                        content.SerializationCount,
                        "The body must be serialized exactly once because the handler buffers it");
    }

    /// <summary>
    /// Verify a POST answered with a server error status is sent once and the response is returned
    /// </summary>
    /// <param name="statusCode">Server error status code</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow(HttpStatusCode.InternalServerError)]
    [DataRow(HttpStatusCode.BadGateway)]
    [DataRow(HttpStatusCode.ServiceUnavailable)]
    [DataRow(HttpStatusCode.GatewayTimeout)]
    [DataRow(HttpStatusCode.RequestTimeout)]
    public async Task TransientHttpRetryHandlerDoesNotRetryPostOnServerErrorAsync(HttpStatusCode statusCode)
    {
        var probe = new RetryProbeHttpMessageHandler([statusCode, HttpStatusCode.OK], HttpStatusCode.OK);
        var logger = new TestLogger<TransientHttpRetryHandler>();
        using var request = CreatePostRequest();

        using var response = await SendThroughRetryHandlerAsync(2, probe, request: request, logger: logger).ConfigureAwait(false);

        Assert.AreEqual(statusCode,
                        response.StatusCode,
                        "The server error response of a POST must be returned unchanged");
        Assert.AreEqual(1,
                        probe.AttemptCount,
                        "A POST must be sent exactly once because the server may already have processed it");
        Assert.DoesNotContain(entry => entry.EventId.Id == 3500,
                              logger.Entries,
                              "No retry warning may be logged for a POST that is not retried");
    }

    /// <summary>
    /// Verify a POST is retried after a failure that proves the request never reached the server
    /// </summary>
    /// <param name="error">Transport error raised before any request byte was sent</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow(HttpRequestError.ConnectionError)]
    [DataRow(HttpRequestError.NameResolutionError)]
    [DataRow(HttpRequestError.SecureConnectionError)]
    [DataRow(HttpRequestError.ProxyTunnelError)]
    public async Task TransientHttpRetryHandlerRetriesPostOnConnectionErrorAsync(HttpRequestError error)
    {
        var probe = new RetryProbeHttpMessageHandler([null, null, HttpStatusCode.OK], HttpStatusCode.OK)
                    {
                        FailureFactory = () => new HttpRequestException(error, "not connected"),
                    };
        using var request = CreatePostRequest();

        using var response = await SendThroughRetryHandlerAsync(2, probe, request: request).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK,
                        response.StatusCode,
                        "A POST must be retried until it succeeds after errors raised before sending");
        Assert.AreEqual(3,
                        probe.AttemptCount,
                        "A POST must use the initial attempt plus both retries after pre-send errors");
    }

    /// <summary>
    /// Verify the last pre-send failure of a POST is rethrown once the retry budget is exhausted
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerRethrowsPostConnectionErrorWhenRetriesExhaustedAsync()
    {
        var failureCount = 0;
        var probe = new RetryProbeHttpMessageHandler([null, null], HttpStatusCode.OK)
                    {
                        FailureFactory = () => new HttpRequestException(HttpRequestError.ConnectionError, $"failure {++failureCount}"),
                    };
        using var request = CreatePostRequest();

        var exception = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => SendThroughRetryHandlerAsync(1, probe, request: request),
                                                                              "The retry handler must rethrow the pre-send failure when retries are exhausted")
                                    .ConfigureAwait(false);

        Assert.AreEqual("failure 2",
                        exception.Message,
                        "The exception of the last attempt must reach the caller");
        Assert.AreEqual(2,
                        probe.AttemptCount,
                        "A POST must use the initial attempt plus the configured retries");
    }

    /// <summary>
    /// Verify a POST is not retried after a failure that may have happened after the server read the request
    /// </summary>
    /// <param name="error">Transport error that does not prove the request was unprocessed</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow(HttpRequestError.ResponseEnded)]
    [DataRow(HttpRequestError.InvalidResponse)]
    [DataRow(HttpRequestError.HttpProtocolError)]
    [DataRow(HttpRequestError.Unknown)]
    public async Task TransientHttpRetryHandlerDoesNotRetryPostOnResponseEndedAsync(HttpRequestError error)
    {
        var probe = new RetryProbeHttpMessageHandler([null, HttpStatusCode.OK], HttpStatusCode.OK)
                    {
                        FailureFactory = () => new HttpRequestException(error, "ended"),
                    };
        using var request = CreatePostRequest();

        var exception = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => SendThroughRetryHandlerAsync(2, probe, request: request),
                                                                              "The failure of a POST that may have been processed must reach the caller")
                                    .ConfigureAwait(false);

        Assert.AreEqual(error,
                        exception.HttpRequestError,
                        "The original exception must reach the caller unchanged");
        Assert.AreEqual(1,
                        probe.AttemptCount,
                        "A POST must be sent exactly once after an error that may follow processing");
    }

    /// <summary>
    /// Verify a POST failing with a plain HTTP request exception without error is not retried
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerDoesNotRetryPostOnPlainHttpRequestExceptionAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([null, HttpStatusCode.OK], HttpStatusCode.OK);
        using var request = CreatePostRequest();

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => SendThroughRetryHandlerAsync(2, probe, request: request),
                                                              "A plain transport failure of a POST must reach the caller")
                    .ConfigureAwait(false);
        Assert.AreEqual(1,
                        probe.AttemptCount,
                        "A POST must be sent exactly once after a failure without a pre-send error");
    }

    /// <summary>
    /// Verify a POST failing with a timeout exception is not retried
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerDoesNotRetryPostOnTimeoutAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([null, HttpStatusCode.OK], HttpStatusCode.OK)
                    {
                        FailureFactory = () => new TimeoutException("timed out"),
                    };
        using var request = CreatePostRequest();

        await Assert.ThrowsExactlyAsync<TimeoutException>(() => SendThroughRetryHandlerAsync(2, probe, request: request),
                                                          "A timeout of a POST must reach the caller")
                    .ConfigureAwait(false);
        Assert.AreEqual(1,
                        probe.AttemptCount,
                        "A POST must be sent exactly once after a timeout");
    }

    /// <summary>
    /// Verify a POST answered with Too Many Requests is retried with the Retry-After or the exponential backoff delay
    /// </summary>
    /// <param name="retryAfterSeconds">Retry-After delta in seconds; null for none</param>
    /// <param name="expectedDelayMilliseconds">Expected backoff delay in milliseconds</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow(7, 7000)]
    [DataRow(30, 30000)]
    [DataRow(null, 500)]
    public async Task TransientHttpRetryHandlerRetriesPostOnTooManyRequestsAsync(int? retryAfterSeconds, int expectedDelayMilliseconds)
    {
        var probe = new RetryProbeHttpMessageHandler([HttpStatusCode.TooManyRequests, HttpStatusCode.OK], HttpStatusCode.OK)
                    {
                        RetryAfter = retryAfterSeconds is null ? null : new RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterSeconds.Value)),
                    };
        var capturedDelays = new List<TimeSpan>();
        using var request = CreatePostRequest();

        using var response = await SendThroughRetryHandlerAsync(2, probe, capturedDelays, request).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK,
                        response.StatusCode,
                        "A POST answered with 429 was not processed and must be retried");
        Assert.AreEqual(2,
                        probe.AttemptCount,
                        "A POST must be retried once after a 429 response");
        Assert.AreSequenceEqual([TimeSpan.FromMilliseconds(expectedDelayMilliseconds)],
                                capturedDelays,
                                "The wait before the retry must be the Retry-After value or the exponential backoff");
    }

    /// <summary>
    /// Verify a non-idempotent request answered with Too Many Requests and a Retry-After delta above the maximum wait is sent once
    /// </summary>
    /// <param name="method">HTTP method name</param>
    /// <param name="retryAfterSeconds">Retry-After delta in seconds</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("POST", 31)]
    [DataRow("POST", 3600)]
    [DataRow("PATCH", 3600)]
    public async Task TransientHttpRetryHandlerDoesNotRetryNonIdempotentRequestWhenRetryAfterExceedsMaximumAsync(string method, int retryAfterSeconds)
    {
        var probe = new RetryProbeHttpMessageHandler([HttpStatusCode.TooManyRequests, HttpStatusCode.OK], HttpStatusCode.OK)
                    {
                        RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterSeconds)),
                    };
        var capturedDelays = new List<TimeSpan>();
        using var request = new HttpRequestMessage(new HttpMethod(method), "https://registry.example.test/api/auth");

        using var response = await SendThroughRetryHandlerAsync(2, probe, capturedDelays, request).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.TooManyRequests,
                        response.StatusCode,
                        "The 429 response must be returned to the caller");
        Assert.AreEqual(1,
                        probe.AttemptCount,
                        "A non-idempotent request must not be resent inside the window the server asked for");
        Assert.IsEmpty(capturedDelays,
                       "The handler must not wait before returning the 429 response");
    }

    /// <summary>
    /// Verify a POST answered with Too Many Requests and a Retry-After date beyond the maximum wait is sent once
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerDoesNotRetryPostWhenRetryAfterDateExceedsMaximumAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([HttpStatusCode.TooManyRequests, HttpStatusCode.OK], HttpStatusCode.OK)
                    {
                        RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddHours(1)),
                    };
        var capturedDelays = new List<TimeSpan>();
        using var request = CreatePostRequest();

        using var response = await SendThroughRetryHandlerAsync(2, probe, capturedDelays, request).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.TooManyRequests,
                        response.StatusCode,
                        "The 429 response must be returned to the caller");
        Assert.AreEqual(1,
                        probe.AttemptCount,
                        "A POST must not be resent before the Retry-After date");
        Assert.IsEmpty(capturedDelays,
                       "The handler must not wait before returning the 429 response");
    }

    /// <summary>
    /// Verify PATCH and an unknown method are sent once after a server error
    /// </summary>
    /// <param name="method">HTTP method name</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("PATCH")]
    [DataRow("LOCK")]
    public async Task TransientHttpRetryHandlerTreatsNonIdempotentMethodLikePostOnServerErrorAsync(string method)
    {
        var probe = new RetryProbeHttpMessageHandler([HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK], HttpStatusCode.OK);
        using var request = new HttpRequestMessage(new HttpMethod(method), "https://registry.example.test/api/resource");

        using var response = await SendThroughRetryHandlerAsync(2, probe, request: request).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable,
                        response.StatusCode,
                        "The 503 response of a non-idempotent method must be returned");
        Assert.AreEqual(1,
                        probe.AttemptCount,
                        "A non-idempotent method must be sent exactly once after a 503 response");
    }

    /// <summary>
    /// Verify PATCH and an unknown method are sent once after a response-ended failure
    /// </summary>
    /// <param name="method">HTTP method name</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("PATCH")]
    [DataRow("LOCK")]
    public async Task TransientHttpRetryHandlerTreatsNonIdempotentMethodLikePostOnResponseEndedAsync(string method)
    {
        var probe = new RetryProbeHttpMessageHandler([null, HttpStatusCode.OK], HttpStatusCode.OK)
                    {
                        FailureFactory = () => new HttpRequestException(HttpRequestError.ResponseEnded, "ended"),
                    };
        using var request = new HttpRequestMessage(new HttpMethod(method), "https://registry.example.test/api/resource");

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => SendThroughRetryHandlerAsync(2, probe, request: request),
                                                              "The failure of a non-idempotent method must reach the caller")
                    .ConfigureAwait(false);
        Assert.AreEqual(1,
                        probe.AttemptCount,
                        "A non-idempotent method must be sent exactly once after a response-ended failure");
    }

    /// <summary>
    /// Verify PATCH and an unknown method are retried after a connection error
    /// </summary>
    /// <param name="method">HTTP method name</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("PATCH")]
    [DataRow("LOCK")]
    public async Task TransientHttpRetryHandlerTreatsNonIdempotentMethodLikePostOnConnectionErrorAsync(string method)
    {
        var probe = new RetryProbeHttpMessageHandler([null, HttpStatusCode.OK], HttpStatusCode.OK)
                    {
                        FailureFactory = () => new HttpRequestException(HttpRequestError.ConnectionError, "refused"),
                    };
        using var request = new HttpRequestMessage(new HttpMethod(method), "https://registry.example.test/api/resource");

        using var response = await SendThroughRetryHandlerAsync(2, probe, request: request).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK,
                        response.StatusCode,
                        "A non-idempotent method must be retried after an error raised before sending");
        Assert.AreEqual(2,
                        probe.AttemptCount,
                        "A non-idempotent method must be retried once after a connection error");
    }

    /// <summary>
    /// Verify idempotent methods are retried on a server error and on a response-ended failure
    /// </summary>
    /// <param name="method">HTTP method name</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("GET")]
    [DataRow("HEAD")]
    [DataRow("OPTIONS")]
    [DataRow("TRACE")]
    [DataRow("PUT")]
    [DataRow("DELETE")]
    public async Task TransientHttpRetryHandlerRetriesIdempotentMethodOnServerErrorAndResponseEndedAsync(string method)
    {
        var probe = new RetryProbeHttpMessageHandler([HttpStatusCode.ServiceUnavailable, null, HttpStatusCode.OK], HttpStatusCode.OK)
                    {
                        FailureFactory = () => new HttpRequestException(HttpRequestError.ResponseEnded, "ended"),
                    };
        using var request = new HttpRequestMessage(new HttpMethod(method), "https://registry.example.test/api/resource");

        using var response = await SendThroughRetryHandlerAsync(2, probe, request: request).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK,
                        response.StatusCode,
                        "An idempotent method must be retried until it succeeds");
        Assert.AreEqual(3,
                        probe.AttemptCount,
                        "An idempotent method must be retried after a 503 and after a response-ended failure");
    }

    /// <summary>
    /// Verify a body of unknown length is buffered so that it is sent with a Content-Length
    /// </summary>
    /// <param name="retryCount">Retry count</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    public async Task TransientHttpRetryHandlerBuffersContentOfUnknownLengthAsync(int retryCount)
    {
        var probe = new RetryProbeHttpMessageHandler([], HttpStatusCode.OK);
        using var content = new CountingHttpContent("{\"username\":\"probe\"}", false, false);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://registry.example.test/api/auth")
                            {
                                Content = content,
                            };

        using var response = await SendThroughRetryHandlerAsync(retryCount, probe, request: request).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK,
                        response.StatusCode,
                        "The request must reach the inner handler");
        Assert.AreSequenceEqual([(long?)content.ByteLength],
                                probe.ContentLengths,
                                "A body of unknown length must reach the inner handler with its byte length as Content-Length");
    }

    /// <summary>
    /// Verify a POST left unretried after a server error logs a debug entry with the masked request target
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerLogsDebugWhenPostIsNotRetriedOnServerErrorAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK], HttpStatusCode.OK);
        var logger = new TestLogger<TransientHttpRetryHandler>();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://user:secret@registry.example.test/path?token=x");

        using var response = await SendThroughRetryHandlerAsync(2, probe, request: request, logger: logger).ConfigureAwait(false);

        var entry = logger.Entries.Single(candidate => candidate.EventId.Id == 3502);

        Assert.AreEqual(LogLevel.Debug,
                        entry.LogLevel,
                        "The not-retried entry must be logged at Debug");
        Assert.AreEqual("Transient HTTP outcome 503 for POST https://***@registry.example.test/path not retried: the method is not idempotent",
                        entry.Message,
                        "The entry must name the outcome, the method and the masked target");
        Assert.DoesNotContain("secret", entry.Message, "The entry must not contain the password");
        Assert.DoesNotContain("token=x", entry.Message, "The entry must not contain the query string");
        Assert.DoesNotContain(candidate => candidate.EventId.Id == 3500,
                              logger.Entries,
                              "No retry warning may be logged for a request that is not retried");
    }

    /// <summary>
    /// Verify a POST left unretried after a response-ended failure logs a debug entry naming the error
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerLogsDebugWhenPostIsNotRetriedOnResponseEndedAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([null, HttpStatusCode.OK], HttpStatusCode.OK)
                    {
                        FailureFactory = () => new HttpRequestException(HttpRequestError.ResponseEnded, "secret detail"),
                    };
        var logger = new TestLogger<TransientHttpRetryHandler>();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://user:secret@registry.example.test/path?token=x");

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => SendThroughRetryHandlerAsync(2, probe, request: request, logger: logger),
                                                              "The failure must reach the caller")
                    .ConfigureAwait(false);

        var entry = logger.Entries.Single(candidate => candidate.EventId.Id == 3502);

        Assert.AreEqual(LogLevel.Debug,
                        entry.LogLevel,
                        "The not-retried entry must be logged at Debug");
        Assert.AreEqual("Transient HTTP outcome ResponseEnded for POST https://***@registry.example.test/path not retried: the method is not idempotent",
                        entry.Message,
                        "The entry must name the error, the method and the masked target, not the exception message");
        Assert.DoesNotContain(candidate => candidate.EventId.Id == 3501,
                              logger.Entries,
                              "No retry warning may be logged for a request that is not retried");
    }

    /// <summary>
    /// Verify no not-retried entry is logged when a POST gets a server error after its retries are used up
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerDoesNotLogNotRetriedWhenPostRetriesAreUsedUpAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable], HttpStatusCode.OK);
        var logger = new TestLogger<TransientHttpRetryHandler>();
        using var request = CreatePostRequest();

        using var response = await SendThroughRetryHandlerAsync(2, probe, request: request, logger: logger).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable,
                        response.StatusCode,
                        "The last response must be returned once the retries are used up");
        Assert.AreEqual(3,
                        probe.AttemptCount,
                        "A POST must use the initial attempt plus both retries after 429 responses");
        Assert.DoesNotContain(entry => entry.EventId.Id == 3502,
                              logger.Entries,
                              "An outcome that could not be retried anyway must not log a not-retried entry");
    }

    /// <summary>
    /// Verify no not-retried entry is logged when a POST fails after its retries are used up
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerDoesNotLogNotRetriedWhenPostFailsAfterRetriesAreUsedUpAsync()
    {
        var failureCount = 0;
        var probe = new RetryProbeHttpMessageHandler([null, null], HttpStatusCode.OK)
                    {
                        FailureFactory = () => new HttpRequestException(++failureCount == 1 ? HttpRequestError.ConnectionError : HttpRequestError.ResponseEnded, "failure"),
                    };
        var logger = new TestLogger<TransientHttpRetryHandler>();
        using var request = CreatePostRequest();

        var exception = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => SendThroughRetryHandlerAsync(1, probe, request: request, logger: logger),
                                                                              "The failure of the last attempt must reach the caller")
                                    .ConfigureAwait(false);

        Assert.AreEqual(HttpRequestError.ResponseEnded,
                        exception.HttpRequestError,
                        "The exception of the last attempt must reach the caller");
        Assert.AreEqual(2,
                        probe.AttemptCount,
                        "A POST must use the initial attempt plus one retry");
        Assert.DoesNotContain(entry => entry.EventId.Id == 3502,
                              logger.Entries,
                              "A failure that could not be retried anyway must not log a not-retried entry");
    }

    /// <summary>
    /// Verify no not-retried entry is logged when retries are disabled
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerWithZeroRetryCountDoesNotLogNotRetriedAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([], HttpStatusCode.ServiceUnavailable);
        var logger = new TestLogger<TransientHttpRetryHandler>();
        using var request = CreatePostRequest();

        using var response = await SendThroughRetryHandlerAsync(0, probe, request: request, logger: logger).ConfigureAwait(false);

        Assert.DoesNotContain(entry => entry.EventId.Id == 3502,
                              logger.Entries,
                              "A request that could not be retried anyway must not log a not-retried entry");
    }

    /// <summary>
    /// Verify a POST left unretried because of a long Retry-After logs a debug entry with the masked request target
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerLogsDebugWhenPostIsNotRetriedOnLongRetryAfterAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([HttpStatusCode.TooManyRequests, HttpStatusCode.OK], HttpStatusCode.OK)
                    {
                        RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(3600)),
                    };
        var logger = new TestLogger<TransientHttpRetryHandler>();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://user:secret@registry.example.test/path?token=x");

        using var response = await SendThroughRetryHandlerAsync(2, probe, request: request, logger: logger).ConfigureAwait(false);

        var entry = logger.Entries.Single(candidate => candidate.EventId.Id == 3503);

        Assert.AreEqual(LogLevel.Debug,
                        entry.LogLevel,
                        "The long Retry-After entry must be logged at Debug");
        Assert.AreEqual("Transient HTTP response 429 for POST https://***@registry.example.test/path not retried: Retry-After of 3600000 ms exceeds the maximum wait of 30000 ms for a method that is not idempotent",
                        entry.Message,
                        "The entry must name the status, the method, the masked target, the requested wait and the maximum");
        Assert.DoesNotContain("secret", entry.Message, "The entry must not contain the password");
        Assert.DoesNotContain("token=x", entry.Message, "The entry must not contain the query string");
        Assert.DoesNotContain(candidate => candidate.EventId.Id == 3500,
                              logger.Entries,
                              "No retry warning may be logged for a request that is not retried");
    }

    /// <summary>
    /// Verify a GET answered with Too Many Requests and a Retry-After above the maximum wait is retried after the clamped wait
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerClampsRetryAfterOfTooManyRequestsForGetAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([HttpStatusCode.TooManyRequests, HttpStatusCode.OK], HttpStatusCode.OK)
                    {
                        RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1)),
                    };
        var capturedDelays = new List<TimeSpan>();

        using var response = await SendThroughRetryHandlerAsync(2, probe, capturedDelays).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK,
                        response.StatusCode,
                        "An idempotent request must be retried after a 429 with a long Retry-After");
        Assert.AreEqual(2,
                        probe.AttemptCount,
                        "An idempotent request must be retried once");
        Assert.AreSequenceEqual([TimeSpan.FromMilliseconds(30000)],
                                capturedDelays,
                                "The wait must be clamped to the maximum backoff");
    }

    /// <summary>
    /// Verify no long Retry-After entry is logged when retries are disabled
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerWithZeroRetryCountDoesNotLogLongRetryAfterAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([], HttpStatusCode.TooManyRequests)
                    {
                        RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(3600)),
                    };
        var logger = new TestLogger<TransientHttpRetryHandler>();
        using var request = CreatePostRequest();

        using var response = await SendThroughRetryHandlerAsync(0, probe, request: request, logger: logger).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.TooManyRequests,
                        response.StatusCode,
                        "The 429 response must be returned");
        Assert.AreEqual(1,
                        probe.AttemptCount,
                        "The request must be sent once");
        Assert.DoesNotContain(entry => entry.EventId.Id == 3503,
                              logger.Entries,
                              "A request with no retries left must not log a long Retry-After entry");
    }

    /// <summary>
    /// Verify the transient status retry log names the request without user info and query string
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerTransientStatusLogOmitsUserInfoAndQueryAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK], HttpStatusCode.OK);
        var logger = new TestLogger<TransientHttpRetryHandler>();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://user:secret@registry.example.test/v2/path?token=x");

        using var response = await SendThroughRetryHandlerAsync(2, probe, request: request, logger: logger).ConfigureAwait(false);

        var entry = logger.Entries.Single(candidate => candidate.EventId.Id == 3500);

        Assert.Contains("https://***@registry.example.test/v2/path",
                        entry.Message,
                        "The transient status retry log must name the request with its user info masked");
        Assert.DoesNotContain("user", entry.Message, "The transient status retry log must not contain the user name");
        Assert.DoesNotContain("secret", entry.Message, "The transient status retry log must not contain the password");
        Assert.DoesNotContain("token=x", entry.Message, "The transient status retry log must not contain the query string");
        Assert.AreEqual("https://user:secret@registry.example.test/v2/path?token=x",
                        probe.LastRequestUri,
                        "The request the inner handler receives must keep its user info and query so that connecting is unchanged");
    }

    /// <summary>
    /// Verify the transient exception retry log names the request without user info and query string
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerTransientExceptionLogOmitsUserInfoAndQueryAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([null, HttpStatusCode.OK], HttpStatusCode.OK);
        var logger = new TestLogger<TransientHttpRetryHandler>();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://user:secret@registry.example.test/v2/path?token=x");

        using var response = await SendThroughRetryHandlerAsync(2, probe, request: request, logger: logger).ConfigureAwait(false);

        var entry = logger.Entries.Single(candidate => candidate.EventId.Id == 3501);

        Assert.Contains("https://***@registry.example.test/v2/path",
                        entry.Message,
                        "The transient exception retry log must name the request with its user info masked");
        Assert.DoesNotContain("user", entry.Message, "The transient exception retry log must not contain the user name");
        Assert.DoesNotContain("secret", entry.Message, "The transient exception retry log must not contain the password");
        Assert.DoesNotContain("token=x", entry.Message, "The transient exception retry log must not contain the query string");
        Assert.AreEqual("https://user:secret@registry.example.test/v2/path?token=x",
                        probe.LastRequestUri,
                        "The request the inner handler receives must keep its user info and query so that connecting is unchanged");
    }

    /// <summary>
    /// Verify the retry log of a relative request URI drops the query string and masks user info
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task TransientHttpRetryHandlerRelativeRequestUriLogOmitsUserInfoAndQueryAsync()
    {
        var probe = new RetryProbeHttpMessageHandler([HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK], HttpStatusCode.OK);
        var logger = new TestLogger<TransientHttpRetryHandler>();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/user:secret@host/v2/path?token=x#frag", UriKind.Relative));

        using var response = await SendThroughRetryHandlerAsync(2, probe, request: request, logger: logger).ConfigureAwait(false);

        var entry = logger.Entries.Single(candidate => candidate.EventId.Id == 3500);

        Assert.Contains("***@host/v2/path",
                        entry.Message,
                        "The retry log of a relative request URI must name the target with its user info masked");
        Assert.DoesNotContain("secret", entry.Message, "The retry log must not contain the password of a relative request URI");
        Assert.DoesNotContain("token=x", entry.Message, "The retry log must not contain the query string of a relative request URI");
        Assert.DoesNotContain("frag", entry.Message, "The retry log must not contain the fragment of a relative request URI");
    }

    #endregion // Methods
}