using System.Buffers.Text;
using System.Text;

using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Infrastructure;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DockerHubAccessTokenCache"/>
/// </summary>
[TestClass]
public class DockerHubAccessTokenCacheTests
{
    #region Constants

    /// <summary>
    /// Login URL used by the tests
    /// </summary>
    private const string LoginUrl = "https://hub.docker.com/v2/users/login";

    /// <summary>
    /// User name used by the tests
    /// </summary>
    private const string UserName = "acme";

    /// <summary>
    /// Personal access token used by the tests
    /// </summary>
    private const string Pat = "test-pat";

    /// <summary>
    /// Failure message of a refused login
    /// </summary>
    private const string LoginFailedMessage = "Docker Hub authentication token request failed: HTTP 401";

    /// <summary>
    /// Time limit of a login in seconds
    /// </summary>
    private const int LoginTimeoutSeconds = 30;

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Current time of the clock the tests move
    /// </summary>
    private DateTimeOffset _now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Context for the tests
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Static methods

    /// <summary>
    /// Create a token whose payload carries the given JSON
    /// </summary>
    /// <param name="payloadJson">JSON of the payload segment</param>
    /// <returns>Token of three segments</returns>
    private static string CreateToken(string payloadJson)
    {
        return $"header.{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payloadJson))}.signature";
    }

    /// <summary>
    /// Create a token whose payload carries the given expiry as its exp claim
    /// </summary>
    /// <param name="expiresAt">Expiry of the token</param>
    /// <param name="subject">Subject that tells tokens of the same expiry apart</param>
    /// <returns>Token of three segments</returns>
    private static string CreateToken(DateTimeOffset expiresAt, string subject)
    {
        return CreateToken($$"""{ "sub": "{{subject}}", "exp": {{expiresAt.ToUnixTimeSeconds()}} }""");
    }

    /// <summary>
    /// Create a login delegate that hands out the given results in order and records each call; the last result repeats
    /// </summary>
    /// <param name="calls">Receives one entry per call</param>
    /// <param name="results">Results of the calls</param>
    /// <returns>Login delegate</returns>
    private static Func<CancellationToken, Task<ExternalOperationResult<string>>> CreateRequest(List<int> calls, params ExternalOperationResult<string>[] results)
    {
        return _ =>
               {
                   var result = results[Math.Min(calls.Count, results.Length - 1)];

                   calls.Add(calls.Count);

                   return Task.FromResult(result);
               };
    }

    /// <summary>
    /// Create a login delegate that hands out the given tokens in order and records each call; the last token repeats
    /// </summary>
    /// <param name="calls">Receives one entry per call</param>
    /// <param name="tokens">Tokens of the calls</param>
    /// <returns>Login delegate</returns>
    private static Func<CancellationToken, Task<ExternalOperationResult<string>>> CreateRequest(List<int> calls, params string[] tokens)
    {
        return CreateRequest(calls,
                             tokens.Select(ExternalOperationResult.Succeeded<string>)
                                   .ToArray());
    }

    /// <summary>
    /// Get the token from the cache with the test's login target unless another one is given
    /// </summary>
    /// <param name="cache">Cache under test</param>
    /// <param name="request">Login delegate</param>
    /// <param name="cancellationToken">Cancellation token of the caller</param>
    /// <param name="loginUrl">Login URL</param>
    /// <param name="userName">User name</param>
    /// <param name="pat">Personal access token</param>
    /// <returns>Access token result</returns>
    private static Task<ExternalOperationResult<string>> GetAsync(DockerHubAccessTokenCache cache,
                                                                  Func<CancellationToken, Task<ExternalOperationResult<string>>> request,
                                                                  CancellationToken cancellationToken,
                                                                  string loginUrl = LoginUrl,
                                                                  string userName = UserName,
                                                                  string pat = Pat)
    {
        return cache.GetOrRequestAsync(new Uri(loginUrl),
                                       userName,
                                       pat,
                                       TimeSpan.FromSeconds(LoginTimeoutSeconds),
                                       request,
                                       cancellationToken);
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Verify a token is reused while the clock is more than one minute before its exp claim
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task DockerHubAccessTokenCacheGetOrRequestAsyncValidTokenIsReusedAsync()
    {
        var calls = new List<int>();
        var token = CreateToken(_now.AddMinutes(10), "first");
        var request = CreateRequest(calls, token, CreateToken(_now.AddMinutes(20), "second"));

        using var cache = new DockerHubAccessTokenCache(CreateClock());

        await GetAsync(cache, request, TestContext.CancellationToken).ConfigureAwait(false);

        _now = _now.AddMinutes(9).AddSeconds(-1);

        var reused = await GetAsync(cache, request, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, reused.Status, "A cached token must be returned as a success");
        Assert.AreEqual(token, reused.Data, "A token more than one minute before its expiry must be reused");
        Assert.HasCount(1, calls, "A valid cached token must not send another login");
    }

    /// <summary>
    /// Verify a token is not reused once the clock is within one minute of its exp claim
    /// </summary>
    /// <param name="elapsedSeconds">Seconds after the login, the token expiring ten minutes after it</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    [DataRow(540)]
    [DataRow(570)]
    public async Task DockerHubAccessTokenCacheGetOrRequestAsyncTokenWithinOneMinuteOfExpiryRequestsAgainAsync(int elapsedSeconds)
    {
        var calls = new List<int>();
        var secondToken = CreateToken(_now.AddMinutes(20), "second");
        var request = CreateRequest(calls, CreateToken(_now.AddMinutes(10), "first"), secondToken);

        using var cache = new DockerHubAccessTokenCache(CreateClock());

        await GetAsync(cache, request, TestContext.CancellationToken).ConfigureAwait(false);

        _now = _now.AddSeconds(elapsedSeconds);

        var renewed = await GetAsync(cache, request, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, calls, $"A token {600 - elapsedSeconds} seconds before its expiry must not be reused");
        Assert.AreEqual(secondToken, renewed.Data, "The call must return the token of the new login");
    }

    /// <summary>
    /// Verify a token without exp claim is kept for five minutes after it was stored, reused until one minute before that
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task DockerHubAccessTokenCacheGetOrRequestAsyncTokenWithoutExpiryRequestsAgainAfterFiveMinutesAsync()
    {
        var calls = new List<int>();
        var firstToken = CreateToken("""{ "sub": "first" }""");
        var secondToken = CreateToken("""{ "sub": "second" }""");
        var request = CreateRequest(calls, firstToken, secondToken);

        using var cache = new DockerHubAccessTokenCache(CreateClock());

        await GetAsync(cache, request, TestContext.CancellationToken).ConfigureAwait(false);

        _now = _now.AddMinutes(4).AddSeconds(-1);

        var reused = await GetAsync(cache, request, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(firstToken, reused.Data, "A token without exp must be reused until one minute before five minutes after it was stored");
        Assert.HasCount(1, calls, "A token without exp must not send another login within its fallback lifetime");

        _now = _now.AddSeconds(1);

        var renewed = await GetAsync(cache, request, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(secondToken, renewed.Data, "A token without exp must be replaced one minute before five minutes after it was stored");
        Assert.HasCount(2, calls, "A token without exp must log in again once its fallback lifetime is within one minute of its end");
    }

    /// <summary>
    /// Verify a changed PAT logs in again and replaces the entry
    /// </summary>
    /// <param name="changedPat">PAT of the second login</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    [DataRow("other-pat")]
    [DataRow("TEST-PAT")]
    public async Task DockerHubAccessTokenCacheGetOrRequestAsyncChangedPatRequestsAgainAsync(string changedPat)
    {
        await AssertChangedTargetRequestsAgainAsync(LoginUrl, UserName, changedPat).ConfigureAwait(false);
    }

    /// <summary>
    /// Verify a changed user name logs in again and replaces the entry; user names are compared ordinally
    /// </summary>
    /// <param name="changedUserName">User name of the second login</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    [DataRow("other")]
    [DataRow("Acme")]
    public async Task DockerHubAccessTokenCacheGetOrRequestAsyncChangedUserNameRequestsAgainAsync(string changedUserName)
    {
        await AssertChangedTargetRequestsAgainAsync(LoginUrl, changedUserName, Pat).ConfigureAwait(false);
    }

    /// <summary>
    /// Verify a login URL that differs in scheme, host, port or path logs in again and replaces the entry
    /// </summary>
    /// <param name="changedLoginUrl">Login URL of the second login</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    [DataRow("http://hub.docker.com/v2/users/login")]
    [DataRow("https://registry.example.test/v2/users/login")]
    [DataRow("https://hub.docker.com:8443/v2/users/login")]
    [DataRow("https://hub.docker.com/api/auth")]
    public async Task DockerHubAccessTokenCacheGetOrRequestAsyncChangedLoginUriRequestsAgainAsync(string changedLoginUrl)
    {
        await AssertChangedTargetRequestsAgainAsync(changedLoginUrl, UserName, Pat).ConfigureAwait(false);
    }

    /// <summary>
    /// Verify concurrent callers on an empty cache log in once and all receive the token of that login
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task DockerHubAccessTokenCacheGetOrRequestAsyncConcurrentCallersRequestOnceAsync()
    {
        var calls = 0;
        var token = CreateToken(_now.AddMinutes(10), "first");
        var loginResult = new TaskCompletionSource<ExternalOperationResult<string>>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var cache = new DockerHubAccessTokenCache(CreateClock());

        var callers = Enumerable.Range(0, 3)
                                .Select(_ => GetAsync(cache,
                                                      _ =>
                                                      {
                                                          Interlocked.Increment(ref calls);

                                                          return loginResult.Task;
                                                      },
                                                      TestContext.CancellationToken))
                                .ToArray();

        Assert.DoesNotContain(caller => caller.IsCompleted, callers, "No caller may finish before the pending login completes");

        loginResult.SetResult(ExternalOperationResult.Succeeded<string>(token));

        var results = await Task.WhenAll(callers).ConfigureAwait(false);

        Assert.AreEqual(1, calls, "Concurrent callers on an empty cache must log in exactly once");
        Assert.AreSequenceEqual(new[] { token, token, token },
                                results.Select(result => result.Data)
                                       .ToArray(),
                                "Every concurrent caller must receive the token of the one login");
    }

    /// <summary>
    /// Verify a failed login stores nothing, is returned unchanged and the next call logs in again
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task DockerHubAccessTokenCacheGetOrRequestAsyncFailedRequestStoresNothingAsync()
    {
        var calls = new List<int>();
        var token = CreateToken(_now.AddMinutes(10), "first");
        var request = CreateRequest(calls,
                                    ExternalOperationResult.Failed<string>(LoginFailedMessage),
                                    ExternalOperationResult.Succeeded<string>(token));

        using var cache = new DockerHubAccessTokenCache(CreateClock());

        var failed = await GetAsync(cache, request, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, failed.Status, "A failed login must be returned as failed");
        Assert.AreEqual(LoginFailedMessage, failed.Message, "A failed login must be returned unchanged");

        var next = await GetAsync(cache, request, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, calls, "The call after a failed login must log in again");
        Assert.AreEqual(token, next.Data, "The call after a failed login must return the token of its own login");
    }

    /// <summary>
    /// Verify a caller cancelled while waiting for another caller's login throws, while that login is stored and the next call is not blocked
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task DockerHubAccessTokenCacheGetOrRequestAsyncCancelledWaitThrowsAndReleasesNothingAsync()
    {
        var calls = new List<int>();
        var token = CreateToken(_now.AddMinutes(10), "first");
        var loginResult = new TaskCompletionSource<ExternalOperationResult<string>>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var cache = new DockerHubAccessTokenCache(CreateClock());
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);

        var login = GetAsync(cache,
                             _ =>
                             {
                                 calls.Add(calls.Count);

                                 return loginResult.Task;
                             },
                             TestContext.CancellationToken);
        var waiting = GetAsync(cache, CreateRequest(calls, "unexpected"), waitCancellation.Token);

        await waitCancellation.CancelAsync().ConfigureAwait(false);

        await Assert.ThrowsAsync<OperationCanceledException>(() => waiting, "A caller cancelled while waiting for another login must throw OperationCanceledException")
                    .ConfigureAwait(false);

        loginResult.SetResult(ExternalOperationResult.Succeeded<string>(token));

        var loggedIn = await login.ConfigureAwait(false);
        var next = await GetAsync(cache, CreateRequest(calls, "unexpected"), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(token, loggedIn.Data, "The login of the other caller must complete");
        Assert.AreEqual(token, next.Data, "The next call must receive the stored token of the other caller's login");
        Assert.HasCount(1, calls, "The cancelled wait must send no login and the next call must reuse the stored token");
    }

    /// <summary>
    /// Verify a login that does not complete within the login timeout of the injected clock is cancelled, stores nothing and frees the lock
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC7")]
    public async Task DockerHubAccessTokenCacheGetOrRequestAsyncLoginTimeoutCancelsLoginAndStoresNothingAsync()
    {
        var calls = new List<int>();
        var token = CreateToken(_now.AddMinutes(10), "first");
        TimerCallback? timerCallback = null;
        object? timerState = null;
        var clock = CreateClock();

        clock.CreateTimer(Arg.Any<TimerCallback>(), Arg.Any<object?>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>())
             .Returns(callInfo =>
                      {
                          timerCallback ??= callInfo.ArgAt<TimerCallback>(0);
                          timerState ??= callInfo.ArgAt<object?>(1);

                          return Substitute.For<ITimer>();
                      });

        using var cache = new DockerHubAccessTokenCache(clock);
        using var callerCancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);

        var stalled = GetAsync(cache,
                               async loginCancellationToken =>
                               {
                                   calls.Add(calls.Count);

                                   await Task.Delay(Timeout.Infinite, loginCancellationToken).ConfigureAwait(false);

                                   return ExternalOperationResult.Succeeded<string>("unexpected");
                               },
                               callerCancellation.Token);

        Assert.IsFalse(stalled.IsCompleted, "The login must still be waiting for its stalled request");
        Assert.IsNotNull(timerCallback, "The login timeout must be armed on the injected clock");

        clock.Received(1).CreateTimer(Arg.Any<TimerCallback>(), Arg.Any<object?>(), TimeSpan.FromSeconds(LoginTimeoutSeconds), Timeout.InfiniteTimeSpan);

        timerCallback(timerState);

        await Assert.ThrowsAsync<OperationCanceledException>(() => stalled, "A login stopped by its timeout must throw OperationCanceledException")
                    .ConfigureAwait(false);

        Assert.IsFalse(callerCancellation.IsCancellationRequested, "The login timeout must not cancel the caller's token");

        var next = await GetAsync(cache, CreateRequest(calls, token), callerCancellation.Token).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, next.Status, "The call after a timed-out login must log in and succeed");
        Assert.AreEqual(token, next.Data, "The timed-out login must store nothing, so the next call returns the token of its own login");
        Assert.HasCount(2, calls, "The call after a timed-out login must log in again without waiting for the lock");
    }

    /// <summary>
    /// Log in for one target, then for a changed target, and assert the changed target logs in again and replaces the entry
    /// </summary>
    /// <param name="changedLoginUrl">Login URL of the changed target</param>
    /// <param name="changedUserName">User name of the changed target</param>
    /// <param name="changedPat">PAT of the changed target</param>
    /// <returns>Task</returns>
    private async Task AssertChangedTargetRequestsAgainAsync(string changedLoginUrl, string changedUserName, string changedPat)
    {
        var calls = new List<int>();
        var firstToken = CreateToken(_now.AddMinutes(10), "first");
        var secondToken = CreateToken(_now.AddMinutes(10), "second");
        var thirdToken = CreateToken(_now.AddMinutes(10), "third");
        var request = CreateRequest(calls, firstToken, secondToken, thirdToken);

        using var cache = new DockerHubAccessTokenCache(CreateClock());

        await GetAsync(cache, request, TestContext.CancellationToken).ConfigureAwait(false);

        var changed = await GetAsync(cache, request, TestContext.CancellationToken, changedLoginUrl, changedUserName, changedPat).ConfigureAwait(false);

        Assert.HasCount(2, calls, "A call for another login target must log in again");
        Assert.AreEqual(secondToken, changed.Data, "A call for another login target must not receive the cached token");

        var changedAgain = await GetAsync(cache, request, TestContext.CancellationToken, changedLoginUrl, changedUserName, changedPat).ConfigureAwait(false);

        Assert.AreEqual(secondToken, changedAgain.Data, "The token of the changed target must be stored");
        Assert.HasCount(2, calls, "A repeated call for the changed target must reuse its stored token");

        var original = await GetAsync(cache, request, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(thirdToken, original.Data, "The token of the changed target must have replaced the first entry");
        Assert.HasCount(3, calls, "A call for the first target must log in again once its entry was replaced");
    }

    /// <summary>
    /// Create a clock that answers the current time of the test
    /// </summary>
    /// <returns>Clock</returns>
    private TimeProvider CreateClock()
    {
        var clock = Substitute.For<TimeProvider>();

        clock.GetUtcNow().Returns(_ => _now);

        return clock;
    }

    #endregion // Methods
}