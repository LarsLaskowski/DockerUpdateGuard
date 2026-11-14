using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images.Interfaces;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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