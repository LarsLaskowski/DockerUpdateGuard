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
    /// Exception thrown when persistence is forced to fail
    /// </summary>
    public Exception? SaveChangesException { get; set; }

    #endregion // Properties

    #region DbContext

    /// <inheritdoc/>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (FailOnSaveChanges)
        {
            throw SaveChangesException ?? new InvalidOperationException("Simulated persistence failure");
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    #endregion // DbContext
}