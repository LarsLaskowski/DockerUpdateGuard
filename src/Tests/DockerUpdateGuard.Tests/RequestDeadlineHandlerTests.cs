using System.Net;
using System.Text;

using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Helper;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="RequestDeadlineHandler"/>
/// </summary>
[TestClass]
public class RequestDeadlineHandlerTests
{
    #region Methods

    /// <summary>
    /// Verify the handler arms exactly one deadline with its timeout before it passes the request on
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task RequestDeadlineHandlerSendArmsDeadlineBeforeSendingAsync()
    {
        var recorder = new DeadlineTimerRecorder();
        var timersWhenSent = -1;

        using var inner = new DelegateHttpMessageHandler(_ =>
                                                         {
                                                             timersWhenSent = recorder.TimerCount;

                                                             return new HttpResponseMessage(HttpStatusCode.OK)
                                                                    {
                                                                        Content = new StringContent("{}"),
                                                                    };
                                                         });
        using var httpClient = recorder.CreateHttpClient(inner, null);
        using var response = await httpClient.GetAsync(new Uri("https://registry.example/v2/"), HttpCompletionOption.ResponseHeadersRead, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(1, timersWhenSent, "The deadline must be armed before the request is passed on");
        Assert.AreEqual(1, recorder.TimerCount, "The handler must arm exactly one deadline per request");
        Assert.AreEqual(TimeSpan.FromSeconds(30), recorder.GetDueTime(0), "The deadline must run for the configured timeout");
    }

    /// <summary>
    /// Verify a body read that ends in time returns the bytes unchanged and the content keeps every content header
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task RequestDeadlineHandlerResponseKeepsBodyAndContentHeadersAsync()
    {
        const string body = """{ "name": "acme/api", "tags": [ "1.0" ] }""";

        var recorder = new DeadlineTimerRecorder();
        var bodyBytes = Encoding.UTF8.GetBytes(body);

        using var inner = new DelegateHttpMessageHandler(_ =>
                                                         {
                                                             var content = new StringContent(body, Encoding.UTF8, "application/json");

                                                             content.Headers.ContentLanguage.Add("de");

                                                             return new HttpResponseMessage(HttpStatusCode.OK)
                                                                    {
                                                                        Content = content,
                                                                    };
                                                         });
        using var httpClient = recorder.CreateHttpClient(inner, null);
        using var response = await httpClient.GetAsync(new Uri("https://registry.example/v2/"), HttpCompletionOption.ResponseHeadersRead, CancellationToken.None).ConfigureAwait(false);

        var bytes = await response.Content.ReadAsByteArrayAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.AreSequenceEqual(bodyBytes, bytes, "A body read that ends in time must return the bytes of the original body");
        Assert.AreEqual("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString(), "The content type must keep its parameters");
        Assert.AreEqual(bodyBytes.Length, response.Content.Headers.ContentLength, "The content length must stay the one of the original content");
        Assert.AreEqual("de", Assert.ContainsSingle(response.Content.Headers.ContentLanguage, "Every other content header must be copied too"), "The content language must be copied unchanged");
    }

    /// <summary>
    /// Verify disposing the response disposes the timer the handler armed
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task RequestDeadlineHandlerResponseDisposedReleasesDeadlineTimerAsync()
    {
        var recorder = new DeadlineTimerRecorder();

        using var inner = new DelegateHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                                                              {
                                                                  Content = new StringContent("{}"),
                                                              });
        using var httpClient = recorder.CreateHttpClient(inner, null);

        var response = await httpClient.GetAsync(new Uri("https://registry.example/v2/"), HttpCompletionOption.ResponseHeadersRead, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(1, recorder.TimerCount, "The handler must have armed its deadline");
        recorder.GetTimer(0).DidNotReceive().Dispose();

        response.Dispose();

        recorder.GetTimer(0).Received(1).Dispose();
    }

    /// <summary>
    /// Verify an inner handler that throws before any response exists releases the deadline timer and rethrows
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task RequestDeadlineHandlerInnerHandlerThrowsReleasesDeadlineTimerAsync()
    {
        var recorder = new DeadlineTimerRecorder();

        using var inner = new DelegateHttpMessageHandler(_ => throw new HttpRequestException("connection failed"));
        using var httpClient = recorder.CreateHttpClient(inner, null);

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => httpClient.GetAsync(new Uri("https://registry.example/v2/"), HttpCompletionOption.ResponseHeadersRead, CancellationToken.None),
                                                              "The failure of the inner handler must reach the caller").ConfigureAwait(false);

        Assert.AreEqual(1, recorder.TimerCount, "The handler must have armed its deadline");
        recorder.GetTimer(0).Received(1).Dispose();
    }

    /// <summary>
    /// Verify a body that cannot be opened disposes the response and the deadline timer and rethrows
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task RequestDeadlineHandlerBodyStreamFailsReleasesDeadlineTimerAsync()
    {
        var recorder = new DeadlineTimerRecorder();
        var gate = Task.FromException(new IOException("body failed"));

        using var inner = new DelegateHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                                                              {
                                                                  Content = new GatedHttpContent(gate, "{}"),
                                                              });
        using var httpClient = recorder.CreateHttpClient(inner, null);

        await Assert.ThrowsAsync<HttpRequestException>(() => httpClient.GetAsync(new Uri("https://registry.example/v2/"), HttpCompletionOption.ResponseHeadersRead, CancellationToken.None),
                                                       "The failure of the body must reach the caller").ConfigureAwait(false);

        recorder.GetTimer(0).Received(1).Dispose();
    }

    #endregion // Methods
}