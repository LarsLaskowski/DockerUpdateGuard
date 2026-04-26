using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.Data.Tests.Data;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Data.Tests;

/// <summary>
/// Tests for <see cref="ImageCatalogRepository"/>
/// </summary>
[TestClass]
public class ImageCatalogRepositoryTests
{
    #region Methods

    /// <summary>
    /// Verify that the unique repository constraint is enforced
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerUpdateGuardDbContextDuplicateRegistryRepositoryThrowsAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var firstRepository = new RegistryRepository
                                      {
                                          Registry = "docker.io",
                                          Repository = "library/nginx"
                                      };

                var secondRepository = new RegistryRepository
                                       {
                                           Registry = "docker.io",
                                           Repository = "library/nginx"
                                       };

                dbContext.RegistryRepositories.Add(firstRepository);
                dbContext.RegistryRepositories.Add(secondRepository);

                var duplicateConstraintViolationThrown = false;

                try
                {
                    await dbContext.SaveChangesAsync().ConfigureAwait(false);
                }
                catch (DbUpdateException)
                {
                    duplicateConstraintViolationThrown = true;
                }

                Assert.IsTrue(duplicateConstraintViolationThrown, "Duplicate registry repositories must violate the unique constraint");
            }
        }
    }

    /// <summary>
    /// Verify image version normalization is deduplicated
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageCatalogRepositoryRepeatedGetOrCreateReturnsSameImageVersionAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var repository = new ImageCatalogRepository(dbContext);

                var firstVersion = await repository.GetOrCreateImageVersionAsync("docker.io",
                                                                                 "library/nginx",
                                                                                 "1.27",
                                                                                 "sha256:123",
                                                                                 cancellationToken: CancellationToken.None)
                                                   .ConfigureAwait(false);
                var secondVersion = await repository.GetOrCreateImageVersionAsync("docker.io",
                                                                                  "library/nginx",
                                                                                  "1.27",
                                                                                  "sha256:123",
                                                                                  cancellationToken: CancellationToken.None)
                                                    .ConfigureAwait(false);

                Assert.AreEqual(firstVersion.Id,
                                secondVersion.Id,
                                "Repeated normalization requests must return the same image version");
                Assert.AreEqual(1,
                                await dbContext.RegistryRepositories.CountAsync().ConfigureAwait(false),
                                "Only one registry repository row must exist after deduplication");
                Assert.AreEqual(1,
                                await dbContext.ImageVersions.CountAsync().ConfigureAwait(false),
                                "Only one image version row must exist after deduplication");
            }
        }
    }

    /// <summary>
    /// Verify newly created image versions keep their registry repository navigation populated
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageCatalogRepositoryGetOrCreateImageVersionReturnsVersionWithRegistryRepositoryAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var repository = new ImageCatalogRepository(dbContext);

                var imageVersion = await repository.GetOrCreateImageVersionAsync("docker.io",
                                                                                 "library/redis",
                                                                                 "7.4",
                                                                                 "sha256:456",
                                                                                 cancellationToken: CancellationToken.None)
                                                   .ConfigureAwait(false);

                Assert.IsNotNull(imageVersion.RegistryRepository, "New image versions must keep the registry repository navigation populated");
                Assert.AreEqual("docker.io",
                                imageVersion.RegistryRepository.Registry,
                                "The populated registry repository must expose the normalized registry");
                Assert.AreEqual("library/redis",
                                imageVersion.RegistryRepository.Repository,
                                "The populated registry repository must expose the normalized repository path");
            }
        }
    }

    #endregion // Methods
}