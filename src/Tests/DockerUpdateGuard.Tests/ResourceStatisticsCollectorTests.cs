using System.Net;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Docker;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

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

    #region Static methods

    /// <summary>
    /// Create the collector with a real Docker instance client on an HTTP client
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="httpClient">HTTP client of the Docker instance client</param>
    /// <param name="logger">Logger of the collector</param>
    /// <returns>Collector</returns>
    private static ResourceStatisticsCollector CreateCollector(DockerUpdateGuardDbContext dbContext, HttpClient httpClient, TestLogger<ResourceStatisticsCollector> logger)
    {
        var configuredInstance = new DockerInstanceOptions
                                 {
                                     Name = "Production",
                                     BaseUrl = "https://docker.example.test",
                                     Enabled = true
                                 };
        var options = new DockerUpdateGuardOptions
                      {
                          DockerInstances = [configuredInstance]
                      };

        return new ResourceStatisticsCollector(dbContext,
                                               new DockerInstanceClient(new TestLogger<DockerInstanceClient>(), (_, _) => httpClient),
                                               Substitute.For<IInstanceDiscoveryService>(),
                                               logger,
                                               new TestOptionsMonitor<DockerUpdateGuardOptions>(options));
    }

    /// <summary>
    /// Create an HTTP client for the Docker Engine test double
    /// </summary>
    /// <param name="handler">Message handler</param>
    /// <returns>HTTP client</returns>
    private static HttpClient CreateHttpClient(HttpMessageHandler handler)
    {
        return new HttpClient(handler)
               {
                   BaseAddress = new Uri("https://docker.example.test/"),
               };
    }

    /// <summary>
    /// Answer a request of the three-round scenario: the second container times out in round 2 and returns in round 3
    /// </summary>
    /// <param name="request">Request</param>
    /// <param name="round">Number of the round, one minute apart from 12:00</param>
    /// <returns>Response</returns>
    private static HttpResponseMessage CreateRoundResponse(HttpRequestMessage request, int round)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path.EndsWith("/containers/json", StringComparison.Ordinal))
        {
            return ResourceSamplingEngineHandlerFactory.CreateJsonResponse(ResourceSamplingEngineHandlerFactory.ThreeContainersJson);
        }

        if (path.EndsWith("/stats", StringComparison.Ordinal) == false)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var isSecondContainer = path.EndsWith("/containers/c2/stats", StringComparison.Ordinal);

        if (isSecondContainer && round == 2)
        {
            throw new TaskCanceledException("The request timed out");
        }

        var totalBytes = round switch
                         {
                             1 => 1000L,
                             2 => 1600L,
                             _ => isSecondContainer ? 1600L : 2200L,
                         };

        return ResourceSamplingEngineHandlerFactory.CreateStatsResponse($"2025-06-01T12:0{round - 1}:00Z", totalBytes);
    }

    #endregion // Static methods

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

    /// <summary>
    /// Containers whose stats request times out are left out while the others are recorded with one instance sample counting them
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task ResourceStatisticsCollectorCollectAsyncWhenOneStatsRequestTimesOutRecordsOtherContainersAsync()
    {
        await using var dbContext = await CreateDbContextAsync().ConfigureAwait(false);

        using var handler = ResourceSamplingEngineHandlerFactory.Create(["c2"], "Timeout", []);
        using var httpClient = CreateHttpClient(handler);

        var collector = CreateCollector(dbContext, httpClient, new TestLogger<ResourceStatisticsCollector>());

        await collector.CollectAsync(TestContext.CancellationToken).ConfigureAwait(false);

        var containerIds = await dbContext.RuntimeContainerResourceSamples.OrderBy(entity => entity.ContainerId)
                                                                          .Select(entity => entity.ContainerId)
                                                                          .ToListAsync(TestContext.CancellationToken)
                                                                          .ConfigureAwait(false);
        var instanceSamples = await dbContext.DockerInstanceResourceSamples.ToListAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreSequenceEqual(["c1", "c3"], containerIds.ToArray(), "Only the containers whose stats could be read must be recorded");

        var instanceSample = Assert.ContainsSingle(instanceSamples, "One instance sample must be recorded for the round");

        Assert.AreEqual(2, instanceSample.ContainerCount, "The instance sample must count only the recorded containers");
    }

    /// <summary>
    /// A round in which every stats request times out records no sample and logs the collection failure
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task ResourceStatisticsCollectorCollectAsyncWhenEveryStatsRequestTimesOutRecordsNoSamplesAsync()
    {
        await using var dbContext = await CreateDbContextAsync().ConfigureAwait(false);

        using var handler = ResourceSamplingEngineHandlerFactory.Create(["c1", "c2", "c3"], "Timeout", []);
        using var httpClient = CreateHttpClient(handler);

        var logger = new TestLogger<ResourceStatisticsCollector>();
        var collector = CreateCollector(dbContext, httpClient, logger);

        await collector.CollectAsync(TestContext.CancellationToken).ConfigureAwait(false);

        var containerSampleCount = await dbContext.RuntimeContainerResourceSamples.CountAsync(TestContext.CancellationToken).ConfigureAwait(false);
        var instanceSampleCount = await dbContext.DockerInstanceResourceSamples.CountAsync(TestContext.CancellationToken).ConfigureAwait(false);
        var entry = Assert.ContainsSingle(logger.Entries, "The failed round must be logged once");

        Assert.AreEqual(0, containerSampleCount, "No container sample must be recorded");
        Assert.AreEqual(0, instanceSampleCount, "No instance sample must be recorded");
        Assert.AreEqual(2103, entry.EventId.Id, "The failed round must be logged with event id 2103");
        Assert.AreEqual("Resource statistics collection failed for Docker instance Production: Docker container resource sampling failed for 'Production': no container stats could be read",
                        entry.Message,
                        "The warning must carry the message of the failed sampling");
    }

    /// <summary>
    /// The instance rates of a round are the sums of the container rates, so a container that was dropped and returns causes no spike
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC8")]
    public async Task ResourceStatisticsCollectorCollectAsyncWhenContainerIsDroppedAndReturnsSumsContainerRatesAsync()
    {
        await using var dbContext = await CreateDbContextAsync().ConfigureAwait(false);

        var round = 1;

        using var handler = new DelegateHttpMessageHandler(request => CreateRoundResponse(request, round));
        using var httpClient = CreateHttpClient(handler);

        var collector = CreateCollector(dbContext, httpClient, new TestLogger<ResourceStatisticsCollector>());

        for (round = 1; round <= 3; round++)
        {
            await collector.CollectAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }

        var instanceSamples = await dbContext.DockerInstanceResourceSamples.OrderBy(entity => entity.RecordedAtUtc)
                                                                           .ToListAsync(TestContext.CancellationToken)
                                                                           .ConfigureAwait(false);

        Assert.AreSequenceEqual([3, 2, 3], instanceSamples.Select(entity => entity.ContainerCount).ToArray(), "Each round must count the containers it sampled");
        Assert.AreSequenceEqual([0m, 20m, 25m], instanceSamples.Select(entity => entity.NetworkRxBytesPerSecond).ToArray(), "The receive rate must be the sum of the container rates");
        Assert.AreSequenceEqual([0m, 20m, 25m], instanceSamples.Select(entity => entity.NetworkTxBytesPerSecond).ToArray(), "The transmit rate must be the sum of the container rates");
    }

    /// <summary>
    /// A round without a running container records an instance sample with count 0
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task ResourceStatisticsCollectorCollectAsyncWhenNoContainerIsRunningRecordsEmptyInstanceSampleAsync()
    {
        await using var dbContext = await CreateDbContextAsync().ConfigureAwait(false);

        using var handler = new DelegateHttpMessageHandler(request => request.RequestUri?.AbsolutePath.EndsWith("/containers/json", StringComparison.Ordinal) == true
                                                                          ? ResourceSamplingEngineHandlerFactory.CreateJsonResponse("[]")
                                                                          : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var httpClient = CreateHttpClient(handler);

        var collector = CreateCollector(dbContext, httpClient, new TestLogger<ResourceStatisticsCollector>());

        await collector.CollectAsync(TestContext.CancellationToken).ConfigureAwait(false);

        var containerSampleCount = await dbContext.RuntimeContainerResourceSamples.CountAsync(TestContext.CancellationToken).ConfigureAwait(false);
        var instanceSamples = await dbContext.DockerInstanceResourceSamples.ToListAsync(TestContext.CancellationToken).ConfigureAwait(false);
        var instanceSample = Assert.ContainsSingle(instanceSamples, "One instance sample must be recorded for the empty round");

        Assert.AreEqual(0, containerSampleCount, "No container sample must be recorded");
        Assert.AreEqual(0, instanceSample.ContainerCount, "The instance sample must count no container");
    }

    /// <summary>
    /// Create a database holding the enabled Docker instance Production
    /// </summary>
    /// <returns>Database context</returns>
    private async Task<DockerUpdateGuardDbContext> CreateDbContextAsync()
    {
        var databaseOptions = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                                       .Options;
        var dbContext = new DockerUpdateGuardDbContext(databaseOptions);

        dbContext.DockerInstances.Add(new DockerInstance
                                      {
                                          Name = "Production",
                                          IsEnabled = true
                                      });
        await dbContext.SaveChangesAsync(TestContext.CancellationToken).ConfigureAwait(false);

        return dbContext;
    }

    #endregion // Methods
}