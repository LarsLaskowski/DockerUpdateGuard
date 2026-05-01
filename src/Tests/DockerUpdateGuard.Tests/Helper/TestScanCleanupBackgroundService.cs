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
        : base(logger,
               optionsMonitor,
               serviceScopeFactory)
    {
    }

    #endregion // Constructors

    #region Methods

    /// <summary>
    /// Execute one cleanup cycle
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    public Task ExecuteOnceAsync(CancellationToken cancellationToken)
    {
        return ExecuteCoreAsync(cancellationToken);
    }

    #endregion // Methods
}