using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.UI;

/// <summary>
/// Runtime container manual tag selection command service
/// </summary>
public class RuntimeContainerTagSelectionService : IRuntimeContainerTagSelectionService
{
    #region Fields

    /// <summary>
    /// Database context
    /// </summary>
    private readonly DockerUpdateGuardDbContext _dbContext;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="dbContext">Database context</param>
    public RuntimeContainerTagSelectionService(DockerUpdateGuardDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    #endregion // Constructors

    #region IRuntimeContainerTagSelectionService

    /// <inheritdoc/>
    public async Task SaveSelectionAsync(Guid dockerInstanceId,
                                         string containerId,
                                         string tag,
                                         string? digest,
                                         CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        var latestSnapshot = await _dbContext.ContainerSnapshots.Include(entity => entity.ImageVersion)
                                                                .ThenInclude(entity => entity.RegistryRepository)
                                                                .OrderByDescending(entity => entity.RecordedAtUtc)
                                                                .FirstOrDefaultAsync(entity => entity.DockerInstanceId == dockerInstanceId
                                                                                               && entity.ContainerId == containerId,
                                                                                     cancellationToken)
                                                                .ConfigureAwait(false);

        if (latestSnapshot is null)
        {
            throw new InvalidOperationException($"Runtime container '{dockerInstanceId}/{containerId}' was not found");
        }

        var selection = await _dbContext.RuntimeContainerTagSelections
                                        .SingleOrDefaultAsync(entity => entity.DockerInstanceId == dockerInstanceId
                                                                        && entity.ContainerId == containerId,
                                                              cancellationToken)
                                        .ConfigureAwait(false);

        if (selection is null)
        {
            selection = new RuntimeContainerTagSelection
                        {
                            DockerInstanceId = dockerInstanceId,
                            ContainerId = containerId,
                        };
            _dbContext.RuntimeContainerTagSelections.Add(selection);
        }

        selection.RegistryRepositoryId = latestSnapshot.ImageVersion.RegistryRepositoryId;
        selection.Tag = tag.Trim();
        selection.Digest = string.IsNullOrWhiteSpace(digest) ? null : digest.Trim();
        selection.SelectedAtUtc = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken)
                        .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ClearSelectionAsync(Guid dockerInstanceId, string containerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        var selection = await _dbContext.RuntimeContainerTagSelections
                                        .SingleOrDefaultAsync(entity => entity.DockerInstanceId == dockerInstanceId
                                                                        && entity.ContainerId == containerId,
                                                              cancellationToken)
                                        .ConfigureAwait(false);

        if (selection is null)
        {
            return;
        }

        _dbContext.RuntimeContainerTagSelections.Remove(selection);

        await _dbContext.SaveChangesAsync(cancellationToken)
                        .ConfigureAwait(false);
    }

    #endregion // IRuntimeContainerTagSelectionService
}