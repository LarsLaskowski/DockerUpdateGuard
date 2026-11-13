using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Images;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Testable observed-image refresh background service facade
/// </summary>
internal sealed class TestOwnImageBaseRefreshBackgroundService : OwnImageBaseRefreshBackgroundService
{
    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="optionsMonitor">Options monitor</param>
    /// <param name="serviceScopeFactory">Service scope factory</param>
    /// <param name="timeProvider">Time provider used for the wait</param>
    public TestOwnImageBaseRefreshBackgroundService(ILogger<OwnImageBaseRefreshBackgroundService> logger,
                                                    IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor,
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

    #endregion // Methods
}