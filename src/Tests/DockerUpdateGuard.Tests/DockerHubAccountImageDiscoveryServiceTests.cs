using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Infrastructure;

using Microsoft.EntityFrameworkCore;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DockerHubAccountImageDiscoveryService"/>
/// </summary>
[TestClass]
public class DockerHubAccountImageDiscoveryServiceTests
{
    #region Methods

    /// <summary>
    /// Verify Docker Hub account synchronization creates, updates and disables discovered observed images
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubAccountImageDiscoveryServiceSynchronizeAccountImagesAsyncSynchronizesRepositoriesAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var logger = new TestLogger<DockerHubAccountImageDiscoveryService>();
                var dockerHubClient = Substitute.For<IDockerHubClient>();
                var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(new DockerUpdateGuardOptions
                                                                                      {
                                                                                          DockerHub = new DockerHubOptions
                                                                                                      {
                                                                                                          Registry = "docker.io",
                                                                                                          UserName = "acme",
                                                                                                          Pat = "configured-pat",
                                                                                                      },
                                                                                      });
                var existingRepositoryTask = imageCatalogRepository.GetOrCreateRegistryRepositoryAsync("docker.io",
                                                                                                        "acme/api",
                                                                                                        CancellationToken.None);
                var existingRepository = await existingRepositoryTask.ConfigureAwait(false);
                var existingVersion = new ImageVersion
                                      {
                                          RegistryRepositoryId = existingRepository.Id,
                                          RegistryRepository = existingRepository,
                                          Tag = "1.0.0",
                                          Digest = "sha256:old",
                                          Source = ImageVersionSource.ObservedImage,
                                      };
                var removedRepositoryTask = imageCatalogRepository.GetOrCreateRegistryRepositoryAsync("docker.io",
                                                                                                       "acme/removed",
                                                                                                       CancellationToken.None);
                var removedRepository = await removedRepositoryTask.ConfigureAwait(false);
                var removedVersion = new ImageVersion
                                     {
                                         RegistryRepositoryId = removedRepository.Id,
                                         RegistryRepository = removedRepository,
                                         Tag = "0.9.0",
                                         Digest = "sha256:removed",
                                         Source = ImageVersionSource.ObservedImage,
                                     };
                var manualRepositoryTask = imageCatalogRepository.GetOrCreateRegistryRepositoryAsync("docker.io",
                                                                                                      "acme/manual",
                                                                                                      CancellationToken.None);
                var manualRepository = await manualRepositoryTask.ConfigureAwait(false);
                var manualVersion = new ImageVersion
                                    {
                                        RegistryRepositoryId = manualRepository.Id,
                                        RegistryRepository = manualRepository,
                                        Tag = "2.0.0",
                                        Digest = "sha256:manual",
                                        Source = ImageVersionSource.ObservedImage,
                                    };

                dbContext.ImageVersions.AddRange(existingVersion,
                                                 removedVersion,
                                                 manualVersion);
                dbContext.ObservedImages.AddRange(new ObservedImage
                                                  {
                                                      Name = "acme/api",
                                                      CurrentImageVersion = existingVersion,
                                                      Source = RegistrationSource.Discovery,
                                                  },
                                                  new ObservedImage
                                                  {
                                                      Name = "acme/removed",
                                                      CurrentImageVersion = removedVersion,
                                                      Source = RegistrationSource.Discovery,
                                                  },
                                                  new ObservedImage
                                                  {
                                                      Name = "Manual image",
                                                      CurrentImageVersion = manualVersion,
                                                      Source = RegistrationSource.Manual,
                                                  });
                await dbContext.SaveChangesAsync(CancellationToken.None)
                               .ConfigureAwait(false);

                dockerHubClient.GetRepositoriesAsync("acme", CancellationToken.None)
                               .Returns(ExternalOperationResult<IReadOnlyList<DockerHubRepositoryData>>.Succeeded(
                               [
                                   new DockerHubRepositoryData
                                   {
                                       Registry = "docker.io",
                                       Repository = "acme/api",
                                       Description = "API service",
                                   },
                                   new DockerHubRepositoryData
                                   {
                                       Registry = "docker.io",
                                       Repository = "acme/web",
                                       Description = "Web frontend",
                                   },
                               ]));
                dockerHubClient.GetTagsAsync("docker.io",
                                             "acme/api",
                                             CancellationToken.None)
                               .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded(
                               [
                                   new DockerHubTagData
                                   {
                                       Tag = "1.1.0",
                                       Digest = "sha256:new-api",
                                       PublishedAtUtc = DateTimeOffset.UtcNow,
                                   },
                               ]));
                dockerHubClient.GetTagsAsync("docker.io",
                                             "acme/web",
                                             CancellationToken.None)
                               .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded(
                               [
                                   new DockerHubTagData
                                   {
                                       Tag = "latest",
                                       Digest = "sha256:web",
                                       PublishedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                                   },
                                   new DockerHubTagData
                                   {
                                       Tag = "1.0.0",
                                       Digest = "sha256:web-old",
                                       PublishedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                                   },
                               ]));

                var service = new DockerHubAccountImageDiscoveryService(dbContext,
                                                                        dockerHubClient,
                                                                        imageCatalogRepository,
                                                                        logger,
                                                                        optionsMonitor);

                await service.SynchronizeAccountImagesAsync(CancellationToken.None)
                             .ConfigureAwait(false);

                var discoveredImages = await dbContext.ObservedImages.Include(entity => entity.CurrentImageVersion)
                                                                     .ThenInclude(entity => entity.RegistryRepository)
                                                                     .Where(entity => entity.Source == RegistrationSource.Discovery)
                                                                     .OrderBy(entity => entity.Name)
                                                                     .ToListAsync(CancellationToken.None)
                                                                     .ConfigureAwait(false);
                var apiImage = discoveredImages.Single(entity => entity.Name == "acme/api");
                var webImage = discoveredImages.Single(entity => entity.Name == "acme/web");
                var removedImage = discoveredImages.Single(entity => entity.Name == "acme/removed");
                var manualImageTask = dbContext.ObservedImages.SingleAsync(entity => entity.Name == "Manual image", CancellationToken.None);
                var manualImage = await manualImageTask.ConfigureAwait(false);

                Assert.AreEqual(3,
                                discoveredImages.Count,
                                "Synchronization must keep existing discovered images and add new ones as needed");
                Assert.AreEqual("1.1.0",
                                apiImage.CurrentImageVersion.Tag,
                                "Synchronization must update existing discovered images to the selected repository tag");
                Assert.AreEqual("API service",
                                apiImage.Description,
                                "Synchronization must refresh the discovered image description");
                Assert.AreEqual("latest",
                                webImage.CurrentImageVersion.Tag,
                                "Synchronization must prefer the latest tag when it is present");
                Assert.IsTrue(webImage.IsEnabled, "Synchronization must enable newly discovered images");
                Assert.IsFalse(removedImage.IsEnabled, "Synchronization must disable discovered images that are no longer present in the account");
                Assert.IsTrue(manualImage.IsEnabled, "Synchronization must not modify manual observed images");
                Assert.IsTrue(logger.Entries.Any(entry => entry.EventId.Id == 2096), "Synchronization must emit a completion log entry");
            }
        }
    }

    /// <summary>
    /// Verify Docker Hub account synchronization skips execution when no PAT is configured
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubAccountImageDiscoveryServiceSynchronizeAccountImagesAsyncSkipsWithoutPatAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var dockerHubClient = Substitute.For<IDockerHubClient>();
                var logger = new TestLogger<DockerHubAccountImageDiscoveryService>();
                var service = new DockerHubAccountImageDiscoveryService(dbContext,
                                                                        dockerHubClient,
                                                                        new ImageCatalogRepository(dbContext),
                                                                        logger,
                                                                        new TestOptionsMonitor<DockerUpdateGuardOptions>(new DockerUpdateGuardOptions()));

                await service.SynchronizeAccountImagesAsync(CancellationToken.None)
                             .ConfigureAwait(false);

                var repositoriesTask = dockerHubClient.DidNotReceive()
                                                      .GetRepositoriesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

                await repositoriesTask.ConfigureAwait(false);
                Assert.IsTrue(logger.Entries.Any(entry => entry.EventId.Id == 2092), "Synchronization must log when Docker Hub account discovery is skipped because no PAT is configured");
            }
        }
    }

    /// <summary>
    /// Verify Docker Hub account synchronization skips execution when no user name is configured
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubAccountImageDiscoveryServiceSynchronizeAccountImagesAsyncSkipsWithoutUserNameAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var dockerHubClient = Substitute.For<IDockerHubClient>();
                var logger = new TestLogger<DockerHubAccountImageDiscoveryService>();
                var options = new DockerUpdateGuardOptions
                              {
                                  DockerHub = new DockerHubOptions
                                              {
                                                  Pat = "configured-pat",
                                              },
                              };
                var service = new DockerHubAccountImageDiscoveryService(dbContext,
                                                                        dockerHubClient,
                                                                        new ImageCatalogRepository(dbContext),
                                                                        logger,
                                                                        new TestOptionsMonitor<DockerUpdateGuardOptions>(options));

                await service.SynchronizeAccountImagesAsync(CancellationToken.None)
                             .ConfigureAwait(false);

                var repositoriesTask = dockerHubClient.DidNotReceive()
                                                      .GetRepositoriesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

                await repositoriesTask.ConfigureAwait(false);
                Assert.IsTrue(logger.Entries.Any(entry => entry.EventId.Id == 2097), "Synchronization must log when Docker Hub account discovery is skipped because no user name is configured");
            }
        }
    }

    #endregion // Methods
}