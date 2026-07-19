using DockerUpdateGuard.Data;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Tests.Data;

/// <summary>
/// Database context that can be forced to fail when persisting changes
/// </summary>
internal sealed class SaveChangesFailingDbContext : DockerUpdateGuardDbContext
{
    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="options">Context options</param>
    public SaveChangesFailingDbContext(DbContextOptions<DockerUpdateGuardDbContext> options)
        : base(options)
    {
    }

    #endregion // Constructors

    #region Properties

    /// <summary>
    /// Whether the next persistence attempt throws
    /// </summary>
    public bool FailOnSaveChanges { get; set; }

    /// <summary>
    /// Number of persistence attempts that succeed before subsequent attempts throw
    /// </summary>
    public int? FailAfterSaveCount { get; set; }

    /// <summary>
    /// Exception thrown when persistence is forced to fail
    /// </summary>
    public Exception? SaveChangesException { get; set; }

    /// <summary>
    /// Number of persistence attempts that have been made
    /// </summary>
    public int SaveChangesAttemptCount { get; private set; }

    #endregion // Properties

    #region DbContext

    /// <inheritdoc/>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesAttemptCount++;

        if (FailOnSaveChanges || (FailAfterSaveCount.HasValue && SaveChangesAttemptCount > FailAfterSaveCount.Value))
        {
            throw SaveChangesException ?? new InvalidOperationException("Simulated persistence failure");
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    #endregion // DbContext
}