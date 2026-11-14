using DockerUpdateGuard.Configuration;

using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.DockerHub;

/// <summary>
/// Process-wide limit of the Docker Hub requests in flight, shared by all client instances and scopes and following
/// a reload of <c>DockerHub:MaxParallelRequests</c>; waiting requests are admitted in arrival order
/// </summary>
public sealed class DockerHubRequestLimiter : IDisposable
{
    #region Fields

    // Skeleton: removed with the implementation
#pragma warning disable S4487
    /// <summary>
    /// Options monitor
    /// </summary>
    private readonly IOptionsMonitor<DockerUpdateGuardOptions> _optionsMonitor;
#pragma warning restore S4487

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="optionsMonitor">Options monitor</param>
    public DockerHubRequestLimiter(IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    #endregion // Constructors

    #region Methods

    // Skeleton: removed with the implementation
#pragma warning disable S2325

    /// <summary>
    /// Wait for a request slot
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task that completes once a slot is taken</returns>
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    /// <summary>
    /// Release a request slot
    /// </summary>
    public void Release()
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

#pragma warning restore S2325

    #endregion // Methods

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        // Skeleton: removed with the implementation
    }

    #endregion // IDisposable
}