using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.Data.Tests.Data;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace DockerUpdateGuard.Data.Tests;

/// <summary>
/// Tests for <see cref="ImageCatalogRepository"/>
/// </summary>
[TestClass]
public class ImageCatalogRepositoryTests
{
    #region Properties

    /// <summary>
    /// Context for the tests
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

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
                    await dbContext.SaveChangesAsync(TestContext.CancellationToken).ConfigureAwait(false);
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
                                await dbContext.RegistryRepositories.CountAsync(TestContext.CancellationToken).ConfigureAwait(false),
                                "Only one registry repository row must exist after deduplication");
                Assert.AreEqual(1,
                                await dbContext.ImageVersions.CountAsync(TestContext.CancellationToken).ConfigureAwait(false),
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

    /// <summary>
    /// Verify a non-unique save failure of a new image version reaches the caller unchanged
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task ImageCatalogRepositoryGetOrCreateImageVersionAsyncNonUniqueSaveFailureRethrowsOriginalExceptionAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var interceptor = new FailingSaveChangesInterceptor(typeof(ImageVersion), "22001");
            var dbContext = database.CreateDbContext([interceptor]);

            await using (dbContext.ConfigureAwait(false))
            {
                var repository = new ImageCatalogRepository(dbContext);

                var exception = await Assert.ThrowsExactlyAsync<DbUpdateException>(async () => await repository.GetOrCreateImageVersionAsync("docker.io", "library/nginx", "1.27", "sha256:123", cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
                                            .ConfigureAwait(false);

                Assert.IsNotNull(interceptor.ThrownException, "The substituted save failure must have been raised");
                Assert.AreSame(interceptor.ThrownException, exception, "The original save failure must reach the caller");
                Assert.IsEmpty(dbContext.ChangeTracker.Entries<ImageVersion>().ToList(),
                               "The context must no longer track the new entity");
            }
        }
    }

    /// <summary>
    /// Verify a non-unique save failure of a new registry repository reaches the caller unchanged
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task ImageCatalogRepositoryGetOrCreateRegistryRepositoryAsyncNonUniqueSaveFailureRethrowsOriginalExceptionAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var interceptor = new FailingSaveChangesInterceptor(typeof(RegistryRepository), "22001");
            var dbContext = database.CreateDbContext([interceptor]);

            await using (dbContext.ConfigureAwait(false))
            {
                var repository = new ImageCatalogRepository(dbContext);

                var exception = await Assert.ThrowsExactlyAsync<DbUpdateException>(async () => await repository.GetOrCreateRegistryRepositoryAsync("docker.io", "library/nginx", TestContext.CancellationToken).ConfigureAwait(false))
                                            .ConfigureAwait(false);

                Assert.IsNotNull(interceptor.ThrownException, "The substituted save failure must have been raised");
                Assert.AreSame(interceptor.ThrownException, exception, "The original save failure must reach the caller");
                Assert.IsEmpty(dbContext.ChangeTracker.Entries<RegistryRepository>().ToList(),
                               "The context must no longer track the new entity");
            }
        }
    }

    /// <summary>
    /// Verify a unique violation without a competing image version reaches the caller unchanged
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task ImageCatalogRepositoryGetOrCreateImageVersionAsyncUniqueViolationWithoutEntryRethrowsOriginalExceptionAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var interceptor = new FailingSaveChangesInterceptor(typeof(ImageVersion), PostgresErrorCodes.UniqueViolation);
            var dbContext = database.CreateDbContext([interceptor]);

            await using (dbContext.ConfigureAwait(false))
            {
                var repository = new ImageCatalogRepository(dbContext);

                var exception = await Assert.ThrowsExactlyAsync<DbUpdateException>(async () => await repository.GetOrCreateImageVersionAsync("docker.io", "library/nginx", "1.27", "sha256:123", cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
                                            .ConfigureAwait(false);

                Assert.IsNotNull(interceptor.ThrownException, "The substituted save failure must have been raised");
                Assert.AreSame(interceptor.ThrownException, exception, "The original save failure must reach the caller");
                Assert.IsEmpty(dbContext.ChangeTracker.Entries<ImageVersion>().ToList(),
                               "The context must no longer track the new entity");
            }
        }
    }

    /// <summary>
    /// Verify a unique violation without a competing registry repository reaches the caller unchanged
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task ImageCatalogRepositoryGetOrCreateRegistryRepositoryAsyncUniqueViolationWithoutEntryRethrowsOriginalExceptionAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var interceptor = new FailingSaveChangesInterceptor(typeof(RegistryRepository), PostgresErrorCodes.UniqueViolation);
            var dbContext = database.CreateDbContext([interceptor]);

            await using (dbContext.ConfigureAwait(false))
            {
                var repository = new ImageCatalogRepository(dbContext);

                var exception = await Assert.ThrowsExactlyAsync<DbUpdateException>(async () => await repository.GetOrCreateRegistryRepositoryAsync("docker.io", "library/nginx", TestContext.CancellationToken).ConfigureAwait(false))
                                            .ConfigureAwait(false);

                Assert.IsNotNull(interceptor.ThrownException, "The substituted save failure must have been raised");
                Assert.AreSame(interceptor.ThrownException, exception, "The original save failure must reach the caller");
                Assert.IsEmpty(dbContext.ChangeTracker.Entries<RegistryRepository>().ToList(),
                               "The context must no longer track the new entity");
            }
        }
    }

    /// <summary>
    /// Verify a lost insert race of an image version returns the competing writer's entry
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task ImageCatalogRepositoryGetOrCreateImageVersionAsyncLostInsertRaceReturnsCompetingVersionAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var competingId = Guid.NewGuid();
            var interceptor = new FailingSaveChangesInterceptor(typeof(ImageVersion),
                                                                PostgresErrorCodes.UniqueViolation,
                                                                async cancellationToken =>
                                                                {
                                                                    var competingContext = database.CreateDbContext();

                                                                    await using (competingContext.ConfigureAwait(false))
                                                                    {
                                                                        var registryRepository = await competingContext.RegistryRepositories
                                                                                                                       .SingleAsync(cancellationToken)
                                                                                                                       .ConfigureAwait(false);

                                                                        competingContext.ImageVersions.Add(new ImageVersion
                                                                                                           {
                                                                                                               Id = competingId,
                                                                                                               RegistryRepositoryId = registryRepository.Id,
                                                                                                               Tag = "1.27",
                                                                                                               Digest = "sha256:123"
                                                                                                           });

                                                                        await competingContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                                                                    }
                                                                });
            var dbContext = database.CreateDbContext([interceptor]);

            await using (dbContext.ConfigureAwait(false))
            {
                var repository = new ImageCatalogRepository(dbContext);

                var imageVersion = await repository.GetOrCreateImageVersionAsync("docker.io", "library/nginx", "1.27", "sha256:123", cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

                Assert.IsNotNull(interceptor.ThrownException, "The substituted save failure must have been raised");
                Assert.AreEqual(competingId, imageVersion.Id, "The competing writer's image version must be returned");
                Assert.IsNotNull(imageVersion.RegistryRepository, "The returned image version must have the registry repository populated");
                Assert.AreEqual("library/nginx", imageVersion.RegistryRepository.Repository, "The populated registry repository must be the matching one");
            }
        }
    }

    /// <summary>
    /// Verify a lost insert race of a registry repository returns the competing writer's entry
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task ImageCatalogRepositoryGetOrCreateRegistryRepositoryAsyncLostInsertRaceReturnsCompetingRepositoryAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var competingId = Guid.NewGuid();
            var interceptor = new FailingSaveChangesInterceptor(typeof(RegistryRepository),
                                                                PostgresErrorCodes.UniqueViolation,
                                                                async cancellationToken =>
                                                                {
                                                                    var competingContext = database.CreateDbContext();

                                                                    await using (competingContext.ConfigureAwait(false))
                                                                    {
                                                                        competingContext.RegistryRepositories.Add(new RegistryRepository
                                                                                                                  {
                                                                                                                      Id = competingId,
                                                                                                                      Registry = "docker.io",
                                                                                                                      Repository = "library/nginx"
                                                                                                                  });

                                                                        await competingContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                                                                    }
                                                                });
            var dbContext = database.CreateDbContext([interceptor]);

            await using (dbContext.ConfigureAwait(false))
            {
                var repository = new ImageCatalogRepository(dbContext);

                var registryRepository = await repository.GetOrCreateRegistryRepositoryAsync("docker.io", "library/nginx", TestContext.CancellationToken).ConfigureAwait(false);

                Assert.IsNotNull(interceptor.ThrownException, "The substituted save failure must have been raised");
                Assert.AreEqual(competingId, registryRepository.Id, "The competing writer's registry repository must be returned");
            }
        }
    }

    #endregion // Methods
}