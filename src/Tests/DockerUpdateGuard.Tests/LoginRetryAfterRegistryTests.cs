using System.Net;
using System.Net.Http.Headers;

using DockerUpdateGuard.Infrastructure;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="LoginRetryAfterRegistry"/>
/// </summary>
[TestClass]
public class LoginRetryAfterRegistryTests
{
    #region Constants

    /// <summary>
    /// Login URL used by the tests
    /// </summary>
    private const string LoginUrl = "https://hub.docker.com/v2/users/login";

    /// <summary>
    /// User name used by the tests
    /// </summary>
    private const string UserName = "alice";

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Current time of the clock the tests move
    /// </summary>
    private DateTimeOffset _now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    #endregion // Fields

    #region Methods

    /// <summary>
    /// Verify a window of one user name does not defer another user name, and that user names are compared ordinally
    /// </summary>
    [TestMethod]
    [TestCategory("AC6")]
    public void LoginRetryAfterRegistryOtherUserNameIsNotDeferred()
    {
        var registry = CreateRegistry();
        var loginUri = new Uri(LoginUrl);

        registry.RecordLoginResponse(loginUri, UserName, CreateTooManyRequests(120));

        Assert.IsTrue(registry.IsLoginDeferred(loginUri, UserName, out var retryAt), "The user name that received the 429 must be deferred");
        Assert.AreEqual(_now.AddSeconds(120), retryAt, "The window must end at the time of the response plus the requested wait");
        Assert.IsFalse(registry.IsLoginDeferred(loginUri, "bob", out _), "Another user name must not be deferred");
        Assert.IsFalse(registry.IsLoginDeferred(loginUri, "Alice", out _), "User names must be compared ordinally, not ignoring case");
    }

    /// <summary>
    /// Verify a window of one login host or path does not defer another login URL
    /// </summary>
    [TestMethod]
    [TestCategory("AC6")]
    public void LoginRetryAfterRegistryOtherHostIsNotDeferred()
    {
        var registry = CreateRegistry();

        registry.RecordLoginResponse(new Uri(LoginUrl), UserName, CreateTooManyRequests(120));

        Assert.IsTrue(registry.IsLoginDeferred(new Uri(LoginUrl), UserName, out _), "The login URL that received the 429 must be deferred");
        Assert.IsFalse(registry.IsLoginDeferred(new Uri("https://registry.example.test/v2/users/login"), UserName, out _), "Another host must not be deferred");
        Assert.IsFalse(registry.IsLoginDeferred(new Uri("https://hub.docker.com/api/auth"), UserName, out _), "Another login path on the same host must not be deferred");
    }

    /// <summary>
    /// Verify user info, query and fragment of the login URL do not take part in the key
    /// </summary>
    [TestMethod]
    [TestCategory("AC6")]
    public void LoginRetryAfterRegistryUserInfoAndQueryAreIgnored()
    {
        var registry = CreateRegistry();

        registry.RecordLoginResponse(new Uri("https://name:secret@hub.docker.com/v2/users/login?attempt=1#top"), UserName, CreateTooManyRequests(120));

        Assert.IsTrue(registry.IsLoginDeferred(new Uri(LoginUrl), UserName, out _), "A window recorded with user info, query and fragment must apply to the plain login URL");

        registry.RecordLoginResponse(new Uri("https://hub.docker.com/api/auth"), UserName, CreateTooManyRequests(60));

        Assert.IsTrue(registry.IsLoginDeferred(new Uri("https://other:pw@hub.docker.com/api/auth?x=y"), UserName, out _), "A window recorded for the plain login URL must apply to a URL with user info and query");
    }

    /// <summary>
    /// Verify a later window never shortens a longer one, a longer one extends it, and other responses leave it as it is
    /// </summary>
    [TestMethod]
    [TestCategory("AC6")]
    public void LoginRetryAfterRegistryShorterWindowKeepsLongerOne()
    {
        var registry = CreateRegistry();
        var loginUri = new Uri(LoginUrl);

        registry.RecordLoginResponse(loginUri, UserName, CreateTooManyRequests(300));
        registry.RecordLoginResponse(loginUri, UserName, CreateTooManyRequests(60));

        Assert.IsTrue(registry.IsLoginDeferred(loginUri, UserName, out var retryAt), "The window must still be open");
        Assert.AreEqual(_now.AddSeconds(300), retryAt, "A shorter later window must not shorten the longer one");

        registry.RecordLoginResponse(loginUri, UserName, CreateTooManyRequests(900));

        Assert.IsTrue(registry.IsLoginDeferred(loginUri, UserName, out retryAt), "The window must still be open after an extension");
        Assert.AreEqual(_now.AddSeconds(900), retryAt, "A longer later window must extend the window");

        registry.RecordLoginResponse(loginUri, UserName, CreateTooManyRequests(1, HttpStatusCode.ServiceUnavailable));
        registry.RecordLoginResponse(loginUri, UserName, new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        registry.RecordLoginResponse(loginUri, UserName, new HttpResponseMessage(HttpStatusCode.Unauthorized));

        Assert.IsTrue(registry.IsLoginDeferred(loginUri, UserName, out retryAt), "Responses that open no window must not end an open one");
        Assert.AreEqual(_now.AddSeconds(900), retryAt, "Responses that open no window must leave the window as it is");
    }

    /// <summary>
    /// Verify a successful login removes the window and that a failure without Retry-After opens none
    /// </summary>
    [TestMethod]
    [TestCategory("AC6")]
    public void LoginRetryAfterRegistrySuccessRemovesWindow()
    {
        var registry = CreateRegistry();
        var loginUri = new Uri(LoginUrl);

        registry.RecordLoginResponse(loginUri, UserName, CreateTooManyRequests(300));
        registry.RecordLoginResponse(loginUri, UserName, new HttpResponseMessage(HttpStatusCode.OK));

        Assert.IsFalse(registry.IsLoginDeferred(loginUri, UserName, out _), "A successful login must remove the window");

        registry.RecordLoginResponse(loginUri, "bob", new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        Assert.IsFalse(registry.IsLoginDeferred(loginUri, "bob", out _), "A 429 without Retry-After must open no window");
    }

    /// <summary>
    /// Verify a window ends when the clock reaches its end and is removed when read
    /// </summary>
    [TestMethod]
    [TestCategory("AC6")]
    public void LoginRetryAfterRegistryPassedWindowIsNotDeferred()
    {
        var registry = CreateRegistry();
        var loginUri = new Uri(LoginUrl);
        var start = _now;

        registry.RecordLoginResponse(loginUri, UserName, CreateTooManyRequests(120));

        _now = start.AddSeconds(119);

        Assert.IsTrue(registry.IsLoginDeferred(loginUri, UserName, out var retryAt), "The login must be deferred until the clock reaches the end of the window");
        Assert.AreEqual(start.AddSeconds(120), retryAt, "The reported end must be the end of the window");

        _now = start.AddSeconds(120);

        Assert.IsFalse(registry.IsLoginDeferred(loginUri, UserName, out _), "The login must not be deferred once the clock reaches the end of the window");

        _now = start.AddSeconds(10);

        Assert.IsFalse(registry.IsLoginDeferred(loginUri, UserName, out _), "A window that has passed must be removed when read, not only ignored");
    }

    /// <summary>
    /// Create a response that asks for a wait in seconds
    /// </summary>
    /// <param name="seconds">Requested wait in seconds</param>
    /// <param name="statusCode">Status code of the response</param>
    /// <returns>Response</returns>
    private static HttpResponseMessage CreateTooManyRequests(int seconds, HttpStatusCode statusCode = HttpStatusCode.TooManyRequests)
    {
        var response = new HttpResponseMessage(statusCode);

        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));

        return response;
    }

    /// <summary>
    /// Create a registry on the clock of the test
    /// </summary>
    /// <returns>Registry</returns>
    private LoginRetryAfterRegistry CreateRegistry()
    {
        var clock = Substitute.For<TimeProvider>();

        clock.GetUtcNow().Returns(_ => _now);

        return new LoginRetryAfterRegistry(clock);
    }

    #endregion // Methods
}