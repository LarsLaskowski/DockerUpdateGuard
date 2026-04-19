using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Queries;

namespace DockerUpdateGuard.Data.Tests;

/// <summary>
/// Tests for <see cref="SharedBaseImageQueryService"/>
/// </summary>
[TestClass]
public class SharedBaseImageQueryServiceTests
{
    #region Methods

    /// <summary>
    /// Verify shared base image queries return the expected observed images
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task SharedBaseImageQueryServiceSharedBaseReturnsObservedImagesAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var sharedBaseRepository = new RegistryRepository
                                           {
                                               Registry = "docker.io",
                                               Repository = "library/debian"
                                           };

                var sharedBaseImage = new ImageVersion
                                      {
                                          RegistryRepository = sharedBaseRepository,
                                          Tag = "12-slim",
                                          Digest = "sha256:base"
                                      };

                var appImageARepository = new RegistryRepository
                                          {
                                              Registry = "docker.io",
                                              Repository = "company/app-a"
                                          };

                var appImageA = new ImageVersion
                                {
                                    RegistryRepository = appImageARepository,
                                    Tag = "1.0.0",
                                    Digest = "sha256:a"
                                };

                var appImageBRepository = new RegistryRepository
                                          {
                                              Registry = "docker.io",
                                              Repository = "company/app-b"
                                          };

                var appImageB = new ImageVersion
                                {
                                    RegistryRepository = appImageBRepository,
                                    Tag = "2.0.0",
                                    Digest = "sha256:b"
                                };

                var observedImageA = new ObservedImage
                                     {
                                         Name = "App A",
                                         CurrentImageVersion = appImageA
                                     };

                var observedImageB = new ObservedImage
                                     {
                                         Name = "App B",
                                         CurrentImageVersion = appImageB
                                     };

                dbContext.AddRange(sharedBaseImage,
                                   appImageA,
                                   appImageB,
                                   observedImageA,
                                   observedImageB);

                var relationshipA = new ImageRelationship
                                    {
                                        ChildImageVersion = appImageA,
                                        BaseImageVersion = sharedBaseImage,
                                        RelationshipType = ImageRelationshipType.BaseImage,
                                        Depth = 1
                                    };

                var relationshipB = new ImageRelationship
                                    {
                                        ChildImageVersion = appImageB,
                                        BaseImageVersion = sharedBaseImage,
                                        RelationshipType = ImageRelationshipType.BaseImage,
                                        Depth = 1
                                    };

                dbContext.ImageRelationships.AddRange(relationshipA, relationshipB);

                await dbContext.SaveChangesAsync().ConfigureAwait(false);

                var queryService = new SharedBaseImageQueryService(dbContext);

                var sharedBaseImages = await queryService.GetSharedBaseImagesAsync().ConfigureAwait(false);
                var observedImages = await queryService.GetObservedImagesByBaseImageAsync(sharedBaseImage.Id).ConfigureAwait(false);

                Assert.HasCount(1,
                                sharedBaseImages,
                                "Exactly one shared base image must be returned");
                Assert.AreEqual(sharedBaseImage.Id,
                                sharedBaseImages[0].BaseImageVersionId,
                                "The shared base image result must point to the seeded base image");
                Assert.AreEqual(2,
                                sharedBaseImages[0].ObservedImageCount,
                                "The shared base image must be used by two observed images");

                CollectionAssert.AreEquivalent(new[] { "App A", "App B" },
                                               observedImages.Select(entity => entity.ObservedImageName).ToArray(),
                                               "The query must return both observed images that share the base image");
            }
        }
    }

    #endregion // Methods
}