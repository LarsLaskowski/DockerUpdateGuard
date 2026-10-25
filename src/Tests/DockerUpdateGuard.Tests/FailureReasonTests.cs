using System.ComponentModel;

using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Portainer;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="FailureReason"/>
/// </summary>
[TestClass]
public class FailureReasonTests
{
    #region Constants

    private const string Marker = "internal.test";

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Verify the message of a reportable exception, also of a subtype, is passed on unchanged
    /// </summary>
    [TestMethod]
    public void FailureReasonFromExceptionWithReportableExceptionReturnsItsMessage()
    {
        var reportable = FailureReason.FromException(new ReportableException("Composed message for config.internal.test"), null, CancellationToken.None);
        var subtype = FailureReason.FromException(new PortainerAuthenticationException("Portainer login failed for portainer.internal.test"), null, CancellationToken.None);

        Assert.AreEqual("Composed message for config.internal.test", reportable, "The composed message of a reportable exception must pass unchanged");
        Assert.AreEqual("Portainer login failed for portainer.internal.test", subtype, "The composed message of a reportable exception subtype must pass unchanged");
    }

    /// <summary>
    /// Verify a cancellation while the caller token is cancelled is reported as cancelled
    /// </summary>
    [TestMethod]
    public void FailureReasonFromExceptionWithCallerCancellationReturnsCancelled()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);

        cts.Cancel();

        var token = cts.Token;
        var reason = FailureReason.FromException(new TaskCanceledException($"Canceled {Marker}", null, token), TimeSpan.FromSeconds(15), token);

        Assert.AreEqual("cancelled", reason, "A cancellation requested by the caller must read cancelled");
        Assert.DoesNotContain(Marker, reason, "The exception text must not reach the reason");
    }

    /// <summary>
    /// Verify a client timeout or timeout exception without a cancelled caller token is reported as timed out with the known seconds
    /// </summary>
    [TestMethod]
    public void FailureReasonFromExceptionWithTimeoutReturnsTimedOut()
    {
        var timeout = TimeSpan.FromSeconds(15);

        var fromTaskCanceled = FailureReason.FromException(new TaskCanceledException($"Timed out {Marker}", new TimeoutException()), timeout, CancellationToken.None);
        var fromTimeout = FailureReason.FromException(new TimeoutException($"Timed out {Marker}"), timeout, CancellationToken.None);

        Assert.AreEqual("timed out after 15 seconds", fromTaskCanceled, "A client timeout must read timed out with the known seconds");
        Assert.AreEqual("timed out after 15 seconds", fromTimeout, "A timeout exception must read timed out with the known seconds");
        Assert.DoesNotContain(Marker, fromTaskCanceled, "The exception text must not reach the reason");
    }

    /// <summary>
    /// Verify a timeout without a positive, finite timeout of its own is reported without seconds
    /// </summary>
    [TestMethod]
    public void FailureReasonFromExceptionWithTimeoutAndNoUsableTimeoutReturnsTimedOut()
    {
        var exception = new TimeoutException($"Timed out {Marker}");

        var withoutTimeout = FailureReason.FromException(exception, null, CancellationToken.None);
        var withZero = FailureReason.FromException(exception, TimeSpan.Zero, CancellationToken.None);
        var withInfinite = FailureReason.FromException(exception, Timeout.InfiniteTimeSpan, CancellationToken.None);

        Assert.AreEqual("timed out", withoutTimeout, "A timeout without a known timeout must read timed out");
        Assert.AreEqual("timed out", withZero, "A zero timeout must read timed out");
        Assert.AreEqual("timed out", withInfinite, "An infinite timeout must read timed out");
    }

    /// <summary>
    /// Verify transport failures, including a broken response body, are reported as connection failed
    /// </summary>
    [TestMethod]
    public void FailureReasonFromExceptionWithTransportFailureReturnsConnectionFailed()
    {
        var httpRequest = FailureReason.FromException(new HttpRequestException(HttpRequestError.ConnectionError, $"Connection refused ({Marker}:443)"), null, CancellationToken.None);
        var httpIo = FailureReason.FromException(new HttpIOException(HttpRequestError.ResponseEnded, $"Response ended {Marker}"), null, CancellationToken.None);
        var io = FailureReason.FromException(new IOException($"Broken pipe {Marker}"), null, CancellationToken.None);

        Assert.AreEqual("connection failed", httpRequest, "An HTTP request failure must read connection failed");
        Assert.AreEqual("connection failed", httpIo, "A broken response body must read connection failed");
        Assert.AreEqual("connection failed", io, "An IO failure must read connection failed");
        Assert.DoesNotContain(Marker, httpRequest, "The exception text must not reach the reason");
    }

    /// <summary>
    /// Verify a malformed JSON answer is reported as an invalid response
    /// </summary>
    [TestMethod]
    public void FailureReasonFromExceptionWithJsonExceptionReturnsInvalidResponse()
    {
        var reason = FailureReason.FromException(new System.Text.Json.JsonException($"Invalid JSON {Marker}"), null, CancellationToken.None);

        Assert.AreEqual("invalid response", reason, "A malformed JSON answer must read invalid response");
        Assert.DoesNotContain(Marker, reason, "The exception text must not reach the reason");
    }

    /// <summary>
    /// Verify every other exception is reported as an unexpected error
    /// </summary>
    [TestMethod]
    public void FailureReasonFromExceptionWithOtherExceptionReturnsUnexpectedError()
    {
        var invalidOperation = FailureReason.FromException(new InvalidOperationException($"Raw text {Marker}"), null, CancellationToken.None);
        var win32 = FailureReason.FromException(new Win32Exception(2, $"Cannot start {Marker}"), null, CancellationToken.None);

        Assert.AreEqual("unexpected error", invalidOperation, "An unknown exception must read unexpected error");
        Assert.AreEqual("unexpected error", win32, "A Win32 exception must read unexpected error in the shared mapping");
        Assert.DoesNotContain(Marker, invalidOperation, "The exception text must not reach the reason");
    }

    #endregion // Methods

    #region Properties

    /// <summary>
    /// Test context
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    #endregion // Properties
}