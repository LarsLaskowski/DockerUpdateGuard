using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Save interceptor that fails every save writing a container action run in one of the given statuses, in whichever context and scope it happens
/// </summary>
internal sealed class ContainerActionRunWriteFailingInterceptor : SaveChangesInterceptor
{
    #region Fields

    /// <summary>
    /// Statuses whose write fails
    /// </summary>
    private readonly ContainerActionStatus[] _failingStatuses;

    /// <summary>
    /// Number of saves failed so far
    /// </summary>
    private int _failedWriteCount;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="failingStatuses">Statuses whose write fails; none lets every save pass</param>
    public ContainerActionRunWriteFailingInterceptor(params ContainerActionStatus[] failingStatuses)
    {
        _failingStatuses = failingStatuses;
    }

    #endregion // Constructors

    #region Properties

    /// <summary>
    /// Number of saves failed so far
    /// </summary>
    public int FailedWriteCount => Volatile.Read(ref _failedWriteCount);

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Throw when the context is about to write an action run in a failing status
    /// </summary>
    /// <param name="context">Context that saves</param>
    private void ThrowWhenFailingWrite(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var failing = context.ChangeTracker.Entries<ContainerActionRun>()
                                           .Any(entry => (entry.State == EntityState.Added || entry.State == EntityState.Modified)
                                                         && _failingStatuses.Contains(entry.Entity.Status));

        if (failing)
        {
            Interlocked.Increment(ref _failedWriteCount);

            throw new InvalidOperationException("Simulated action run write failure");
        }
    }

    #endregion // Methods

    #region SaveChangesInterceptor

    /// <inheritdoc/>
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ThrowWhenFailingWrite(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
                                                                          InterceptionResult<int> result,
                                                                          CancellationToken cancellationToken = default)
    {
        ThrowWhenFailingWrite(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    #endregion // SaveChangesInterceptor
}