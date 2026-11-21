using DockerUpdateGuard.Data;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Moves tracked image state onto canonical catalog entries
/// </summary>
public static class ImageCatalogCanonicalizer
{
    #region Static methods

    /// <summary>
    /// Move tracked image references to their canonical catalog entries
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of moved tracked image references</returns>
    public static async Task<int> CanonicalizeTrackedStateAsync(DockerUpdateGuardDbContext dbContext, CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        throw new InvalidOperationException("Skeleton: not implemented");
    }

    #endregion // Static methods
}