using System.Threading.Channels;

using NSubstitute;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Time provider substitute that records every timer a wait creates and lets a test elapse the wait
/// </summary>
internal sealed class WaitRecordingTimeProvider
{
    #region Constants

    /// <summary>
    /// Upper bound in seconds a test waits for a job to start its next wait before it gives up
    /// </summary>
    private const int SafetyTimeoutSeconds = 10;

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Guard of the recorded waits
    /// </summary>
    private readonly Lock _lock;

    /// <summary>
    /// Signal written once per started wait
    /// </summary>
    private readonly Channel<bool> _waitStarted;

    /// <summary>
    /// Intervals of the started waits in start order
    /// </summary>
    private readonly List<TimeSpan> _waits;

    /// <summary>
    /// Callback of the most recently started wait
    /// </summary>
    private TimerCallback? _lastCallback;

    /// <summary>
    /// State of the most recently started wait
    /// </summary>
    private object? _lastState;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    public WaitRecordingTimeProvider()
    {
        _lock = new Lock();
        _waitStarted = Channel.CreateUnbounded<bool>();
        _waits = [];
        TimeProvider = Substitute.For<TimeProvider>();
        TimeProvider.CreateTimer(Arg.Any<TimerCallback>(),
                                 Arg.Any<object?>(),
                                 Arg.Any<TimeSpan>(),
                                 Arg.Any<TimeSpan>())
                    .Returns(callInfo => RecordWait(callInfo.Arg<TimerCallback>(),
                                                    callInfo.Arg<object?>(),
                                                    callInfo.ArgAt<TimeSpan>(2)));
    }

    #endregion // Constructors

    #region Properties

    /// <summary>
    /// Substituted time provider handed to the job
    /// </summary>
    public TimeProvider TimeProvider { get; }

    /// <summary>
    /// Intervals of the waits started so far in start order
    /// </summary>
    public IReadOnlyList<TimeSpan> Waits
    {
        get
        {
            lock (_lock)
            {
                return _waits.ToList();
            }
        }
    }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Wait until the job starts its next wait, or until the job's task ends
    /// </summary>
    /// <param name="jobTask">Task of the running job</param>
    /// <returns><see langword="true"/> when a wait started; <see langword="false"/> when the job's task ended first</returns>
    public async Task<bool> WaitForNextWaitAsync(Task jobTask)
    {
        var startedTask = _waitStarted.Reader.ReadAsync().AsTask();
        var finishedTask = await Task.WhenAny(startedTask, jobTask)
                                     .WaitAsync(TimeSpan.FromSeconds(SafetyTimeoutSeconds))
                                     .ConfigureAwait(false);

        return ReferenceEquals(finishedTask, startedTask);
    }

    /// <summary>
    /// Let the most recently started wait elapse
    /// </summary>
    public void ElapseLastWait()
    {
        TimerCallback? callback;
        object? state;

        lock (_lock)
        {
            callback = _lastCallback;
            state = _lastState;
        }

        callback?.Invoke(state);
    }

    /// <summary>
    /// Record a started wait
    /// </summary>
    /// <param name="callback">Timer callback</param>
    /// <param name="state">Timer state</param>
    /// <param name="dueTime">Interval of the wait</param>
    /// <returns>Timer substitute</returns>
    private ITimer RecordWait(TimerCallback callback, object? state, TimeSpan dueTime)
    {
        lock (_lock)
        {
            _waits.Add(dueTime);
            _lastCallback = callback;
            _lastState = state;
        }

        _waitStarted.Writer.TryWrite(true);

        return Substitute.For<ITimer>();
    }

    #endregion // Methods
}