using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore.Storage;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Execution strategy that retries every failure without delay and cancels a token on its first retry decision
/// </summary>
internal sealed class CancellingRetryExecutionStrategy : ExecutionStrategy
{
    #region Fields

    /// <summary>
    /// Source cancelled on the first retry decision
    /// </summary>
    private readonly CancellationTokenSource _cancellationTokenSource;

    /// <summary>
    /// Number of retry decisions, shared by every strategy instance of a test
    /// </summary>
    private readonly StrongBox<int> _retryDecisions;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="dependencies">Execution strategy dependencies</param>
    /// <param name="maxRetryCount">Maximum number of retries</param>
    /// <param name="cancellationTokenSource">Source cancelled on the first retry decision</param>
    /// <param name="retryDecisions">Counter of the retry decisions</param>
    public CancellingRetryExecutionStrategy(ExecutionStrategyDependencies dependencies,
                                            int maxRetryCount,
                                            CancellationTokenSource cancellationTokenSource,
                                            StrongBox<int> retryDecisions)
        : base(dependencies, maxRetryCount, TimeSpan.Zero)
    {
        _cancellationTokenSource = cancellationTokenSource;
        _retryDecisions = retryDecisions;
    }

    #endregion // Constructors

    #region ExecutionStrategy

    /// <inheritdoc/>
    protected override bool ShouldRetryOn(Exception exception)
    {
        if (Interlocked.Increment(ref _retryDecisions.Value) == 1)
        {
            _cancellationTokenSource.Cancel();
        }

        return true;
    }

    #endregion // ExecutionStrategy
}