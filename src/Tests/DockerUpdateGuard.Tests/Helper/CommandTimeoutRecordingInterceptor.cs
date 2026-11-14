using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Command interceptor that records the command timeout of every executed command and fails the first one with an exception of its own
/// </summary>
internal sealed class CommandTimeoutRecordingInterceptor : DbCommandInterceptor
{
    #region Fields

    /// <summary>
    /// Guard of the recorded timeouts
    /// </summary>
    private readonly Lock _lock = new();

    /// <summary>
    /// Command timeouts in execution order
    /// </summary>
    private readonly List<int> _commandTimeouts = [];

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Exception thrown from the first executed command
    /// </summary>
    public InvalidOperationException Failure { get; } = new("Command stopped by the recording interceptor");

    /// <summary>
    /// Command timeouts in seconds of the executed commands so far
    /// </summary>
    public IReadOnlyList<int> CommandTimeouts
    {
        get
        {
            lock (_lock)
            {
                return _commandTimeouts.ToList();
            }
        }
    }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Record the command timeout and fail the command
    /// </summary>
    /// <param name="command">Command about to be executed</param>
    private void RecordAndFail(DbCommand command)
    {
        lock (_lock)
        {
            _commandTimeouts.Add(command.CommandTimeout);
        }

        throw Failure;
    }

    #endregion // Methods

    #region DbCommandInterceptor

    /// <inheritdoc/>
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        RecordAndFail(command);

        return result;
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
                                                                                     CommandEventData eventData,
                                                                                     InterceptionResult<DbDataReader> result,
                                                                                     CancellationToken cancellationToken = default)
    {
        RecordAndFail(command);

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc/>
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        RecordAndFail(command);

        return result;
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
                                                                              CommandEventData eventData,
                                                                              InterceptionResult<int> result,
                                                                              CancellationToken cancellationToken = default)
    {
        RecordAndFail(command);

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc/>
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        RecordAndFail(command);

        return result;
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
                                                                               CommandEventData eventData,
                                                                               InterceptionResult<object> result,
                                                                               CancellationToken cancellationToken = default)
    {
        RecordAndFail(command);

        return ValueTask.FromResult(result);
    }

    #endregion // DbCommandInterceptor
}