using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Npgsql;

namespace DockerUpdateGuard.Data.Tests.Data;

/// <summary>
/// Test-only interceptor that fails a save which holds a new entity of a configured type with a substituted PostgreSQL error
/// </summary>
internal sealed class FailingSaveChangesInterceptor : SaveChangesInterceptor
{
    #region Fields

    private readonly Type _entityType;
    private readonly string _sqlState;
    private readonly Func<CancellationToken, Task>? _competingWriter;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="entityType">Type of the added entity that triggers the failure</param>
    /// <param name="sqlState">SQLSTATE of the substituted PostgreSQL error</param>
    /// <param name="competingWriter">Optional competing insert run just before the failure</param>
    public FailingSaveChangesInterceptor(Type entityType, string sqlState, Func<CancellationToken, Task>? competingWriter = null)
    {
        _entityType = entityType;
        _sqlState = sqlState;
        _competingWriter = competingWriter;
    }

    #endregion // Constructors

    #region Properties

    /// <summary>
    /// Exception thrown by the last failed save
    /// </summary>
    public DbUpdateException? ThrownException { get; private set; }

    #endregion // Properties

    #region SaveChangesInterceptor

    /// <inheritdoc/>
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
                                                                                InterceptionResult<int> result,
                                                                                CancellationToken cancellationToken = default)
    {
        var holdsNewEntity = eventData.Context!.ChangeTracker
                                              .Entries()
                                              .Any(entry => entry.State == EntityState.Added
                                                            && entry.Entity.GetType() == _entityType);

        if (holdsNewEntity == false)
        {
            return result;
        }

        if (_competingWriter is not null)
        {
            await _competingWriter(cancellationToken).ConfigureAwait(false);
        }

        var inner = new PostgresException("substituted failure", "ERROR", "ERROR", _sqlState);

        ThrownException = new DbUpdateException("substituted save failure", inner);

        throw ThrownException;
    }

    #endregion // SaveChangesInterceptor
}