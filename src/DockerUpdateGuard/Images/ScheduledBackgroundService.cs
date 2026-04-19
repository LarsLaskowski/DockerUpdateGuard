using System.Diagnostics;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Base class for periodic hosted services
/// </summary>
public abstract class ScheduledBackgroundService : BackgroundService
{
    #region Fields

    private readonly ILogger _logger;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="logger">Logger</param>
    protected ScheduledBackgroundService(ILogger logger)
    {
        _logger = logger;
    }

    #endregion // Constructors

    #region Methods

    /// <summary>
    /// Resolve the current execution interval
    /// </summary>
    /// <returns>Execution interval</returns>
    protected abstract TimeSpan GetInterval();

    /// <summary>
    /// Execute the actual background operation
    /// </summary>
    /// <param name="stoppingToken">Cancellation token</param>
    /// <returns>Task</returns>
    protected abstract Task ExecuteCoreAsync(CancellationToken stoppingToken);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backgroundServiceName = GetType().Name;

        _logger.BackgroundServiceStarted(backgroundServiceName, GetInterval().TotalMinutes);
        await ExecuteSafelyAsync(stoppingToken)
              .ConfigureAwait(false);

        while (stoppingToken.IsCancellationRequested == false)
        {
            var interval = GetInterval();

            _logger.BackgroundServiceWaiting(backgroundServiceName, interval.TotalMinutes);
            await Task.Delay(interval, stoppingToken)
                      .ConfigureAwait(false);
            await ExecuteSafelyAsync(stoppingToken)
                  .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Execute the background operation with exception logging
    /// </summary>
    /// <param name="stoppingToken">Cancellation token</param>
    /// <returns>Task</returns>
    private async Task ExecuteSafelyAsync(CancellationToken stoppingToken)
    {
        var backgroundServiceName = GetType().Name;
        var stopwatch = Stopwatch.StartNew();

        _logger.BackgroundServiceExecutionStarted(backgroundServiceName);

        try
        {
            await ExecuteCoreAsync(stoppingToken)
                  .ConfigureAwait(false);
            _logger.BackgroundServiceExecutionCompleted(backgroundServiceName, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.BackgroundServiceExecutionFailed(exception,
                                                     backgroundServiceName,
                                                     stopwatch.ElapsedMilliseconds);
        }
    }

    #endregion // Methods
}