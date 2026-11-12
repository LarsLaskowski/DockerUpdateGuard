using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Docker;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ResourceStatisticsCollector"/>
/// </summary>
[TestClass]
public class ResourceStatisticsCollectorTests
{
    #region Properties

    /// <summary>
    /// Test context providing information about and functionality for the current test run
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// A failed resource statistics collection is logged once as a warning with event id 2103
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task ResourceStatisticsCollectorCollectAsyncWhenCollectionFailsLogsWarningWithEventId()
    {
        var operationMessage = "Docker instance 'edge' returned 500: boom";

        var databaseOptions = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                                       .Options;

        await using var dbContext = new DockerUpdateGuardDbContext(databaseOptions);

        dbContext.DockerInstances.Add(new DockerInstance
                                      {
                                          Name = "edge",
                                          IsEnabled = true
                                      });
        await dbContext.SaveChangesAsync(TestContext.CancellationToken);

        var configuredInstance = new DockerInstanceOptions
                                 {
                                     Name = "edge",
                                     Enabled = true
                                 };
        var options = new DockerUpdateGuardOptions
                      {
                          DockerInstances = [configuredInstance]
                      };
        var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();

        dockerInstanceClient.CollectContainerResourceUsageAsync(configuredInstance, Arg.Any<CancellationToken>())
                            .Returns(ExternalOperationResult.Failed<IReadOnlyList<RuntimeContainerResourceDescriptor>>(operationMessage));
        dockerInstanceClient.GetHostMemoryTotalAsync(configuredInstance, Arg.Any<CancellationToken>())
                            .Returns(ExternalOperationResult.Succeeded(0L));

        var logger = new TestLogger<ResourceStatisticsCollector>();
        var collector = new ResourceStatisticsCollector(dbContext,
                                                        dockerInstanceClient,
                                                        Substitute.For<IInstanceDiscoveryService>(),
                                                        logger,
                                                        new TestOptionsMonitor<DockerUpdateGuardOptions>(options));

        await collector.CollectAsync(TestContext.CancellationToken);

        Assert.HasCount(1, logger.Entries, "Exactly one log entry must be written for the failed collection");

        var entry = logger.Entries[0];

        Assert.AreEqual(LogLevel.Warning, entry.LogLevel, "A failed collection must be logged as a warning");
        Assert.AreEqual(2103, entry.EventId.Id, "A failed collection must be logged with event id 2103");
        Assert.AreEqual($"Resource statistics collection failed for Docker instance edge: {operationMessage}",
                        entry.Message,
                        "The message must name the Docker instance and carry the operation message");
    }

    #endregion // Methods
}