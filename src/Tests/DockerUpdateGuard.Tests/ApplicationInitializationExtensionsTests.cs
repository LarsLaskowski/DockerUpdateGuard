using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Tests.Data;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ApplicationInitializationExtensions"/>
/// </summary>
[TestClass]
public class ApplicationInitializationExtensionsTests
{
    #region Properties

    /// <summary>
    /// Test context providing information about and functionality for the current test run
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify the startup initialization migrates with the registered time provider and synchronizes the instances
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationInitializationExtensionsInitializeDockerUpdateGuardMigratesAndSynchronizesAsync()
    {
        var instanceDiscoveryService = Substitute.For<IInstanceDiscoveryService>();
        var databaseName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder();

        builder.Services.AddDbContext<DockerUpdateGuardDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.AddSingleton(instanceDiscoveryService);
        builder.Services.AddSingleton<ApplicationTelemetry>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(Options.Create(new DockerUpdateGuardOptions()));

        var application = builder.Build();

        await using (application.ConfigureAwait(false))
        {
            await application.InitializeDockerUpdateGuardAsync()
                             .ConfigureAwait(false);
        }

        await instanceDiscoveryService.Received(1)
                                      .SynchronizeConfiguredInstancesAsync(CancellationToken.None)
                                      .ConfigureAwait(false);
    }

    /// <summary>
    /// Verify the startup initialization turns a run left Running into Failed with the interrupted message and keeps a terminal run
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC9")]
    public async Task ApplicationInitializationExtensionsInitializeDockerUpdateGuardReconcilesInterruptedRunsAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder();

        builder.Services.AddDbContext<DockerUpdateGuardDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.AddSingleton(Substitute.For<IInstanceDiscoveryService>());
        builder.Services.AddSingleton<ApplicationTelemetry>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(Options.Create(new DockerUpdateGuardOptions()));

        var application = builder.Build();
        List<ContainerActionRun> runs;

        await using (application.ConfigureAwait(false))
        {
            await SeedRunsAsync(application).ConfigureAwait(false);

            await application.InitializeDockerUpdateGuardAsync()
                             .ConfigureAwait(false);

            runs = await LoadRunsAsync(application).ConfigureAwait(false);
        }

        var interrupted = runs.Single(run => run.ResourceName == "running");
        var finished = runs.Single(run => run.ResourceName == "succeeded");

        Assert.AreEqual(ContainerActionStatus.Failed, interrupted.Status, "A run left Running must be failed at startup");
        Assert.AreEqual("Interrupted: the application stopped before the action completed",
                        interrupted.ErrorMessage,
                        "A run failed at startup must carry the message of E10");
        Assert.IsNotNull(interrupted.CompletedAtUtc, "A run failed at startup must be completed");
        Assert.AreEqual(ContainerActionStatus.Succeeded, finished.Status, "A terminal run must stay unchanged");
        Assert.AreEqual("done", finished.ErrorMessage, "A terminal run must keep its message");
    }

    /// <summary>
    /// Verify the startup initialization logs the number of reconciled runs only when it reconciled any
    /// </summary>
    /// <param name="withRuns">Whether a run left Running is seeded</param>
    /// <param name="expectedCount">Expected number of reconciliation log entries</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC9")]
    [DataRow(true, 1)]
    [DataRow(false, 0)]
    public async Task ApplicationInitializationExtensionsInitializeDockerUpdateGuardLogsReconciledRunCountAsync(bool withRuns, int expectedCount)
    {
        var logger = new TestLogger<WebApplication>();
        var application = CreateApplication(Guid.NewGuid().ToString(), CreateLoggerFactory(logger));

        await using (application.ConfigureAwait(false))
        {
            if (withRuns)
            {
                await SeedRunsAsync(application).ConfigureAwait(false);
            }

            await application.InitializeDockerUpdateGuardAsync()
                             .ConfigureAwait(false);
        }

        var entries = logger.Entries.Where(entry => entry.EventId.Id == 3324)
                                    .ToList();

        Assert.HasCount(expectedCount, entries, "The reconciliation must be logged exactly when a run was reconciled");
        Assert.IsTrue(entries.TrueForAll(entry => entry.LogLevel == LogLevel.Information
                                                  && entry.Message == "Reconciled 1 container action runs interrupted by an application stop"),
                      "The reconciliation entry must be informational and carry the number of reconciled runs");
    }

    /// <summary>
    /// Verify the startup initialization moves a seeded non-canonical observed image to the canonical version
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC9")]
    public async Task ApplicationInitializationExtensionsInitializeDockerUpdateGuardCanonicalizesTrackedImageReferencesAsync()
    {
        var application = CreateApplication(Guid.NewGuid().ToString(), CreateLoggerFactory(new TestLogger<WebApplication>()));
        string reference;

        await using (application.ConfigureAwait(false))
        {
            await SeedObservedImageAsync(application).ConfigureAwait(false);

            await application.InitializeDockerUpdateGuardAsync()
                             .ConfigureAwait(false);

            reference = await LoadObservedImageReferenceAsync(application).ConfigureAwait(false);
        }

        Assert.AreEqual("docker.io/library/nginx:1.27", reference, "A seeded non-canonical observed image must point at the canonical version after startup");
    }

    /// <summary>
    /// Verify the startup initialization logs the number of canonicalized references only when it moved any
    /// </summary>
    /// <param name="withRow">Whether a non-canonical observed image is seeded</param>
    /// <param name="expectedCount">Expected number of log entries</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC9")]
    [DataRow(true, 1)]
    [DataRow(false, 0)]
    public async Task ApplicationInitializationExtensionsInitializeDockerUpdateGuardLogsCanonicalizedReferenceCountAsync(bool withRow, int expectedCount)
    {
        var logger = new TestLogger<WebApplication>();
        var application = CreateApplication(Guid.NewGuid().ToString(), CreateLoggerFactory(logger));

        await using (application.ConfigureAwait(false))
        {
            if (withRow)
            {
                await SeedObservedImageAsync(application).ConfigureAwait(false);
            }

            await application.InitializeDockerUpdateGuardAsync()
                             .ConfigureAwait(false);
        }

        var entries = logger.Entries.Where(entry => entry.EventId.Id == 1009)
                                    .ToList();

        Assert.HasCount(expectedCount, entries, "The canonicalization must be logged exactly when a reference was moved");
        Assert.IsTrue(entries.TrueForAll(entry => entry.LogLevel == LogLevel.Information
                                                  && entry.Message == "Moved 1 tracked image references to canonical catalog entries"),
                      "The canonicalization entry must be informational and carry the number of moved references");
    }

    /// <summary>
    /// Create a logger factory that hands the given logger to the startup category and discards every other category
    /// </summary>
    /// <param name="logger">Logger of the startup category</param>
    /// <returns>Logger factory</returns>
    private static ILoggerFactory CreateLoggerFactory(ILogger logger)
    {
        var loggerFactory = Substitute.For<ILoggerFactory>();

        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(NullLogger.Instance);
        loggerFactory.CreateLogger(typeof(ApplicationInitializationExtensions).FullName!).Returns(logger);

        return loggerFactory;
    }

    /// <summary>
    /// Create an application on an in-memory database with the given logger factory
    /// </summary>
    /// <param name="databaseName">In-memory database name</param>
    /// <param name="loggerFactory">Logger factory</param>
    /// <returns>Application</returns>
    private static WebApplication CreateApplication(string databaseName, ILoggerFactory loggerFactory)
    {
        var builder = WebApplication.CreateBuilder();

        builder.Services.AddDbContext<DockerUpdateGuardDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.AddSingleton(Substitute.For<IInstanceDiscoveryService>());
        builder.Services.AddSingleton<ApplicationTelemetry>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(Options.Create(new DockerUpdateGuardOptions()));
        builder.Services.AddSingleton(loggerFactory);

        return builder.Build();
    }

    /// <summary>
    /// Seed a manual observed image on the non-canonical reference docker.io/nginx:1.27
    /// </summary>
    /// <param name="application">Application holding the database</param>
    /// <returns>Task</returns>
    private async Task SeedObservedImageAsync(WebApplication application)
    {
        var scope = application.Services.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>();

            dbContext.ObservedImages.Add(new ObservedImage
                                         {
                                             Name = "nginx",
                                             Source = RegistrationSource.Manual,
                                             CurrentImageVersion = new ImageVersion
                                                                   {
                                                                       Tag = "1.27",
                                                                       Digest = string.Empty,
                                                                       RegistryRepository = new RegistryRepository
                                                                                            {
                                                                                                Registry = "docker.io",
                                                                                                Repository = "nginx",
                                                                                            },
                                                                   },
                                         });

            await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                           .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Load the reference of the only observed image's current version
    /// </summary>
    /// <param name="application">Application holding the database</param>
    /// <returns>Registry, repository and tag of the current version</returns>
    private async Task<string> LoadObservedImageReferenceAsync(WebApplication application)
    {
        var scope = application.Services.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            var observedImage = await scope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>()
                                                           .ObservedImages
                                                           .AsNoTracking()
                                                           .Include(entity => entity.CurrentImageVersion)
                                                           .ThenInclude(entity => entity.RegistryRepository)
                                                           .SingleAsync(TestContext.CancellationToken)
                                                           .ConfigureAwait(false);
            var version = observedImage.CurrentImageVersion;

            return $"{version.RegistryRepository.Registry}/{version.RegistryRepository.Repository}:{version.Tag}";
        }
    }

    /// <summary>
    /// Seed a Running and a Succeeded run
    /// </summary>
    /// <param name="application">Application holding the database</param>
    /// <returns>Task</returns>
    private async Task SeedRunsAsync(WebApplication application)
    {
        var scope = application.Services.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>();
            var instance = new DockerInstance
                           {
                               Name = "Production",
                               EndpointUri = "https://docker.example.test",
                               ConnectionKind = DockerConnectionKind.Https,
                           };

            dbContext.DockerInstances.Add(instance);
            dbContext.ContainerActionRuns.AddRange(new ContainerActionRun
                                                   {
                                                       DockerInstance = instance,
                                                       ActionType = ContainerActionType.RestartContainer,
                                                       ResourceType = PortainerResourceType.Container,
                                                       ResourceName = "running",
                                                       RequestedBy = "UI",
                                                       Status = ContainerActionStatus.Running,
                                                   },
                                                   new ContainerActionRun
                                                   {
                                                       DockerInstance = instance,
                                                       ActionType = ContainerActionType.StopContainer,
                                                       ResourceType = PortainerResourceType.Container,
                                                       ResourceName = "succeeded",
                                                       RequestedBy = "UI",
                                                       Status = ContainerActionStatus.Succeeded,
                                                       ErrorMessage = "done",
                                                       CompletedAtUtc = DateTimeOffset.UnixEpoch,
                                                   });

            await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                           .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Load every action run
    /// </summary>
    /// <param name="application">Application holding the database</param>
    /// <returns>Runs</returns>
    private async Task<List<ContainerActionRun>> LoadRunsAsync(WebApplication application)
    {
        var scope = application.Services.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            return await scope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>()
                                              .ContainerActionRuns
                                              .AsNoTracking()
                                              .ToListAsync(TestContext.CancellationToken)
                                              .ConfigureAwait(false);
        }
    }

    #endregion // Methods
}