using System.IO.Pipelines;
using System.Net;

using DockerUpdateGuard.Infrastructure;

using NSubstitute;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Clock that records every timer a request deadline arms and fires them on demand
/// </summary>
internal sealed class DeadlineTimerRecorder
{
    #region Fields

    private readonly Lock _lock = new();
    private readonly List<(TimerCallback Callback, object? State, TimeSpan DueTime, ITimer Timer)> _timers = [];

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    public DeadlineTimerRecorder()
    {
        var clock = Substitute.For<TimeProvider>();

        clock.CreateTimer(Arg.Any<TimerCallback>(), Arg.Any<object?>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>())
             .Returns(callInfo => Record(callInfo.ArgAt<TimerCallback>(0), callInfo.ArgAt<object?>(1), callInfo.ArgAt<TimeSpan>(2)));

        Clock = clock;
    }

    #endregion // Constructors

    #region Properties

    /// <summary>
    /// Clock that creates the recorded timers
    /// </summary>
    public TimeProvider Clock { get; }

    /// <summary>
    /// Number of timers created so far
    /// </summary>
    public int TimerCount
    {
        get
        {
            lock (_lock)
            {
                return _timers.Count;
            }
        }
    }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Create a response whose body never delivers a byte
    /// </summary>
    /// <param name="statusCode">Status code of the response</param>
    /// <returns>Response</returns>
    public static HttpResponseMessage CreateStalledResponse(HttpStatusCode statusCode)
    {
        return new HttpResponseMessage(statusCode)
               {
                   Content = new StreamContent(new Pipe().Reader.AsStream()),
               };
    }

    /// <summary>
    /// Create an HTTP client that sends through a request deadline handler on this clock with the timeout of the clients
    /// </summary>
    /// <param name="innerHandler">Handler that answers the requests</param>
    /// <param name="baseAddress">Base address of the client or null</param>
    /// <returns>HTTP client</returns>
    public HttpClient CreateHttpClient(HttpMessageHandler innerHandler, Uri? baseAddress)
    {
        var deadlineHandler = new RequestDeadlineHandler(Clock, TimeSpan.FromSeconds(30))
                              {
                                  InnerHandler = innerHandler,
                              };

        return new HttpClient(deadlineHandler)
               {
                   BaseAddress = baseAddress,
                   Timeout = TimeSpan.FromSeconds(30),
               };
    }

    /// <summary>
    /// Get the due time a timer was armed with
    /// </summary>
    /// <param name="index">Zero-based creation order of the timer</param>
    /// <returns>Due time</returns>
    public TimeSpan GetDueTime(int index)
    {
        lock (_lock)
        {
            return _timers[index].DueTime;
        }
    }

    /// <summary>
    /// Get the timer object handed out for a timer
    /// </summary>
    /// <param name="index">Zero-based creation order of the timer</param>
    /// <returns>Timer</returns>
    public ITimer GetTimer(int index)
    {
        lock (_lock)
        {
            return _timers[index].Timer;
        }
    }

    /// <summary>
    /// Fire the timer created last, the deadline of the request sent last
    /// </summary>
    public void FireLast()
    {
        (TimerCallback Callback, object? State, TimeSpan DueTime, ITimer Timer) last;

        lock (_lock)
        {
            last = _timers[^1];
        }

        last.Callback(last.State);
    }

    /// <summary>
    /// Record a timer
    /// </summary>
    /// <param name="callback">Callback of the timer</param>
    /// <param name="state">State of the timer</param>
    /// <param name="dueTime">Due time of the timer</param>
    /// <returns>Timer</returns>
    private ITimer Record(TimerCallback callback, object? state, TimeSpan dueTime)
    {
        var timer = Substitute.For<ITimer>();

        lock (_lock)
        {
            _timers.Add((callback, state, dueTime, timer));
        }

        return timer;
    }

    #endregion // Methods
}