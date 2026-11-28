using System.Text.Json.Nodes;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Docker;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

using Microsoft.Extensions.Logging;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for the reading of stats and host memory values of <see cref="DockerInstanceClient"/>
/// </summary>
[TestClass]
public class DockerInstanceClientStatsValuesTests
{
    #region Constants

    /// <summary>
    /// Stats example of the Docker Engine API definition (API v1.41)
    /// </summary>
    private const string StatsSample = """
                                                  {
                                                    "read": "2015-01-08T22:57:31.547920715Z",
                                                    "pids_stats": {
                                                      "current": 3
                                                    },
                                                    "networks": {
                                                      "eth0": {
                                                        "rx_bytes": 5338,
                                                        "rx_dropped": 0,
                                                        "rx_errors": 0,
                                                        "rx_packets": 36,
                                                        "tx_bytes": 648,
                                                        "tx_dropped": 0,
                                                        "tx_errors": 0,
                                                        "tx_packets": 8
                                                      },
                                                      "eth5": {
                                                        "rx_bytes": 4641,
                                                        "rx_dropped": 0,
                                                        "rx_errors": 0,
                                                        "rx_packets": 26,
                                                        "tx_bytes": 690,
                                                        "tx_dropped": 0,
                                                        "tx_errors": 0,
                                                        "tx_packets": 9
                                                      }
                                                    },
                                                    "memory_stats": {
                                                      "stats": {
                                                        "total_pgmajfault": 0,
                                                        "cache": 0,
                                                        "mapped_file": 0,
                                                        "total_inactive_file": 0,
                                                        "pgpgout": 414,
                                                        "rss": 6537216,
                                                        "total_mapped_file": 0,
                                                        "writeback": 0,
                                                        "unevictable": 0,
                                                        "pgpgin": 477,
                                                        "total_unevictable": 0,
                                                        "pgmajfault": 0,
                                                        "total_rss": 6537216,
                                                        "total_rss_huge": 6291456,
                                                        "total_writeback": 0,
                                                        "total_inactive_anon": 0,
                                                        "rss_huge": 6291456,
                                                        "hierarchical_memory_limit": 67108864,
                                                        "total_pgfault": 964,
                                                        "total_active_file": 0,
                                                        "active_anon": 6537216,
                                                        "total_active_anon": 6537216,
                                                        "total_pgpgout": 414,
                                                        "total_cache": 0,
                                                        "inactive_anon": 0,
                                                        "active_file": 0,
                                                        "pgfault": 964,
                                                        "inactive_file": 0,
                                                        "total_pgpgin": 477
                                                      },
                                                      "max_usage": 6651904,
                                                      "usage": 6537216,
                                                      "failcnt": 0,
                                                      "limit": 67108864
                                                    },
                                                    "blkio_stats": {},
                                                    "cpu_stats": {
                                                      "cpu_usage": {
                                                        "percpu_usage": [
                                                          8646879,
                                                          24472255,
                                                          36438778,
                                                          30657443
                                                        ],
                                                        "usage_in_usermode": 50000000,
                                                        "total_usage": 100215355,
                                                        "usage_in_kernelmode": 30000000
                                                      },
                                                      "system_cpu_usage": 739306590000000,
                                                      "online_cpus": 4,
                                                      "throttling_data": {
                                                        "periods": 0,
                                                        "throttled_periods": 0,
                                                        "throttled_time": 0
                                                      }
                                                    },
                                                    "precpu_stats": {
                                                      "cpu_usage": {
                                                        "percpu_usage": [
                                                          8646879,
                                                          24350896,
                                                          36438778,
                                                          30657443
                                                        ],
                                                        "usage_in_usermode": 50000000,
                                                        "total_usage": 100093996,
                                                        "usage_in_kernelmode": 30000000
                                                      },
                                                      "system_cpu_usage": 9492140000000,
                                                      "online_cpus": 4,
                                                      "throttling_data": {
                                                        "periods": 0,
                                                        "throttled_periods": 0,
                                                        "throttled_time": 0
                                                      }
                                                    }
                                                  }
                                       """;

    /// <summary>
    /// System CPU usage of the previous sample that makes the CPU percent of the base sample 40
    /// </summary>
    private const long BasePreviousSystemCpuUsage = 739306588786410;

    #endregion // Constants

    #region Properties

    /// <summary>
    /// Context for the tests
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify a stats value that is not a JSON number counts as 0 and the other values are still read
    /// </summary>
    /// <param name="path">Dot-separated path of the replaced member</param>
    /// <param name="replacementJson">JSON that replaces the member</param>
    /// <param name="expectedCpu">Expected CPU percent</param>
    /// <param name="expectedUsage">Expected memory usage</param>
    /// <param name="expectedLimit">Expected memory limit</param>
    /// <param name="expectedRx">Expected received network total</param>
    /// <param name="expectedTx">Expected transmitted network total</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    [DataRow("cpu_stats.cpu_usage.total_usage", "\"100215355\"", 0L, 6537216L, 67108864L, 9979L, 1338L)]
    [DataRow("cpu_stats.system_cpu_usage", "true", 0L, 6537216L, 67108864L, 9979L, 1338L)]
    [DataRow("precpu_stats.cpu_usage.total_usage", "\"x\"", 0L, 6537216L, 67108864L, 9979L, 1338L)]
    [DataRow("precpu_stats.system_cpu_usage", "null", 0L, 6537216L, 67108864L, 9979L, 1338L)]
    [DataRow("memory_stats.usage", "\"6537216\"", 40L, 0L, 67108864L, 9979L, 1338L)]
    [DataRow("memory_stats.limit", "null", 40L, 6537216L, 0L, 9979L, 1338L)]
    [DataRow("networks.eth0.rx_bytes", "\"5338\"", 40L, 6537216L, 67108864L, 4641L, 1338L)]
    [DataRow("networks.eth5.tx_bytes", "{}", 40L, 6537216L, 67108864L, 9979L, 648L)]
    public async Task DockerInstanceClientCollectContainerResourceUsageAsyncWhenStatsValueIsNoNumberCountsItAsZeroAsync(string path,
                                                                                                                        string replacementJson,
                                                                                                                        long expectedCpu,
                                                                                                                        long expectedUsage,
                                                                                                                        long expectedLimit,
                                                                                                                        long expectedRx,
                                                                                                                        long expectedTx)
    {
        await AssertSampledValuesAsync(path, replacementJson, expectedCpu, expectedUsage, expectedLimit, expectedRx, expectedTx).ConfigureAwait(false);
    }

    /// <summary>
    /// Verify a stats member on a read path that is not a JSON object counts its values as 0 and the other values are still read
    /// </summary>
    /// <param name="path">Dot-separated path of the replaced member</param>
    /// <param name="replacementJson">JSON that replaces the member</param>
    /// <param name="expectedCpu">Expected CPU percent</param>
    /// <param name="expectedUsage">Expected memory usage</param>
    /// <param name="expectedLimit">Expected memory limit</param>
    /// <param name="expectedRx">Expected received network total</param>
    /// <param name="expectedTx">Expected transmitted network total</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    [DataRow("memory_stats", "[]", 40L, 0L, 0L, 9979L, 1338L)]
    [DataRow("networks.eth0", "5", 40L, 6537216L, 67108864L, 4641L, 690L)]
    [DataRow("cpu_stats", "\"x\"", 0L, 6537216L, 67108864L, 9979L, 1338L)]
    [DataRow("cpu_stats.cpu_usage", "1", 0L, 6537216L, 67108864L, 9979L, 1338L)]
    [DataRow("precpu_stats", "\"x\"", 0L, 6537216L, 67108864L, 9979L, 1338L)]
    [DataRow("precpu_stats.cpu_usage", "1", 0L, 6537216L, 67108864L, 9979L, 1338L)]
    public async Task DockerInstanceClientCollectContainerResourceUsageAsyncWhenStatsMemberIsNoObjectCountsItsValuesAsZeroAsync(string path,
                                                                                                                                string replacementJson,
                                                                                                                                long expectedCpu,
                                                                                                                                long expectedUsage,
                                                                                                                                long expectedLimit,
                                                                                                                                long expectedRx,
                                                                                                                                long expectedTx)
    {
        await AssertSampledValuesAsync(path, replacementJson, expectedCpu, expectedUsage, expectedLimit, expectedRx, expectedTx).ConfigureAwait(false);
    }

    /// <summary>
    /// Verify an online CPU count that is not a number falls back to the length of the per-CPU usage
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task DockerInstanceClientCollectContainerResourceUsageAsyncWhenOnlineCpusIsNoNumberUsesPerCpuCountAsync()
    {
        await AssertSampledValuesAsync("cpu_stats.online_cpus", "\"2\"", 40, 6537216, 67108864, 9979, 1338).ConfigureAwait(false);
    }

    /// <summary>
    /// Verify a host info answer without a readable total memory succeeds with 0 and logs no failure
    /// </summary>
    /// <param name="infoJson">Body of the info answer</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    [DataRow("""{ "MemTotal": "8589934592" }""")]
    [DataRow("""{ "MemTotal": null }""")]
    [DataRow("[]")]
    [DataRow("\"x\"")]
    public async Task DockerInstanceClientGetHostMemoryTotalAsyncWhenMemTotalIsUnreadableReturnsZeroAsync(string infoJson)
    {
        var logger = new TestLogger<DockerInstanceClient>();

        using var handler = new DelegateHttpMessageHandler(_ => ResourceSamplingEngineHandlerFactory.CreateJsonResponse(infoJson));
        using var httpClient = CreateHttpClient(handler);

        var client = new DockerInstanceClient(logger, (_, _) => httpClient);

        var result = await client.GetHostMemoryTotalAsync(CreateInstanceOptions(), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, $"An info answer {infoJson} must not fail the host memory lookup");
        Assert.AreEqual(0L, result.Data, $"An info answer {infoJson} must count the host memory as 0");
        Assert.DoesNotContain(entry => entry.EventId.Id == 3110, logger.Entries, $"An info answer {infoJson} must log no host info failure");
    }

    #endregion // Methods

    #region Static methods

    /// <summary>
    /// Create the HTTP client of the Docker Engine double
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
    /// Create the options of an enabled Docker instance
    /// </summary>
    /// <returns>Instance options</returns>
    private static DockerInstanceOptions CreateInstanceOptions()
    {
        return new DockerInstanceOptions
               {
                   Name = "Production",
                   BaseUrl = "https://docker.example.test",
                   Enabled = true,
               };
    }

    /// <summary>
    /// Create the stats body of the base sample with one member replaced
    /// </summary>
    /// <param name="path">Dot-separated path of the replaced member</param>
    /// <param name="replacementJson">JSON that replaces the member</param>
    /// <returns>Stats body</returns>
    private static string CreateStatsBody(string path, string replacementJson)
    {
        var root = JsonNode.Parse(StatsSample)!;

        root["precpu_stats"]!["system_cpu_usage"] = BasePreviousSystemCpuUsage;

        var names = path.Split('.');
        var parent = root;

        foreach (var name in names.Take(names.Length - 1))
        {
            parent = parent[name]!;
        }

        parent[names[^1]] = JsonNode.Parse(replacementJson);

        return root.ToJsonString();
    }

    /// <summary>
    /// Create the answer of the Docker Engine double: one container and its stats body
    /// </summary>
    /// <param name="request">Request</param>
    /// <param name="statsBody">Stats body</param>
    /// <returns>Response</returns>
    private static HttpResponseMessage Respond(HttpRequestMessage request, string statsBody)
    {
        var isStats = request.RequestUri?.AbsolutePath.EndsWith("/stats", StringComparison.Ordinal) == true;

        return ResourceSamplingEngineHandlerFactory.CreateJsonResponse(isStats ? statsBody : """[ { "Id": "c1", "Names": ["/web-1"] } ]""");
    }

    #endregion // Static methods

    #region Private methods

    /// <summary>
    /// Sample one container whose stats body is the base sample with one member replaced and assert the values read
    /// </summary>
    /// <param name="path">Dot-separated path of the replaced member</param>
    /// <param name="replacementJson">JSON that replaces the member</param>
    /// <param name="expectedCpu">Expected CPU percent</param>
    /// <param name="expectedUsage">Expected memory usage</param>
    /// <param name="expectedLimit">Expected memory limit</param>
    /// <param name="expectedRx">Expected received network total</param>
    /// <param name="expectedTx">Expected transmitted network total</param>
    /// <returns>Task</returns>
    private async Task AssertSampledValuesAsync(string path,
                                                string replacementJson,
                                                long expectedCpu,
                                                long expectedUsage,
                                                long expectedLimit,
                                                long expectedRx,
                                                long expectedTx)
    {
        var logger = new TestLogger<DockerInstanceClient>();
        var statsBody = CreateStatsBody(path, replacementJson);

        using var handler = new DelegateHttpMessageHandler(request => Respond(request, statsBody));
        using var httpClient = CreateHttpClient(handler);

        var client = new DockerInstanceClient(logger, (_, _) => httpClient);

        var result = await client.CollectContainerResourceUsageAsync(CreateInstanceOptions(), TestContext.CancellationToken).ConfigureAwait(false);
        var context = $"{path} = {replacementJson}";

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, $"The round must succeed for {context}");
        Assert.DoesNotContain(entry => entry.EventId.Id == 3111, logger.Entries, $"The container must not be left out for {context}");
        Assert.IsNotNull(result.Data, $"The round must return the samples for {context}");

        var sample = Assert.ContainsSingle(result.Data, $"The container must be sampled for {context}");

        Assert.AreEqual((decimal)expectedCpu, sample.CpuPercent, $"CPU percent for {context}");
        Assert.AreEqual(expectedUsage, sample.MemoryUsageBytes, $"Memory usage for {context}");
        Assert.AreEqual(expectedLimit, sample.MemoryLimitBytes, $"Memory limit for {context}");
        Assert.AreEqual(expectedRx, sample.NetworkRxBytesTotal, $"Received network total for {context}");
        Assert.AreEqual(expectedTx, sample.NetworkTxBytesTotal, $"Transmitted network total for {context}");
    }

    #endregion // Private methods
}