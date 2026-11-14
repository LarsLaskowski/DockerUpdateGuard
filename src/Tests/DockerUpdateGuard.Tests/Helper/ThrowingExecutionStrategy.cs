using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Execution strategy that runs its first operations unchanged and throws a given exception instead of every later one
/// </summary>
internal sealed class ThrowingExecutionStrategy : IExecutionStrategy
{
    #region Fields

    /// <summary>
    /// Exception thrown instead of running an operation
    /// </summary>
    private readonly Exception _exception;

    /// <summary>
    /// Number of operations still run unchanged before every operation throws
    /// </summary>
    private int _remainingPassThroughs;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="exception">Exception thrown instead of running an operation</param>
    /// <param name="passThroughCount">Number of leading operations that run unchanged, such as the connection open of the startup wait</param>
    public ThrowingExecutionStrategy(Exception exception, int passThroughCount)
    {
        _exception = exception;
        _remainingPassThroughs = passThroughCount;
    }

    #endregion // Constructors

    #region Methods

    /// <summary>
    /// Consume one of the leading operations that run unchanged
    /// </summary>
    /// <returns><see langword="true"/> when the operation runs unchanged</returns>
    private bool TryPassThrough()
    {
        return Interlocked.Decrement(ref _remainingPassThroughs) >= 0;
    }

    #endregion // Methods

    #region IExecutionStrategy

    /// <inheritdoc/>
    public bool RetriesOnFailure => false;

    /// <inheritdoc/>
    public TResult Execute<TState, TResult>(TState state,
                                            Func<DbContext, TState, TResult> operation,
                                            Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded)
    {
        if (TryPassThrough())
        {
            return operation(null!, state);
        }

        throw _exception;
    }

    /// <inheritdoc/>
    public Task<TResult> ExecuteAsync<TState, TResult>(TState state,
                                                       Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
                                                       Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
                                                       CancellationToken cancellationToken = default)
    {
        if (TryPassThrough())
        {
            return operation(null!, state, cancellationToken);
        }

        return Task.FromException<TResult>(_exception);
    }

    #endregion // IExecutionStrategy
}