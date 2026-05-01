using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Queries;
using DockerUpdateGuard.Data.Tests.Data;

namespace DockerUpdateGuard.Data.Tests;

/// <summary>
/// Tests for <see cref="SharedBaseImageQueryService"/>
/// </summary>
[TestClass]
public class SharedBaseImageQueryServiceTests
{
    #region Properties

    /// <summary>
    /// Context for the tests
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify base image queries return the expected observed images
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task SharedBaseImageQueryServiceBaseImagesReturnsObservedImagesAsync()
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

                await dbContext.SaveChangesAsync(TestContext.CancellationToken).ConfigureAwait(false);

                var queryService = new SharedBaseImageQueryService(dbContext);

                var sharedBaseImages = await queryService.GetBaseImagesAsync(TestContext.CancellationToken).ConfigureAwait(false);
                var observedImages = await queryService.GetObservedImagesByBaseImageAsync(sharedBaseImage.Id, TestContext.CancellationToken).ConfigureAwait(false);

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

    /// <summary>
    /// Verify unresolved same-name base images are separated by source reference
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task SharedBaseImageQueryServiceBaseImagesWithoutDigestKeepSeparateSourceGroupsAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var unresolvedBaseRepository = new RegistryRepository
                                               {
                                                   Registry = "docker.io",
                                                   Repository = "library/python"
                                               };
                var unresolvedBaseImage = new ImageVersion
                                          {
                                              RegistryRepository = unresolvedBaseRepository,
                                              Tag = "3.12-alpine",
                                              Digest = string.Empty
                                          };
                var parentRepositoryA = new RegistryRepository
                                        {
                                            Registry = "docker.io",
                                            Repository = "company/api-a"
                                        };
                var parentRepositoryB = new RegistryRepository
                                        {
                                            Registry = "docker.io",
                                            Repository = "company/api-b"
                                        };
                var parentImageA = new ImageVersion
                                   {
                                       RegistryRepository = parentRepositoryA,
                                       Tag = "1.0.0",
                                       Digest = "sha256:parent-a"
                                   };
                var parentImageB = new ImageVersion
                                   {
                                       RegistryRepository = parentRepositoryB,
                                       Tag = "2.0.0",
                                       Digest = "sha256:parent-b"
                                   };

                dbContext.AddRange(unresolvedBaseImage, parentImageA, parentImageB);
                dbContext.ObservedImages.AddRange(new ObservedImage
                                                  {
                                                      Name = "API A",
                                                      CurrentImageVersion = parentImageA
                                                  },
                                                  new ObservedImage
                                                  {
                                                      Name = "API B",
                                                      CurrentImageVersion = parentImageB
                                                  });
                dbContext.ImageRelationships.AddRange(new ImageRelationship
                                                      {
                                                          ChildImageVersion = parentImageA,
                                                          BaseImageVersion = unresolvedBaseImage,
                                                          RelationshipType = ImageRelationshipType.BaseImage,
                                                          Depth = 1,
                                                          SourceReference = "docker.io/company/api-a:1.0.0@sha256:parent-a"
                                                      },
                                                      new ImageRelationship
                                                      {
                                                          ChildImageVersion = parentImageB,
                                                          BaseImageVersion = unresolvedBaseImage,
                                                          RelationshipType = ImageRelationshipType.BaseImage,
                                                          Depth = 1,
                                                          SourceReference = "docker.io/company/api-b:2.0.0@sha256:parent-b"
                                                      });

                await dbContext.SaveChangesAsync(TestContext.CancellationToken).ConfigureAwait(false);

                var queryService = new SharedBaseImageQueryService(dbContext);

                var baseImages = await queryService.GetBaseImagesAsync(TestContext.CancellationToken).ConfigureAwait(false);

                Assert.AreEqual(2,
                                baseImages.Count,
                                "Unresolved base images with the same name must remain separable by source reference");
                CollectionAssert.AreEquivalent(new[]
                                               {
                                                   "docker.io/company/api-a:1.0.0@sha256:parent-a",
                                                   "docker.io/company/api-b:2.0.0@sha256:parent-b",
                                               },
                                               baseImages.SelectMany(entity => entity.SourceReferences).ToArray(),
                                               "Each unresolved base image entry must keep the source reference that disambiguates it");
            }
        }
    }

    #endregion // Methods
}