using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Command interceptor that records every reader execution, synchronous and asynchronous with its token
/// </summary>
internal sealed class RecordingCommandInterceptor : DbCommandInterceptor
{
    #region Fields

    /// <summary>
    /// Guard of the recorded executions
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// Tokens of the asynchronous reader executions in execution order
    /// </summary>
    private readonly List<CancellationToken> _asynchronousTokens = [];

    /// <summary>
    /// Number of synchronous reader executions
    /// </summary>
    private int _synchronousExecutions;

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Number of synchronous reader executions so far
    /// </summary>
    public int SynchronousExecutions
    {
        get
        {
            lock (_lock)
            {
                return _synchronousExecutions;
            }
        }
    }

    /// <summary>
    /// Tokens of the asynchronous reader executions so far
    /// </summary>
    public IReadOnlyList<CancellationToken> AsynchronousTokens
    {
        get
        {
            lock (_lock)
            {
                return _asynchronousTokens.ToList();
            }
        }
    }

    #endregion // Properties

    #region DbCommandInterceptor

    /// <inheritdoc/>
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        lock (_lock)
        {
            _synchronousExecutions++;
        }

        return base.ReaderExecuting(command, eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
                                                                                     CommandEventData eventData,
                                                                                     InterceptionResult<DbDataReader> result,
                                                                                     CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _asynchronousTokens.Add(cancellationToken);
        }

        return base.ReaderExecutingAsync(command,
                                         eventData,
                                         result,
                                         cancellationToken);
    }

    #endregion // DbCommandInterceptor
}