using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Tests.Data;

/// <summary>
/// Database context that holds the first persistence of an added vulnerability finding until the test releases it
/// </summary>
internal sealed class GatedSaveChangesDbContext : DockerUpdateGuardDbContext
{
    #region Fields

    /// <summary>
    /// Completed when the held persistence has been reached
    /// </summary>
    private readonly TaskCompletionSource _saveEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Completed when the test lets the held persistence continue
    /// </summary>
    private readonly TaskCompletionSource _saveReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// 1 once the gate has held a persistence
    /// </summary>
    private int _gateTaken;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="options">Context options</param>
    public GatedSaveChangesDbContext(DbContextOptions<DockerUpdateGuardDbContext> options)
        : base(options)
    {
    }

    #endregion // Constructors

    #region Properties

    /// <summary>
    /// Task that completes when the held persistence has been reached, before it touches the database
    /// </summary>
    public Task SaveEntered => _saveEntered.Task;

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Let the held persistence continue
    /// </summary>
    public void ReleaseSave()
    {
        _saveReleased.TrySetResult();
    }

    #endregion // Methods

    #region DbContext

    /// <inheritdoc/>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var addsFinding = ChangeTracker.Entries<VulnerabilityFinding>()
                                       .Any(entry => entry.State == EntityState.Added);

        if (addsFinding && Interlocked.CompareExchange(ref _gateTaken, 1, 0) == 0)
        {
            _saveEntered.TrySetResult();

            await _saveReleased.Task.ConfigureAwait(false);
        }

        return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    #endregion // DbContext
}