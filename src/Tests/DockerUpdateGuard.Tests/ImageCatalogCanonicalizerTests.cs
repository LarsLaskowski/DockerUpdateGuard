using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Tests.Data;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ImageCatalogCanonicalizer"/>
/// </summary>
[TestClass]
public class ImageCatalogCanonicalizerTests
{
    #region Constants

    /// <summary>
    /// Digest of the seeded versions
    /// </summary>
    private const string Digest = "sha256:abc";

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Publication time of the seeded versions
    /// </summary>
    private static readonly DateTimeOffset _publishedAtUtc = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Test context providing information about and functionality for the current test run
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Static methods

    /// <summary>
    /// Describe a version as registry, repository, tag and digest
    /// </summary>
    /// <param name="version">Version with its registry repository</param>
    /// <returns>Description</returns>
    private static string Describe(ImageVersion version)
    {
        return $"{version.RegistryRepository.Registry}/{version.RegistryRepository.Repository}:{version.Tag}@{version.Digest}";
    }

    /// <summary>
    /// Create a database context on the test database
    /// </summary>
    /// <param name="database">Test database</param>
    /// <returns>Database context</returns>
    private static DockerUpdateGuardDbContext CreateContext(SqliteTestDatabase database)
    {
        return database.CreateDbContext();
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Verify an observed image moves to a newly created canonical version with the old version's publication time and metadata
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC8")]
    public async Task ImageCatalogCanonicalizerCanonicalizeTrackedStateAsyncMovesObservedImageToCanonicalVersionAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var observedId = await SeedObservedImageAsync(database, "docker.io", "nginx", "1.27", RegistrationSource.Manual).ConfigureAwait(false);
            var oldVersionId = await LoadVersionIdAsync(database, observedId).ConfigureAwait(false);

            var moved = await RunAsync(database).ConfigureAwait(false);

            var versionId = await LoadVersionIdAsync(database, observedId).ConfigureAwait(false);
            var version = await LoadVersionAsync(database, versionId).ConfigureAwait(false);
            var oldVersion = await LoadVersionAsync(database, oldVersionId).ConfigureAwait(false);
            var observed = await LoadObservedImageAsync(database, observedId).ConfigureAwait(false);

            Assert.AreEqual(1, moved, "Exactly the one non-canonical observed image must be counted");
            Assert.AreEqual("docker.io/library/nginx:1.27@sha256:abc", Describe(version), "The observed image must point at the canonical version with the same tag and digest");
            Assert.AreEqual(_publishedAtUtc, version.PublishedAtUtc, "A new canonical version must take the publication time of the old one");
            Assert.AreEqual("{\"key\":\"my-name\"}", version.MetadataJson, "A new canonical version must take the metadata of the old one");
            Assert.AreEqual("docker.io/nginx:1.27@sha256:abc", Describe(oldVersion), "The old catalog entries must still exist");
            Assert.AreEqual("my-name", observed.Name, "Nothing but the current version of the observed image may change");
        }
    }

    /// <summary>
    /// Verify an existing canonical version is reused without changing it
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC8")]
    public async Task ImageCatalogCanonicalizerCanonicalizeTrackedStateAsyncReusesExistingCanonicalVersionAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var canonicalId = await SeedObservedImageAsync(database, "docker.io", "library/nginx", "1.27", RegistrationSource.Discovery, "other").ConfigureAwait(false);
            var canonicalVersionId = await LoadVersionIdAsync(database, canonicalId).ConfigureAwait(false);
            var observedId = await SeedObservedImageAsync(database, "docker.io", "nginx", "1.27", RegistrationSource.Manual).ConfigureAwait(false);

            var moved = await RunAsync(database).ConfigureAwait(false);

            var versionId = await LoadVersionIdAsync(database, observedId).ConfigureAwait(false);
            var versions = await LoadVersionsAsync(database).ConfigureAwait(false);
            var canonicalVersion = versions.Single(version => version.Id == canonicalVersionId);

            Assert.AreEqual(1, moved, "The observed image must be counted as moved");
            Assert.AreEqual(canonicalVersionId, versionId, "The observed image must point at the existing canonical version");
            Assert.HasCount(2, versions, "No further version may be created beside the old and the canonical one");
            Assert.AreEqual("{\"key\":\"other\"}", canonicalVersion.MetadataJson, "The existing canonical version must stay unchanged");
        }
    }

    /// <summary>
    /// Verify a tag selection moves to the canonical registry repository
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC8")]
    public async Task ImageCatalogCanonicalizerCanonicalizeTrackedStateAsyncMovesTagSelectionToCanonicalRepositoryAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var selectionId = await SeedTagSelectionAsync(database, "index.docker.io", "company/api").ConfigureAwait(false);

            var moved = await RunAsync(database).ConfigureAwait(false);

            var selection = await LoadTagSelectionAsync(database, selectionId).ConfigureAwait(false);

            Assert.AreEqual(1, moved, "The tag selection must be counted as moved");
            Assert.AreEqual("docker.io/company/api", $"{selection.RegistryRepository.Registry}/{selection.RegistryRepository.Repository}", "The selection must point at the canonical repository");
            Assert.AreEqual("1.0", selection.Tag, "The selected tag must stay unchanged");
            Assert.AreEqual(Digest, selection.Digest, "The selected digest must stay unchanged");
        }
    }

    /// <summary>
    /// Verify a second run returns 0 and changes no row
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC8")]
    public async Task ImageCatalogCanonicalizerCanonicalizeTrackedStateAsyncSecondRunChangesNothingAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var observedId = await SeedObservedImageAsync(database, "index.docker.io", "nginx", "1.27", RegistrationSource.Manual).ConfigureAwait(false);
            var selectionId = await SeedTagSelectionAsync(database, "registry-1.docker.io", "company/api").ConfigureAwait(false);

            var firstRun = await RunAsync(database).ConfigureAwait(false);

            var stateAfterFirstRun = await DescribeStateAsync(database, observedId, selectionId).ConfigureAwait(false);

            var secondRun = await RunAsync(database).ConfigureAwait(false);

            var stateAfterSecondRun = await DescribeStateAsync(database, observedId, selectionId).ConfigureAwait(false);

            Assert.AreEqual(2, firstRun, "The first run must move the observed image and the tag selection");
            Assert.AreEqual(0, secondRun, "A second run must move nothing");
            Assert.AreEqual(stateAfterFirstRun, stateAfterSecondRun, "A second run must change no row");
        }
    }

    /// <summary>
    /// Verify rows of other registries and canonical docker.io rows stay unchanged
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC8")]
    public async Task ImageCatalogCanonicalizerCanonicalizeTrackedStateAsyncKeepsOtherRegistriesAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var ghcrId = await SeedObservedImageAsync(database, "ghcr.io", "owner/app", "1", RegistrationSource.Manual, "ghcr").ConfigureAwait(false);
            var hubId = await SeedObservedImageAsync(database, "docker.io", "library/nginx", "1", RegistrationSource.Manual, "hub").ConfigureAwait(false);
            var namespacedId = await SeedObservedImageAsync(database, "docker.io", "myuser/app", "1", RegistrationSource.Manual, "namespaced").ConfigureAwait(false);
            var versionIds = await LoadVersionIdsAsync(database, ghcrId, hubId, namespacedId).ConfigureAwait(false);
            var versionCount = (await LoadVersionsAsync(database).ConfigureAwait(false)).Count;

            var moved = await RunAsync(database).ConfigureAwait(false);

            var versionIdsAfter = await LoadVersionIdsAsync(database, ghcrId, hubId, namespacedId).ConfigureAwait(false);
            var versionCountAfter = (await LoadVersionsAsync(database).ConfigureAwait(false)).Count;

            Assert.AreEqual(0, moved, "Rows that are already canonical must not be counted");
            Assert.AreSequenceEqual(versionIds, versionIdsAfter, "No observed image of another registry or of a canonical docker.io repository may move");
            Assert.AreEqual(versionCount, versionCountAfter, "No catalog entry may be created for rows that stay");
        }
    }

    /// <summary>
    /// Verify a row stays unchanged when its canonical repository fails the reference rules
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC8")]
    public async Task ImageCatalogCanonicalizerCanonicalizeTrackedStateAsyncKeepsRowWithInvalidCanonicalRepositoryAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var repository = new string('a', 250);
            var observedId = await SeedObservedImageAsync(database, "docker.io", repository, "1", RegistrationSource.Manual).ConfigureAwait(false);
            var versionId = await LoadVersionIdAsync(database, observedId).ConfigureAwait(false);

            var moved = await RunAsync(database).ConfigureAwait(false);

            var versionIdAfter = await LoadVersionIdAsync(database, observedId).ConfigureAwait(false);
            var repositories = await LoadRepositoriesAsync(database).ConfigureAwait(false);

            Assert.AreEqual(0, moved, "A row whose canonical repository is too long must not be counted");
            Assert.AreEqual(versionId, versionIdAfter, "A row whose canonical repository is too long must stay on its version");
            Assert.AreSequenceEqual(new[] { $"docker.io/{repository}" }, repositories, "No catalog entry may be created for the invalid canonical repository");
        }
    }

    /// <summary>
    /// Verify a row stays unchanged when its canonical registry fails the reference rules
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC8")]
    public async Task ImageCatalogCanonicalizerCanonicalizeTrackedStateAsyncKeepsRowWithInvalidCanonicalRegistryAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var repository = $"harbor.{new string('a', 200)}.com/app";
            var observedId = await SeedObservedImageAsync(database, "index.docker.io", repository, "1", RegistrationSource.Manual).ConfigureAwait(false);
            var versionId = await LoadVersionIdAsync(database, observedId).ConfigureAwait(false);

            var moved = await RunAsync(database).ConfigureAwait(false);

            var versionIdAfter = await LoadVersionIdAsync(database, observedId).ConfigureAwait(false);
            var repositories = await LoadRepositoriesAsync(database).ConfigureAwait(false);

            Assert.AreEqual(0, moved, "A row whose canonical registry is too long must not be counted");
            Assert.AreEqual(versionId, versionIdAfter, "A row whose canonical registry is too long must stay on its version");
            Assert.AreSequenceEqual(new[] { $"index.docker.io/{repository}" }, repositories, "No catalog entry may be created for the invalid canonical registry");
        }
    }

    /// <summary>
    /// Verify a stored wrapped registry row moves to the unwrapped registry
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC8")]
    public async Task ImageCatalogCanonicalizerCanonicalizeTrackedStateAsyncMovesWrappedRegistryRowToUnwrappedRegistryAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var observedId = await SeedObservedImageAsync(database, "index.docker.io", "ghcr.io/owner/app", "1", RegistrationSource.Manual).ConfigureAwait(false);

            var moved = await RunAsync(database).ConfigureAwait(false);

            var versionId = await LoadVersionIdAsync(database, observedId).ConfigureAwait(false);
            var version = await LoadVersionAsync(database, versionId).ConfigureAwait(false);

            Assert.AreEqual(1, moved, "The wrapped row must be counted as moved");
            Assert.AreEqual("ghcr.io/owner/app:1@sha256:abc", Describe(version), "The wrapped row must move to the unwrapped registry");
        }
    }

    /// <summary>
    /// Verify two manual observed images end on one canonical version and both are kept
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC8")]
    public async Task ImageCatalogCanonicalizerCanonicalizeTrackedStateAsyncKeepsBothManualImagesOnSharedVersionAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var firstId = await SeedObservedImageAsync(database, "docker.io", "nginx", "1.27", RegistrationSource.Manual, "first").ConfigureAwait(false);
            var secondId = await SeedObservedImageAsync(database, "index.docker.io", "library/nginx", "1.27", RegistrationSource.Manual, "second").ConfigureAwait(false);

            var moved = await RunAsync(database).ConfigureAwait(false);

            var firstVersionId = await LoadVersionIdAsync(database, firstId).ConfigureAwait(false);
            var secondVersionId = await LoadVersionIdAsync(database, secondId).ConfigureAwait(false);
            var version = await LoadVersionAsync(database, firstVersionId).ConfigureAwait(false);

            Assert.AreEqual(2, moved, "Both observed images must be counted as moved");
            Assert.AreEqual(firstVersionId, secondVersionId, "Both observed images must share one canonical version");
            Assert.AreEqual("docker.io/library/nginx:1.27@sha256:abc", Describe(version), "The shared version must be the canonical one");
            Assert.HasCount(2, await LoadObservedImagesAsync(database).ConfigureAwait(false), "No observed image may be deleted");
        }
    }

    /// <summary>
    /// Verify a manual observed image moves onto the version of an enabled discovered image and leaves that image unchanged
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC8")]
    public async Task ImageCatalogCanonicalizerCanonicalizeTrackedStateAsyncMovesManualImageOntoDiscoveredImageVersionAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var discoveredId = await SeedObservedImageAsync(database, "docker.io", "me/app", "1", RegistrationSource.Discovery, "discovered").ConfigureAwait(false);
            var manualId = await SeedObservedImageAsync(database, "index.docker.io", "me/app", "1", RegistrationSource.Manual, "manual").ConfigureAwait(false);
            var discoveredVersionId = await LoadVersionIdAsync(database, discoveredId).ConfigureAwait(false);

            var moved = await RunAsync(database).ConfigureAwait(false);

            var manualVersionId = await LoadVersionIdAsync(database, manualId).ConfigureAwait(false);
            var discoveredVersionIdAfter = await LoadVersionIdAsync(database, discoveredId).ConfigureAwait(false);
            var discovered = await LoadObservedImageAsync(database, discoveredId).ConfigureAwait(false);

            Assert.AreEqual(1, moved, "Only the manual observed image must be counted as moved");
            Assert.AreEqual(discoveredVersionId, manualVersionId, "The manual observed image must move onto the version of the discovered image");
            Assert.AreEqual(discoveredVersionId, discoveredVersionIdAfter, "The discovered image must keep its version");
            Assert.IsTrue(discovered.IsEnabled, "The discovered image must stay enabled");
        }
    }

    /// <summary>
    /// Seed an observed image with its version and registry repository, reusing a registry repository that exists
    /// </summary>
    /// <param name="database">Test database</param>
    /// <param name="registry">Registry</param>
    /// <param name="repository">Repository</param>
    /// <param name="tag">Tag</param>
    /// <param name="source">Registration source</param>
    /// <param name="name">Name of the observed image, also the metadata value</param>
    /// <returns>Id of the observed image</returns>
    private async Task<Guid> SeedObservedImageAsync(SqliteTestDatabase database, string registry, string repository, string tag, RegistrationSource source, string name = "my-name")
    {
        var dbContext = CreateContext(database);

        await using (dbContext.ConfigureAwait(false))
        {
            var registryRepository = await FindOrAddRepositoryAsync(dbContext, registry, repository).ConfigureAwait(false);
            var version = new ImageVersion
                          {
                              RegistryRepository = registryRepository,
                              Tag = tag,
                              Digest = Digest,
                              PublishedAtUtc = _publishedAtUtc,
                              MetadataJson = $"{{\"key\":\"{name}\"}}",
                          };
            var observedImage = new ObservedImage
                                {
                                    Name = name,
                                    Source = source,
                                    CurrentImageVersion = version,
                                };

            dbContext.ObservedImages.Add(observedImage);

            await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                           .ConfigureAwait(false);

            return observedImage.Id;
        }
    }

    /// <summary>
    /// Seed a tag selection of a container on a registry repository
    /// </summary>
    /// <param name="database">Test database</param>
    /// <param name="registry">Registry</param>
    /// <param name="repository">Repository</param>
    /// <returns>Id of the tag selection</returns>
    private async Task<Guid> SeedTagSelectionAsync(SqliteTestDatabase database, string registry, string repository)
    {
        var dbContext = CreateContext(database);

        await using (dbContext.ConfigureAwait(false))
        {
            var instance = new DockerInstance
                           {
                               Name = "Production",
                               EndpointUri = "https://docker.example.test",
                               ConnectionKind = DockerConnectionKind.Https,
                           };
            var selection = new RuntimeContainerTagSelection
                            {
                                DockerInstance = instance,
                                RegistryRepository = new RegistryRepository
                                                     {
                                                         Registry = registry,
                                                         Repository = repository,
                                                     },
                                ContainerId = "container-1",
                                Tag = "1.0",
                                Digest = Digest,
                            };

            dbContext.RuntimeContainerTagSelections.Add(selection);

            await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                           .ConfigureAwait(false);

            return selection.Id;
        }
    }

    /// <summary>
    /// Find a registry repository or add it to the context
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="registry">Registry</param>
    /// <param name="repository">Repository</param>
    /// <returns>Registry repository</returns>
    private async Task<RegistryRepository> FindOrAddRepositoryAsync(DockerUpdateGuardDbContext dbContext, string registry, string repository)
    {
        var existing = await dbContext.RegistryRepositories
                                      .SingleOrDefaultAsync(entity => entity.Registry == registry
                                                                      && entity.Repository == repository,
                                                            TestContext.CancellationToken)
                                      .ConfigureAwait(false);

        return existing ?? new RegistryRepository
                           {
                               Registry = registry,
                               Repository = repository,
                           };
    }

    /// <summary>
    /// Run the canonicalization on a fresh context
    /// </summary>
    /// <param name="database">Test database</param>
    /// <returns>Number of moved rows</returns>
    private async Task<int> RunAsync(SqliteTestDatabase database)
    {
        var dbContext = CreateContext(database);

        await using (dbContext.ConfigureAwait(false))
        {
            return await ImageCatalogCanonicalizer.CanonicalizeTrackedStateAsync(dbContext, TestContext.CancellationToken)
                                                  .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Load an observed image
    /// </summary>
    /// <param name="database">Test database</param>
    /// <param name="observedId">Id of the observed image</param>
    /// <returns>Observed image</returns>
    private async Task<ObservedImage> LoadObservedImageAsync(SqliteTestDatabase database, Guid observedId)
    {
        var dbContext = CreateContext(database);

        await using (dbContext.ConfigureAwait(false))
        {
            return await dbContext.ObservedImages
                                  .AsNoTracking()
                                  .SingleAsync(entity => entity.Id == observedId, TestContext.CancellationToken)
                                  .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Load every observed image
    /// </summary>
    /// <param name="database">Test database</param>
    /// <returns>Observed images</returns>
    private async Task<List<ObservedImage>> LoadObservedImagesAsync(SqliteTestDatabase database)
    {
        var dbContext = CreateContext(database);

        await using (dbContext.ConfigureAwait(false))
        {
            return await dbContext.ObservedImages
                                  .AsNoTracking()
                                  .ToListAsync(TestContext.CancellationToken)
                                  .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Load the current version id of an observed image
    /// </summary>
    /// <param name="database">Test database</param>
    /// <param name="observedId">Id of the observed image</param>
    /// <returns>Version id</returns>
    private async Task<Guid> LoadVersionIdAsync(SqliteTestDatabase database, Guid observedId)
    {
        var observedImage = await LoadObservedImageAsync(database, observedId).ConfigureAwait(false);

        return observedImage.CurrentImageVersionId;
    }

    /// <summary>
    /// Load the current version ids of several observed images
    /// </summary>
    /// <param name="database">Test database</param>
    /// <param name="observedIds">Ids of the observed images</param>
    /// <returns>Version ids in the order of the observed images</returns>
    private async Task<List<Guid>> LoadVersionIdsAsync(SqliteTestDatabase database, params Guid[] observedIds)
    {
        var versionIds = new List<Guid>();

        foreach (var observedId in observedIds)
        {
            versionIds.Add(await LoadVersionIdAsync(database, observedId).ConfigureAwait(false));
        }

        return versionIds;
    }

    /// <summary>
    /// Load every version with its registry repository
    /// </summary>
    /// <param name="database">Test database</param>
    /// <returns>Versions</returns>
    private async Task<List<ImageVersion>> LoadVersionsAsync(SqliteTestDatabase database)
    {
        var dbContext = CreateContext(database);

        await using (dbContext.ConfigureAwait(false))
        {
            return await dbContext.ImageVersions
                                  .AsNoTracking()
                                  .Include(entity => entity.RegistryRepository)
                                  .ToListAsync(TestContext.CancellationToken)
                                  .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Load a version with its registry repository
    /// </summary>
    /// <param name="database">Test database</param>
    /// <param name="versionId">Version id</param>
    /// <returns>Version</returns>
    private async Task<ImageVersion> LoadVersionAsync(SqliteTestDatabase database, Guid versionId)
    {
        var versions = await LoadVersionsAsync(database).ConfigureAwait(false);

        return versions.Single(version => version.Id == versionId);
    }

    /// <summary>
    /// Load every registry repository as registry and repository
    /// </summary>
    /// <param name="database">Test database</param>
    /// <returns>Registry repositories</returns>
    private async Task<List<string>> LoadRepositoriesAsync(SqliteTestDatabase database)
    {
        var dbContext = CreateContext(database);

        await using (dbContext.ConfigureAwait(false))
        {
            var repositories = await dbContext.RegistryRepositories
                                              .AsNoTracking()
                                              .ToListAsync(TestContext.CancellationToken)
                                              .ConfigureAwait(false);

            return repositories.Select(entity => $"{entity.Registry}/{entity.Repository}")
                               .ToList();
        }
    }

    /// <summary>
    /// Load a tag selection with its registry repository
    /// </summary>
    /// <param name="database">Test database</param>
    /// <param name="selectionId">Id of the tag selection</param>
    /// <returns>Tag selection</returns>
    private async Task<RuntimeContainerTagSelection> LoadTagSelectionAsync(SqliteTestDatabase database, Guid selectionId)
    {
        var dbContext = CreateContext(database);

        await using (dbContext.ConfigureAwait(false))
        {
            return await dbContext.RuntimeContainerTagSelections
                                  .AsNoTracking()
                                  .Include(entity => entity.RegistryRepository)
                                  .SingleAsync(entity => entity.Id == selectionId, TestContext.CancellationToken)
                                  .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Describe the state of the catalog, an observed image and a tag selection
    /// </summary>
    /// <param name="database">Test database</param>
    /// <param name="observedId">Id of the observed image</param>
    /// <param name="selectionId">Id of the tag selection</param>
    /// <returns>Description</returns>
    private async Task<string> DescribeStateAsync(SqliteTestDatabase database, Guid observedId, Guid selectionId)
    {
        var versionId = await LoadVersionIdAsync(database, observedId).ConfigureAwait(false);
        var selection = await LoadTagSelectionAsync(database, selectionId).ConfigureAwait(false);
        var repositories = await LoadRepositoriesAsync(database).ConfigureAwait(false);
        var versions = await LoadVersionsAsync(database).ConfigureAwait(false);

        return $"{versionId}|{selection.RegistryRepositoryId}|{string.Join(",", repositories.Order())}|{string.Join(",", versions.Select(Describe).Order())}";
    }

    #endregion // Methods
}