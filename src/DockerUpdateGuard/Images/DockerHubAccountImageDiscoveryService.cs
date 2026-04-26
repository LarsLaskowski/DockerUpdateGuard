using System.Text.Json;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Synchronizes Docker Hub account repositories into observed images
/// </summary>
public class DockerHubAccountImageDiscoveryService : IDockerHubAccountImageDiscoveryService
{
    #region Fields

    /// <summary>
    /// Database context
    /// </summary>
    private readonly DockerUpdateGuardDbContext _dbContext;

    /// <summary>
    /// Docker Hub client
    /// </summary>
    private readonly IDockerHubClient _dockerHubClient;

    /// <summary>
    /// Image-catalog repository
    /// </summary>
    private readonly IImageCatalogRepository _imageCatalogRepository;

    /// <summary>
    /// Logger
    /// </summary>
    private readonly ILogger<DockerHubAccountImageDiscoveryService> _logger;

    /// <summary>
    /// Options monitor
    /// </summary>
    private readonly IOptionsMonitor<DockerUpdateGuardOptions> _optionsMonitor;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="dockerHubClient">Docker Hub client</param>
    /// <param name="imageCatalogRepository">Image catalog repository</param>
    /// <param name="logger">Logger</param>
    /// <param name="optionsMonitor">Options monitor</param>
    public DockerHubAccountImageDiscoveryService(DockerUpdateGuardDbContext dbContext,
                                                 IDockerHubClient dockerHubClient,
                                                 IImageCatalogRepository imageCatalogRepository,
                                                 ILogger<DockerHubAccountImageDiscoveryService> logger,
                                                 IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor)
    {
        _dbContext = dbContext;
        _dockerHubClient = dockerHubClient;
        _imageCatalogRepository = imageCatalogRepository;
        _logger = logger;
        _optionsMonitor = optionsMonitor;
    }

    #endregion // Constructors

    #region Static methods

    /// <summary>
    /// Determine whether an observed image matches one of the currently discovered repositories
    /// </summary>
    /// <param name="observedImage">Observed image</param>
    /// <param name="repositories">Discovered repository paths</param>
    /// <returns>True when the observed image belongs to a discovered repository</returns>
    private static bool MatchesRepository(ObservedImage observedImage, ISet<string> repositories)
    {
        ArgumentNullException.ThrowIfNull(observedImage);
        ArgumentNullException.ThrowIfNull(repositories);

        var repository = observedImage.CurrentImageVersion?.RegistryRepository?.Repository;

        return string.IsNullOrWhiteSpace(repository) == false
               && repositories.Contains(repository);
    }

    /// <summary>
    /// Determine whether an observed image matches the supplied repository coordinates
    /// </summary>
    /// <param name="observedImage">Observed image</param>
    /// <param name="registry">Registry name</param>
    /// <param name="repository">Repository path</param>
    /// <returns>True when the observed image belongs to the repository</returns>
    private static bool MatchesRepository(ObservedImage observedImage,
                                          string registry,
                                          string repository)
    {
        ArgumentNullException.ThrowIfNull(observedImage);
        ArgumentException.ThrowIfNullOrWhiteSpace(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);

        var existingRegistry = observedImage.CurrentImageVersion?.RegistryRepository?.Registry;
        var existingRepository = observedImage.CurrentImageVersion?.RegistryRepository?.Repository;

        return string.Equals(existingRegistry,
                             registry,
                             StringComparison.OrdinalIgnoreCase)
               && string.Equals(existingRepository,
                                repository,
                                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Select the tag that should be tracked for a repository
    /// </summary>
    /// <param name="tags">Available repository tags</param>
    /// <returns>Selected tag data or null</returns>
    private static DockerHubTagData? SelectTrackedTag(IEnumerable<DockerHubTagData> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var candidates = tags.Where(entity => string.IsNullOrWhiteSpace(entity.Tag) == false)
                             .GroupBy(entity => entity.Tag, StringComparer.OrdinalIgnoreCase)
                             .Select(group => group.OrderByDescending(entity => entity.PublishedAtUtc)
                                                   .First())
                             .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates.OrderByDescending(entity => entity.PublishedAtUtc)
                         .ThenByDescending(entity => string.Equals(entity.Tag,
                                                                   "latest",
                                                                   StringComparison.OrdinalIgnoreCase))
                         .ThenBy(entity => entity.Tag, StringComparer.OrdinalIgnoreCase)
                         .First();
    }

    /// <summary>
    /// Normalize an optional repository description
    /// </summary>
    /// <param name="description">Raw description</param>
    /// <returns>Normalized description</returns>
    private static string? NormalizeDescription(string? description)
    {
        return string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    #endregion // Static methods

    #region Methods

    /// <inheritdoc/>
    public async Task SynchronizeAccountImagesAsync(CancellationToken cancellationToken = default)
    {
        var dockerHubOptions = _optionsMonitor.CurrentValue.DockerHub;

        if (string.IsNullOrWhiteSpace(dockerHubOptions.Pat))
        {
            _logger.DockerHubAccountSynchronizationSkippedPatMissing();

            return;
        }

        if (string.IsNullOrWhiteSpace(dockerHubOptions.UserName))
        {
            _logger.DockerHubAccountSynchronizationSkippedUserNameMissing();

            return;
        }

        var accountName = dockerHubOptions.UserName.Trim();

        _logger.DockerHubAccountSynchronizationStarted(accountName);
        var repositoriesResult = await _dockerHubClient.GetRepositoriesAsync(accountName, cancellationToken)
                                                       .ConfigureAwait(false);

        if (repositoriesResult.Status != ExternalOperationStatus.Succeeded
            || repositoriesResult.Data is null)
        {
            _logger.DockerHubAccountSynchronizationAccountUnavailable(repositoriesResult.Status, repositoriesResult.Message);

            return;
        }

        var repositories = repositoriesResult.Data.Where(entity => string.IsNullOrWhiteSpace(entity.Repository) == false)
                                                  .GroupBy(entity => entity.Repository, StringComparer.OrdinalIgnoreCase)
                                                  .Select(group => group.OrderByDescending(entity => entity.LastUpdatedAtUtc)
                                                                        .First())
                                                  .ToList();
        var discoveredRepositories = repositories.Select(entity => entity.Repository)
                                                 .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingObservedImages = await _dbContext.ObservedImages
                                                     .Include(entity => entity.CurrentImageVersion)
                                                     .ThenInclude(entity => entity.RegistryRepository)
                                                     .Where(entity => entity.Source == RegistrationSource.Discovery)
                                                     .ToListAsync(cancellationToken)
                                                     .ConfigureAwait(false);
        var synchronizedImageCount = 0;
        var disabledImageCount = 0;
        var skippedRepositoryCount = 0;

        foreach (var existingObservedImage in existingObservedImages.Where(entity => MatchesRepository(entity, discoveredRepositories) == false))
        {
            if (existingObservedImage.IsEnabled)
            {
                disabledImageCount++;
            }

            existingObservedImage.IsEnabled = false;
            existingObservedImage.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        foreach (var repository in repositories)
        {
            var tagsResult = await _dockerHubClient.GetTagsAsync(repository.Registry,
                                                                 repository.Repository,
                                                                 cancellationToken)
                                                   .ConfigureAwait(false);

            if (tagsResult.Status != ExternalOperationStatus.Succeeded
                || tagsResult.Data is null)
            {
                skippedRepositoryCount++;
                _logger.DockerHubAccountSynchronizationRepositorySkipped(repository.Repository,
                                                                         tagsResult.Status,
                                                                         tagsResult.Message);

                continue;
            }

            var selectedTag = SelectTrackedTag(tagsResult.Data);

            if (selectedTag is null)
            {
                skippedRepositoryCount++;
                _logger.DockerHubAccountSynchronizationRepositorySkipped(repository.Repository,
                                                                         ExternalOperationStatus.Unknown,
                                                                         "No non-empty repository tags were returned");

                continue;
            }

            var imageVersionTask = _imageCatalogRepository.GetOrCreateImageVersionAsync(repository.Registry,
                                                                                        repository.Repository,
                                                                                        selectedTag.Tag,
                                                                                        selectedTag.Digest,
                                                                                        selectedTag.PublishedAtUtc,
                                                                                        JsonSerializer.Serialize(selectedTag),
                                                                                        cancellationToken);
            var imageVersion = await imageVersionTask.ConfigureAwait(false);
            var existingObservedImage = existingObservedImages.SingleOrDefault(entity => MatchesRepository(entity,
                                                                                                           repository.Registry,
                                                                                                           repository.Repository));

            imageVersion.Source = ImageVersionSource.ObservedImage;

            if (existingObservedImage is null)
            {
                existingObservedImage = new ObservedImage
                                        {
                                            Name = repository.Repository,
                                            Description = NormalizeDescription(repository.Description),
                                            CurrentImageVersionId = imageVersion.Id,
                                            IsEnabled = true,
                                            Source = RegistrationSource.Discovery,
                                        };

                existingObservedImages.Add(existingObservedImage);
                _dbContext.ObservedImages.Add(existingObservedImage);
            }
            else
            {
                existingObservedImage.Name = repository.Repository;
                existingObservedImage.Description = NormalizeDescription(repository.Description);
                existingObservedImage.CurrentImageVersionId = imageVersion.Id;
                existingObservedImage.IsEnabled = true;
                existingObservedImage.Source = RegistrationSource.Discovery;
                existingObservedImage.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            synchronizedImageCount++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken)
                        .ConfigureAwait(false);
        _logger.DockerHubAccountSynchronizationCompleted(accountName,
                                                         repositories.Count,
                                                         synchronizedImageCount,
                                                         disabledImageCount,
                                                         skippedRepositoryCount);
    }

    #endregion // Methods
}