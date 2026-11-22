using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

using Microsoft.EntityFrameworkCore;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ImageRegistrationService"/>
/// </summary>
[TestClass]
public partial class ImageRegistrationServiceTests
{
    #region Constants

    /// <summary>
    /// Reference of the first image version
    /// </summary>
    private const string FirstReference = "docker.io/company/api:1.0.0";

    /// <summary>
    /// Reference of the second image version
    /// </summary>
    private const string SecondReference = "docker.io/company/api:2.0.0";

    /// <summary>
    /// Description of every seeded observed image
    /// </summary>
    private const string SeedDescription = "seed description";

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Time of every seeded observed image
    /// </summary>
    private static readonly DateTimeOffset _seedTime = new(2020, 1, 2, 3, 4, 5, TimeSpan.Zero);

    /// <summary>
    /// Time of the first registration
    /// </summary>
    private static readonly DateTimeOffset _firstRegistrationTime = new(2024, 5, 6, 7, 8, 9, TimeSpan.Zero);

    /// <summary>
    /// Time of the second registration
    /// </summary>
    private static readonly DateTimeOffset _secondRegistrationTime = new(2025, 6, 7, 8, 9, 10, TimeSpan.Zero);

    #endregion // Fields

    #region Static methods

    /// <summary>
    /// Create the service under test on the real catalog repository
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="now">Time the injected clock returns</param>
    /// <returns>Service</returns>
    private static ImageRegistrationService CreateService(DockerUpdateGuardDbContext dbContext, DateTimeOffset now)
    {
        var clock = Substitute.For<TimeProvider>();

        clock.GetUtcNow().Returns(now);

        return new ImageRegistrationService(dbContext,
                                            new ImageCatalogRepository(dbContext),
                                            new ImageReferenceParser(),
                                            clock);
    }

    /// <summary>
    /// Create a registration request
    /// </summary>
    /// <param name="name">Name</param>
    /// <param name="description">Description</param>
    /// <param name="imageReference">Image reference</param>
    /// <returns>Request</returns>
    private static ObservedImageRegistrationRequest CreateRequest(string name, string? description, string imageReference)
    {
        return new ObservedImageRegistrationRequest
               {
                   Name = name,
                   Description = description,
                   ImageReference = imageReference,
               };
    }

    /// <summary>
    /// Seed an image version of the company/api repository
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="tag">Tag</param>
    /// <param name="source">Source of the version</param>
    /// <returns>Image version</returns>
    private static async Task<ImageVersion> SeedVersionAsync(DockerUpdateGuardDbContext dbContext, string tag, ImageVersionSource source)
    {
        var imageVersion = await new ImageCatalogRepository(dbContext).GetOrCreateImageVersionAsync("docker.io", "company/api", tag, null)
                                                                      .ConfigureAwait(false);

        imageVersion.Source = source;

        await dbContext.SaveChangesAsync(CancellationToken.None)
                       .ConfigureAwait(false);

        return imageVersion;
    }

    /// <summary>
    /// Seed an observed image
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="name">Name</param>
    /// <param name="imageVersion">Current image version</param>
    /// <param name="source">Registration source</param>
    /// <returns>Observed image</returns>
    private static async Task<ObservedImage> SeedImageAsync(DockerUpdateGuardDbContext dbContext,
                                                            string name,
                                                            ImageVersion imageVersion,
                                                            RegistrationSource source)
    {
        var observedImage = new ObservedImage
                            {
                                Name = name,
                                Description = SeedDescription,
                                CurrentImageVersionId = imageVersion.Id,
                                Source = source,
                                CreatedAtUtc = _seedTime,
                                UpdatedAtUtc = _seedTime,
                            };

        dbContext.ObservedImages.Add(observedImage);

        await dbContext.SaveChangesAsync(CancellationToken.None)
                       .ConfigureAwait(false);

        return observedImage;
    }

    /// <summary>
    /// Load an observed image from the database, bypassing the change tracker
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="id">Identifier</param>
    /// <returns>Stored observed image</returns>
    private static Task<ObservedImage> LoadImageAsync(DockerUpdateGuardDbContext dbContext, Guid id)
    {
        return dbContext.ObservedImages.AsNoTracking()
                                       .SingleAsync(entity => entity.Id == id, CancellationToken.None);
    }

    /// <summary>
    /// Assert the stored observed image still carries its seeded state
    /// </summary>
    /// <param name="stored">Stored observed image</param>
    /// <param name="name">Expected name</param>
    /// <param name="imageVersion">Expected current image version</param>
    /// <param name="source">Expected registration source</param>
    private static void AssertSeedState(ObservedImage stored, string name, ImageVersion imageVersion, RegistrationSource source)
    {
        Assert.AreEqual(name, stored.Name, "The name must be unchanged");
        Assert.AreEqual(SeedDescription, stored.Description, "The description must be unchanged");
        Assert.AreEqual(imageVersion.Id, stored.CurrentImageVersionId, "The current image version must be unchanged");
        Assert.AreEqual(source, stored.Source, "The registration source must be unchanged");
        Assert.IsTrue(stored.IsEnabled, "The enabled state must be unchanged");
        Assert.AreEqual(_seedTime, stored.UpdatedAtUtc, "The update time must be unchanged");
    }

    /// <summary>
    /// Assert a name is rejected with the message, nothing is saved and no catalog entry is created
    /// </summary>
    /// <param name="name">Name to register</param>
    /// <param name="expectedMessage">Expected message</param>
    /// <returns>Task</returns>
    private static async Task AssertNameRejectedAsync(string name, string expectedMessage)
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var service = CreateService(dbContext, _firstRegistrationTime);
                var request = CreateRequest(name, null, FirstReference);

                var exception = await Assert.ThrowsExactlyAsync<ReportableException>(() => service.RegisterAsync(request, CancellationToken.None),
                                                                                     "An invalid name must raise a reportable error")
                                            .ConfigureAwait(false);

                Assert.AreEqual(expectedMessage, exception.Message, "The reportable error must carry the fixed message");

                var observedImageCount = await dbContext.ObservedImages.CountAsync(CancellationToken.None).ConfigureAwait(false);
                var imageVersionCount = await dbContext.ImageVersions.CountAsync(CancellationToken.None).ConfigureAwait(false);

                Assert.AreEqual(0, observedImageCount, "A rejected name must not save an observed image");
                Assert.AreEqual(0, imageVersionCount, "A rejected name must not create a catalog entry");
            }
        }
    }

    /// <summary>
    /// Assert a reference is rejected with the text and nothing is saved
    /// </summary>
    /// <param name="imageReference">Image reference</param>
    /// <param name="expectedMessage">Expected message</param>
    /// <returns>Task</returns>
    private static async Task AssertReferenceRejectedBeforeCatalogAsync(string imageReference, string expectedMessage)
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var service = CreateService(dbContext, _firstRegistrationTime);
                var request = CreateRequest("Company API", null, imageReference);

                var exception = await Assert.ThrowsExactlyAsync<ReportableException>(() => service.RegisterAsync(request, CancellationToken.None),
                                                                                     "An over-long reference part must raise a reportable error")
                                            .ConfigureAwait(false);

                Assert.AreEqual(expectedMessage, exception.Message, "The reportable error must carry the fixed message");

                var registryRepositoryCount = await dbContext.RegistryRepositories.CountAsync(CancellationToken.None).ConfigureAwait(false);
                var imageVersionCount = await dbContext.ImageVersions.CountAsync(CancellationToken.None).ConfigureAwait(false);
                var observedImageCount = await dbContext.ObservedImages.CountAsync(CancellationToken.None).ConfigureAwait(false);

                Assert.AreEqual(0, registryRepositoryCount, "A rejected reference must not save a registry repository");
                Assert.AreEqual(0, imageVersionCount, "A rejected reference must not save an image version");
                Assert.AreEqual(0, observedImageCount, "A rejected reference must not save an observed image");
            }
        }
    }

    #endregion // Static methods

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
                                                           new ImageReferenceParser(),
                                                           TimeProvider.System);
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
    /// Verify a reference without a repository name is rejected with a reportable error that wraps the parser error and saves nothing
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageRegistrationServiceRegisterAsyncReferenceWithoutRepositoryThrowsReportableExceptionAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var imageVersion = new ImageVersion
                                   {
                                       RegistryRepository = new RegistryRepository
                                                            {
                                                                Registry = "docker.io",
                                                                Repository = "company/api",
                                                            },
                                       Tag = "1.0.0",
                                   };
                var service = new ImageRegistrationService(dbContext,
                                                           new TestImageCatalogRepository(imageVersion),
                                                           new ImageReferenceParser(),
                                                           TimeProvider.System);
                var request = new ObservedImageRegistrationRequest
                              {
                                  Name = "Company API",
                                  ImageReference = ":latest",
                              };

                var exception = await Assert.ThrowsExactlyAsync<ReportableException>(() => service.RegisterAsync(request, CancellationToken.None),
                                                                                     "A reference without a repository name must raise a reportable error")
                                            .ConfigureAwait(false);

                Assert.AreEqual("The image reference does not contain a repository name",
                                exception.Message,
                                "The reportable error must carry the fixed message");
                Assert.IsInstanceOfType<ArgumentException>(exception.InnerException,
                                                           "The reportable error must wrap the parser error");

                var observedImageCountTask = dbContext.ObservedImages.CountAsync(CancellationToken.None);
                var observedImageCount = await observedImageCountTask.ConfigureAwait(false);

                Assert.AreEqual(0,
                                observedImageCount,
                                "A rejected reference must not save an observed image");
            }
        }
    }

    /// <summary>
    /// Verify registering the same reference twice under another name and case updates the existing manual image
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task ImageRegistrationServiceRegisterSameReferenceTwiceUpdatesExistingImageAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var first = await CreateService(dbContext, _firstRegistrationTime).RegisterAsync(CreateRequest("Company API", "first", FirstReference), CancellationToken.None)
                                                                                  .ConfigureAwait(false);
                var second = await CreateService(dbContext, _secondRegistrationTime).RegisterAsync(CreateRequest("  company api  ", "   ", FirstReference), CancellationToken.None)
                                                                                    .ConfigureAwait(false);
                var observedImageCount = await dbContext.ObservedImages.CountAsync(CancellationToken.None).ConfigureAwait(false);
                var stored = await LoadImageAsync(dbContext, first.Id).ConfigureAwait(false);

                Assert.AreEqual(1, observedImageCount, "The same reference must not create a second observed image");
                Assert.AreEqual(first.Id, second.Id, "The registration must return the existing image");
                Assert.AreEqual("company api", stored.Name, "The name must be replaced by the trimmed new name");
                Assert.IsNull(stored.Description, "A blank description must be stored as none");
                Assert.IsTrue(stored.IsEnabled, "The image must be enabled");
                Assert.AreEqual(RegistrationSource.Manual, stored.Source, "The source must be manual");
                Assert.AreEqual(_secondRegistrationTime, stored.UpdatedAtUtc, "The update time must be the injected clock's time");
            }
        }
    }

    /// <summary>
    /// Verify the name is trimmed and the existing manual image is matched by its reference, not by its name
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task ImageRegistrationServiceRegisterTrimsNameAndMatchesByReferenceAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var imageVersion = await SeedVersionAsync(dbContext, "1.0.0", ImageVersionSource.ObservedImage).ConfigureAwait(false);
                var seeded = await SeedImageAsync(dbContext, "web", imageVersion, RegistrationSource.Manual).ConfigureAwait(false);

                await CreateService(dbContext, _secondRegistrationTime).RegisterAsync(CreateRequest("  WEB  ", "new", FirstReference), CancellationToken.None)
                                                                       .ConfigureAwait(false);

                var observedImageCount = await dbContext.ObservedImages.CountAsync(CancellationToken.None).ConfigureAwait(false);
                var stored = await LoadImageAsync(dbContext, seeded.Id).ConfigureAwait(false);

                Assert.AreEqual(1, observedImageCount, "A name that differs in case or whitespace must not create a second image");
                Assert.AreEqual("WEB", stored.Name, "The stored name must be the trimmed name");
                Assert.AreEqual("new", stored.Description, "The description must be replaced");
            }
        }
    }

    /// <summary>
    /// Verify registering the reference of a disabled manual image enables it again
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task ImageRegistrationServiceRegisterDisabledManualImageEnablesItAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var imageVersion = await SeedVersionAsync(dbContext, "1.0.0", ImageVersionSource.ObservedImage).ConfigureAwait(false);
                var seeded = await SeedImageAsync(dbContext, "old", imageVersion, RegistrationSource.Manual).ConfigureAwait(false);

                seeded.IsEnabled = false;

                await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                await CreateService(dbContext, _secondRegistrationTime).RegisterAsync(CreateRequest("renamed", null, FirstReference), CancellationToken.None)
                                                                       .ConfigureAwait(false);

                var observedImageCount = await dbContext.ObservedImages.CountAsync(CancellationToken.None).ConfigureAwait(false);
                var stored = await LoadImageAsync(dbContext, seeded.Id).ConfigureAwait(false);

                Assert.AreEqual(1, observedImageCount, "The disabled image must be reused");
                Assert.IsTrue(stored.IsEnabled, "Registering the reference again must enable the image");
                Assert.AreEqual("renamed", stored.Name, "The name must be replaced");
            }
        }
    }

    /// <summary>
    /// Verify two different references under one name create two manual images
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task ImageRegistrationServiceRegisterSameNameDifferentReferenceCreatesSecondImageAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var first = await CreateService(dbContext, _firstRegistrationTime).RegisterAsync(CreateRequest("Company API", null, FirstReference), CancellationToken.None)
                                                                                  .ConfigureAwait(false);
                var second = await CreateService(dbContext, _secondRegistrationTime).RegisterAsync(CreateRequest("Company API", null, SecondReference), CancellationToken.None)
                                                                                    .ConfigureAwait(false);
                var storedImages = await dbContext.ObservedImages.AsNoTracking().ToListAsync(CancellationToken.None).ConfigureAwait(false);

                Assert.HasCount(2, storedImages, "Two references must be two observed images");
                Assert.AreNotEqual(first.Id, second.Id, "The second reference must create its own image");
                Assert.IsTrue(storedImages.All(entity => entity.Source == RegistrationSource.Manual), "Both images must be manual");
                Assert.AreNotEqual(first.CurrentImageVersionId, second.CurrentImageVersionId, "Each image must keep its own version");
            }
        }
    }

    /// <summary>
    /// Verify the reference of an enabled discovered image is rejected and nothing is changed
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task ImageRegistrationServiceRegisterReferenceOfDiscoveredImageIsRejectedAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var imageVersion = await SeedVersionAsync(dbContext, "1.0.0", ImageVersionSource.RuntimeContainer).ConfigureAwait(false);
                var seeded = await SeedImageAsync(dbContext, "myuser/app", imageVersion, RegistrationSource.Discovery).ConfigureAwait(false);
                var service = CreateService(dbContext, _secondRegistrationTime);
                var request = CreateRequest("Mine", "mine", FirstReference);

                var exception = await Assert.ThrowsExactlyAsync<OwnImageAlreadyTrackedException>(() => service.RegisterAsync(request, CancellationToken.None),
                                                                                                 "The reference of an enabled discovered image must be rejected")
                                            .ConfigureAwait(false);

                Assert.AreEqual("This image is already tracked as own image 'myuser/app'.", exception.Message, "The message must name the own image");
                Assert.AreEqual(seeded.Id, exception.ObservedImageId, "The exception must carry the own image's identifier");

                var storedImages = await dbContext.ObservedImages.AsNoTracking().ToListAsync(CancellationToken.None).ConfigureAwait(false);
                var storedVersion = await dbContext.ImageVersions.AsNoTracking().SingleAsync(entity => entity.Id == imageVersion.Id, CancellationToken.None).ConfigureAwait(false);

                Assert.HasCount(1, storedImages, "No observed image may be added");
                AssertSeedState(storedImages[0], "myuser/app", imageVersion, RegistrationSource.Discovery);
                Assert.AreEqual(ImageVersionSource.RuntimeContainer, storedVersion.Source, "The image version's source must be unchanged");
            }
        }
    }

    /// <summary>
    /// Verify registering a discovered image's name with another reference creates a new manual image
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task ImageRegistrationServiceRegisterDiscoveredImageNameDoesNotTakeOverAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var imageVersion = await SeedVersionAsync(dbContext, "1.0.0", ImageVersionSource.ObservedImage).ConfigureAwait(false);
                var seeded = await SeedImageAsync(dbContext, "myuser/app", imageVersion, RegistrationSource.Discovery).ConfigureAwait(false);
                var registered = await CreateService(dbContext, _secondRegistrationTime).RegisterAsync(CreateRequest("myuser/app", null, SecondReference), CancellationToken.None)
                                                                                        .ConfigureAwait(false);
                var storedDiscovered = await LoadImageAsync(dbContext, seeded.Id).ConfigureAwait(false);
                var observedImageCount = await dbContext.ObservedImages.CountAsync(CancellationToken.None).ConfigureAwait(false);

                Assert.AreEqual(2, observedImageCount, "A new manual image must be created");
                Assert.AreNotEqual(seeded.Id, registered.Id, "The discovered image must not be reused");
                Assert.AreEqual(RegistrationSource.Manual, registered.Source, "The new image must be manual");
                AssertSeedState(storedDiscovered, "myuser/app", imageVersion, RegistrationSource.Discovery);
            }
        }
    }

    /// <summary>
    /// Verify the reference of a disabled discovered image creates a new manual image and leaves the discovered one unchanged
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task ImageRegistrationServiceRegisterReferenceOfDisabledDiscoveredImageCreatesManualImageAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var imageVersion = await SeedVersionAsync(dbContext, "1.0.0", ImageVersionSource.ObservedImage).ConfigureAwait(false);
                var seeded = await SeedImageAsync(dbContext, "myuser/app", imageVersion, RegistrationSource.Discovery).ConfigureAwait(false);

                seeded.IsEnabled = false;

                await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

                var registered = await CreateService(dbContext, _secondRegistrationTime).RegisterAsync(CreateRequest("myuser/app", null, FirstReference), CancellationToken.None)
                                                                                        .ConfigureAwait(false);
                var storedDiscovered = await LoadImageAsync(dbContext, seeded.Id).ConfigureAwait(false);
                var observedImageCount = await dbContext.ObservedImages.CountAsync(CancellationToken.None).ConfigureAwait(false);

                Assert.AreEqual(2, observedImageCount, "A new manual image must be created");
                Assert.AreNotEqual(seeded.Id, registered.Id, "The disabled discovered image must not be reused");
                Assert.AreEqual(RegistrationSource.Manual, registered.Source, "The new image must be manual");
                Assert.IsFalse(storedDiscovered.IsEnabled, "The discovered image must stay disabled");
                Assert.AreEqual(RegistrationSource.Discovery, storedDiscovered.Source, "The discovered image must keep its source");
                Assert.AreEqual(SeedDescription, storedDiscovered.Description, "The discovered image's description must be unchanged");
                Assert.AreEqual(_seedTime, storedDiscovered.UpdatedAtUtc, "The discovered image's update time must be unchanged");
            }
        }
    }

    /// <summary>
    /// Verify a blank name is rejected
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC6")]
    public Task ImageRegistrationServiceRegisterBlankNameIsRejectedAsync()
    {
        return AssertNameRejectedAsync("   ", "The name must not be empty");
    }

    /// <summary>
    /// Verify a name longer than 200 characters after trimming is rejected
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC6")]
    public Task ImageRegistrationServiceRegisterTooLongNameIsRejectedAsync()
    {
        return AssertNameRejectedAsync(new string('a', 201), "The name must not be longer than 200 characters");
    }

    /// <summary>
    /// Verify a name of 200 characters with surrounding whitespace is accepted
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC6")]
    public async Task ImageRegistrationServiceRegisterNameOfMaximumLengthAfterTrimmingIsAcceptedAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var name = new string('a', 200);
                var registered = await CreateService(dbContext, _firstRegistrationTime).RegisterAsync(CreateRequest($"  {name}  ", null, FirstReference), CancellationToken.None)
                                                                                       .ConfigureAwait(false);

                Assert.AreEqual(name, registered.Name, "A name of 200 characters must be accepted and stored trimmed");
            }
        }
    }

    /// <summary>
    /// Verify two spellings of one Docker Hub reference end in one manual image
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC7")]
    public async Task ImageRegistrationServiceRegisterDockerHubSpellingVariantsMatchSameImageAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var first = await CreateService(dbContext, _firstRegistrationTime).RegisterAsync(CreateRequest("Web", null, "nginx:1.27"), CancellationToken.None)
                                                                                  .ConfigureAwait(false);
                var second = await CreateService(dbContext, _secondRegistrationTime).RegisterAsync(CreateRequest("Proxy", null, " docker.io/library/nginx:1.27 "), CancellationToken.None)
                                                                                    .ConfigureAwait(false);
                var observedImageCount = await dbContext.ObservedImages.CountAsync(CancellationToken.None).ConfigureAwait(false);

                Assert.AreEqual(1, observedImageCount, "Both spellings must end in one manual image");
                Assert.AreEqual(first.Id, second.Id, "The second spelling must update the first image");
                Assert.AreEqual("Proxy", second.Name, "The name must be replaced");
            }
        }
    }

    /// <summary>
    /// Verify registration updates the oldest of several manual images of one version
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC8")]
    public async Task ImageRegistrationServiceRegisterWithExistingDuplicatesUpdatesOldestAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var imageVersion = await SeedVersionAsync(dbContext, "1.0.0", ImageVersionSource.ObservedImage).ConfigureAwait(false);
                var newer = await SeedImageAsync(dbContext, "newer", imageVersion, RegistrationSource.Manual).ConfigureAwait(false);
                var older = await SeedImageAsync(dbContext, "older", imageVersion, RegistrationSource.Manual).ConfigureAwait(false);

                newer.CreatedAtUtc = _seedTime.AddDays(10);
                older.CreatedAtUtc = _seedTime.AddDays(-10);

                await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

                var registered = await CreateService(dbContext, _secondRegistrationTime).RegisterAsync(CreateRequest("Renamed", null, FirstReference), CancellationToken.None)
                                                                                        .ConfigureAwait(false);
                var storedNewer = await LoadImageAsync(dbContext, newer.Id).ConfigureAwait(false);
                var observedImageCount = await dbContext.ObservedImages.CountAsync(CancellationToken.None).ConfigureAwait(false);

                Assert.AreEqual(2, observedImageCount, "No image may be added or removed");
                Assert.AreEqual(older.Id, registered.Id, "The image with the earlier creation time must be updated");
                Assert.AreEqual("Renamed", registered.Name, "The oldest image must carry the new name");
                AssertSeedState(storedNewer, "newer", imageVersion, RegistrationSource.Manual);
            }
        }
    }

    /// <summary>
    /// Verify an over-long tag is rejected before anything reaches the catalog
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public Task ImageRegistrationServiceRegisterTooLongTagIsRejectedBeforeCatalogAsync()
    {
        return AssertReferenceRejectedBeforeCatalogAsync($"docker.io/company/api:{new string('t', 201)}", "The tag must not be longer than 200 characters");
    }

    /// <summary>
    /// Verify an over-long repository is rejected before anything reaches the catalog
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public Task ImageRegistrationServiceRegisterTooLongRepositoryIsRejectedBeforeCatalogAsync()
    {
        return AssertReferenceRejectedBeforeCatalogAsync($"docker.io/company/{new string('a', 293)}:1.0.0", "The repository must not be longer than 300 characters");
    }

    #endregion // Methods
}