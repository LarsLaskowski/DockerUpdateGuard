using DockerUpdateGuard.Data.Repositories;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Data.Tests;

/// <summary>
/// Tests for null-digest handling in <see cref="ImageCatalogRepository"/>
/// </summary>
[TestClass]
public class ImageCatalogRepositoryNullDigestTests
{
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

                var persistedVersion = await dbContext.ImageVersions.SingleAsync(entity => entity.Id == createdVersion.Id)
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

    #endregion // Methods
}