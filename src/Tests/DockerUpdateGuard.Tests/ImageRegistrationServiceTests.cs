using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Tests.Data;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ImageRegistrationService"/>
/// </summary>
[TestClass]
public class ImageRegistrationServiceTests
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

    /// <summary>
    /// Test repository for registration scenarios that do not need full catalog persistence behavior
    /// </summary>
    private sealed class TestImageCatalogRepository : IImageCatalogRepository
    {
        #region Fields

        private readonly ImageVersion _imageVersion;

        #endregion // Fields

        #region Constructors

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="imageVersion">Image version to return</param>
        public TestImageCatalogRepository(ImageVersion imageVersion)
        {
            _imageVersion = imageVersion;
        }

        #endregion // Constructors

        #region Methods

        /// <inheritdoc/>
        public Task<ImageVersion?> FindImageVersionAsync(string registry,
                                                         string repository,
                                                         string tag,
                                                         string? digest,
                                                         CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ImageVersion?>(_imageVersion);
        }

        /// <inheritdoc/>
        public Task<RegistryRepository> GetOrCreateRegistryRepositoryAsync(string registry,
                                                                           string repository,
                                                                           CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(_imageVersion.RegistryRepository);

            return Task.FromResult(_imageVersion.RegistryRepository);
        }

        /// <inheritdoc/>
        public Task<ImageVersion> GetOrCreateImageVersionAsync(string registry,
                                                               string repository,
                                                               string tag,
                                                               string? digest,
                                                               DateTimeOffset? publishedAtUtc = null,
                                                               string? metadataJson = null,
                                                               CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_imageVersion);
        }

        /// <inheritdoc/>
        public Task<ObservedImage> AddObservedImageAsync(ObservedImage observedImage, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(observedImage);
        }

        /// <inheritdoc/>
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        #endregion // Methods
    }

    #endregion // Methods
}