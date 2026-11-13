using DockerUpdateGuard.Images;

using Microsoft.Extensions.Logging;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Testable scheduled background service that records how the base class drives its steps
/// </summary>
internal sealed class TestScheduledBackgroundService : ScheduledBackgroundService
{
    #region Fields

    /// <summary>
    /// Signal completed as soon as the scheduled execution ran once
    /// </summary>
    private readonly TaskCompletionSource _executionSignal;

    /// <summary>
    /// Configured execution interval
    /// </summary>
    private readonly TimeSpan _interval;

    /// <summary>
    /// Exception thrown by the startup step, or <see langword="null"/> when the startup step succeeds
    /// </summary>
    private readonly Exception? _startupException;

    /// <summary>
    /// Source of the interval of each lookup, receives the zero-based lookup number and may throw; <see langword="null"/> returns the configured interval
    /// </summary>
    private readonly Func<int, TimeSpan>? _intervalSource;

    /// <summary>
    /// Number of scheduled executions
    /// </summary>
    private int _executionCount;

    /// <summary>
    /// Number of interval lookups
    /// </summary>
    private int _intervalLookupCount;

    /// <summary>
    /// Number of startup executions
    /// </summary>
    private int _startupExecutionCount;

    /// <summary>
    /// Indicates whether the startup step ran before the first scheduled execution
    /// </summary>
    private bool _startupRanBeforeExecution;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="interval">Configured execution interval</param>
    /// <param name="startupException">Exception thrown by the startup step, or <see langword="null"/> when the startup step succeeds</param>
    public TestScheduledBackgroundService(ILogger logger, TimeSpan interval, Exception? startupException)
        : this(logger, TimeProvider.System, interval, null, startupException)
    {
    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="timeProvider">Time provider used for the wait</param>
    /// <param name="interval">Configured execution interval, also the initial interval of the base class</param>
    /// <param name="intervalSource">Source of the interval of each lookup, receives the zero-based lookup number and may throw; <see langword="null"/> returns the configured interval</param>
    /// <param name="startupException">Exception thrown by the startup step, or <see langword="null"/> when the startup step succeeds</param>
    public TestScheduledBackgroundService(ILogger logger,
                                          TimeProvider timeProvider,
                                          TimeSpan interval,
                                          Func<int, TimeSpan>? intervalSource,
                                          Exception? startupException)
        : base(logger, timeProvider, interval)
    {
        _intervalSource = intervalSource;
        _executionSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _interval = interval;
        _startupException = startupException;
    }

    #endregion // Constructors

    #region Properties

    /// <summary>
    /// Signal completed as soon as the scheduled execution ran once
    /// </summary>
    public Task ExecutionSignal => _executionSignal.Task;

    /// <summary>
    /// Number of scheduled executions
    /// </summary>
    public int ExecutionCount => Volatile.Read(ref _executionCount);

    /// <summary>
    /// Number of startup executions
    /// </summary>
    public int StartupExecutionCount => Volatile.Read(ref _startupExecutionCount);

    /// <summary>
    /// Indicates whether the startup step ran before the first scheduled execution
    /// </summary>
    public bool StartupRanBeforeExecution => Volatile.Read(ref _startupRanBeforeExecution);

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Run the scheduled loop of the base class until the supplied token is cancelled
    /// </summary>
    /// <param name="stoppingToken">Cancellation token</param>
    /// <returns>Task</returns>
    public Task RunAsync(CancellationToken stoppingToken)
    {
        return ExecuteAsync(stoppingToken);
    }

    #endregion // Methods

    #region ScheduledBackgroundService

    /// <inheritdoc/>
    protected override TimeSpan GetInterval()
    {
        var lookupNumber = Interlocked.Increment(ref _intervalLookupCount) - 1;

        return _intervalSource is null ? _interval : _intervalSource(lookupNumber);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteStartupAsync(CancellationToken stoppingToken)
    {
        await base.ExecuteStartupAsync(stoppingToken).ConfigureAwait(false);

        stoppingToken.ThrowIfCancellationRequested();

        Volatile.Write(ref _startupRanBeforeExecution, Volatile.Read(ref _executionCount) == 0);
        Interlocked.Increment(ref _startupExecutionCount);

        if (_startupException is not null)
        {
            throw _startupException;
        }
    }

    /// <inheritdoc/>
    protected override Task ExecuteCoreAsync(CancellationToken stoppingToken)
    {
        stoppingToken.ThrowIfCancellationRequested();

        Interlocked.Increment(ref _executionCount);
        _executionSignal.TrySetResult();

        return Task.CompletedTask;
    }

    #endregion // ScheduledBackgroundService
}