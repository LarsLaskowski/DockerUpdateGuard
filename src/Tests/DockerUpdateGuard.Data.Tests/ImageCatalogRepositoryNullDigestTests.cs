using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.Data.Tests.Data;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Data.Tests;

/// <summary>
/// Tests for null-digest handling in <see cref="ImageCatalogRepository"/>
/// </summary>
[TestClass]
public class ImageCatalogRepositoryNullDigestTests
{
    #region Properties

    /// <summary>
    /// Context for the tests
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify image versions without a digest can be created and found again
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageCatalogRepositoryGetOrCreateImageVersionAsyncWithoutDigestPersistsAndFindsVersionAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var repository = new ImageCatalogRepository(dbContext);

                var createdVersion = await repository.GetOrCreateImageVersionAsync("docker.io",
                                                                                   "library/busybox",
                                                                                   "latest",
                                                                                   digest: null,
                                                                                   cancellationToken: CancellationToken.None)
                                                     .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();

                var persistedVersion = await dbContext.ImageVersions.SingleAsync(entity => entity.Id == createdVersion.Id, TestContext.CancellationToken)
                                                                    .ConfigureAwait(false);
                var foundVersion = await repository.FindImageVersionAsync("docker.io",
                                                                          "library/busybox",
                                                                          "latest",
                                                                          digest: null,
                                                                          cancellationToken: CancellationToken.None)
                                                   .ConfigureAwait(false);

                Assert.IsNotNull(foundVersion, "Image versions without a digest must be found again through the repository");
                Assert.AreEqual(createdVersion.Id,
                                foundVersion.Id,
                                "Image versions without a digest must round-trip to the same image version");
                Assert.IsNull(persistedVersion.Digest, "Persisted image versions without a digest must materialize back as null");
            }
        }
    }

    /// <summary>
    /// Verify image versions strip an embedded image reference from the persisted digest value
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageCatalogRepositoryGetOrCreateImageVersionAsyncNormalizesDigestReferenceAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var repository = new ImageCatalogRepository(dbContext);

                var createdVersion = await repository.GetOrCreateImageVersionAsync("docker.io",
                                                                                   "library/debian",
                                                                                   "12",
                                                                                   "docker.io/library/debian@sha256:base",
                                                                                   cancellationToken: CancellationToken.None)
                                                     .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();

                var persistedVersion = await dbContext.ImageVersions.SingleAsync(entity => entity.Id == createdVersion.Id, TestContext.CancellationToken)
                                                                    .ConfigureAwait(false);
                var foundVersion = await repository.FindImageVersionAsync("docker.io",
                                                                          "library/debian",
                                                                          "12",
                                                                          "sha256:base",
                                                                          cancellationToken: CancellationToken.None)
                                                   .ConfigureAwait(false);

                Assert.AreEqual("sha256:base",
                                persistedVersion.Digest,
                                "Persisted image versions must store only the digest portion when a full reference is supplied");
                Assert.IsNotNull(foundVersion, "Normalized digest lookups must find image versions created from embedded digest references");
                Assert.AreEqual(createdVersion.Id,
                                foundVersion.Id,
                                "Normalized digest lookups must resolve to the created image version");
            }
        }
    }

    #endregion // Methods
}