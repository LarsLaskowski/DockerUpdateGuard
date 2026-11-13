using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Images;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Testable cleanup background service facade
/// </summary>
internal sealed class TestScanCleanupBackgroundService : ScanCleanupBackgroundService
{
    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="optionsMonitor">Options monitor</param>
    /// <param name="serviceScopeFactory">Service scope factory</param>
    public TestScanCleanupBackgroundService(ILogger<ScanCleanupBackgroundService> logger,
                                            Microsoft.Extensions.Options.IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor,
                                            IServiceScopeFactory serviceScopeFactory)
        : this(logger,
               optionsMonitor,
               serviceScopeFactory,
               TimeProvider.System)
    {
    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="optionsMonitor">Options monitor</param>
    /// <param name="serviceScopeFactory">Service scope factory</param>
    /// <param name="timeProvider">Time provider used for the wait</param>
    public TestScanCleanupBackgroundService(ILogger<ScanCleanupBackgroundService> logger,
                                            Microsoft.Extensions.Options.IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor,
                                            IServiceScopeFactory serviceScopeFactory,
                                            TimeProvider timeProvider)
        : base(logger,
               optionsMonitor,
               serviceScopeFactory,
               timeProvider)
    {
    }

    #endregion // Constructors

    #region Methods

    /// <summary>
    /// Run the scheduled loop of the base class until the supplied token is cancelled
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    public Task RunAsync(CancellationToken cancellationToken)
    {
        return ExecuteAsync(cancellationToken);
    }

    /// <summary>
    /// Execute one cleanup cycle
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    public Task ExecuteOnceAsync(CancellationToken cancellationToken)
    {
        return ExecuteCoreAsync(cancellationToken);
    }

    /// <summary>
    /// Execute the startup step of the cleanup service
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    public Task ExecuteStartupOnceAsync(CancellationToken cancellationToken)
    {
        return ExecuteStartupAsync(cancellationToken);
    }

    #endregion // Methods
}