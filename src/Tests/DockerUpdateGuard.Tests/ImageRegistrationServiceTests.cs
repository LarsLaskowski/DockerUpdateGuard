using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ImageRegistrationService"/>
/// </summary>
[TestClass]
public partial class ImageRegistrationServiceTests
{
    #region Methods

    /// <summary>
    /// Verify image registration persists a new observed image without concurrent DbContext access
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageRegistrationServiceRegisterAsyncPersistsObservedImageAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var registryRepository = new RegistryRepository
                                         {
                                             Registry = "docker.io",
                                             Repository = "company/api",
                                         };
                var imageVersion = new ImageVersion
                                   {
                                       RegistryRepository = registryRepository,
                                       Tag = "1.0.0",
                                       Digest = "sha256:api",
                                   };

                dbContext.RegistryRepositories.Add(registryRepository);
                dbContext.ImageVersions.Add(imageVersion);
                await dbContext.SaveChangesAsync(CancellationToken.None)
                               .ConfigureAwait(false);

                var service = new ImageRegistrationService(dbContext,
                                                           new TestImageCatalogRepository(imageVersion),
                                                           new ImageReferenceParser());
                var request = new ObservedImageRegistrationRequest
                              {
                                  Name = "Company API",
                                  Description = "Production API image",
                                  ImageReference = "docker.io/company/api:1.0.0",
                              };

                var observedImageTask = service.RegisterAsync(request, CancellationToken.None);
                var observedImage = await observedImageTask.ConfigureAwait(false);
                var persistedImageTask = dbContext.ObservedImages.SingleAsync(entity => entity.Id == observedImage.Id, CancellationToken.None);
                var persistedImage = await persistedImageTask.ConfigureAwait(false);

                Assert.AreEqual("Company API",
                                persistedImage.Name,
                                "Registering an image must persist the observed image name");
                Assert.AreEqual("Production API image",
                                persistedImage.Description,
                                "Registering an image must persist the observed image description");
                Assert.AreNotEqual(Guid.Empty,
                                   persistedImage.CurrentImageVersionId,
                                   "Registering an image must associate the observed image with a current image version");

                var imageVersionCountTask = dbContext.ImageVersions.CountAsync(CancellationToken.None);
                var imageVersionCount = await imageVersionCountTask.ConfigureAwait(false);

                Assert.AreEqual(1,
                                imageVersionCount,
                                "Registering an image must create exactly one image version for the supplied reference");
            }
        }
    }

    #endregion // Methods
}