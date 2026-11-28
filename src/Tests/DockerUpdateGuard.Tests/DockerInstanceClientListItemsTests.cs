using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Docker;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for the reading of list items of <see cref="DockerInstanceClient"/>
/// </summary>
[TestClass]
public class DockerInstanceClientListItemsTests
{
    #region Constants

    /// <summary>
    /// First item of the container list example of the Docker Engine API definition (API v1.41)
    /// </summary>
    private const string ContainerSample = """
                                           {
                                             "Id": "8dfafdbc3a40",
                                             "Names": ["/boring_feynman"],
                                             "Image": "ubuntu:latest",
                                             "ImageID": "d74508fb6632491cea586a1fd7d748dfc5274cd6fdfedee309ecf33cbc24e5f8",
                                             "Command": "echo 1",
                                             "Created": 1367854155,
                                             "State": "Exited",
                                             "Status": "Exit 0",
                                             "Labels": {
                                               "com.example.vendor": "Acme",
                                               "com.example.license": "GPL",
                                               "com.example.version": "1.0"
                                             }
                                           }
                                           """;

    /// <summary>
    /// Image inspect answer of the image of the container sample
    /// </summary>
    private const string ImageInspectSample = """
                                              { "RepoDigests": ["ubuntu@sha256:7777777777777777777777777777777777777777777777777777777777777777"] }
                                              """;

    /// <summary>
    /// Image history example of the Docker Engine API definition, with a generated maintainer
    /// </summary>
    private const string HistorySample = """
                                         [
                                           {
                                             "Id": "3db9c44f45209632d6050b35958829c3a2aa256d81b9a7be45b362ff85c54710",
                                             "Created": 1398108230,
                                             "CreatedBy": "/bin/sh -c #(nop) ADD file:eb15dbd63394e063b805a3c32ca7bf0266ef64676d5a6fab4801f2e81e2a5148 in /",
                                             "Tags": ["ubuntu:lucid", "ubuntu:10.04"],
                                             "Size": 182964289,
                                             "Comment": ""
                                           },
                                           {
                                             "Id": "6cfa4d1f33fb861d4d114f43b25abd0ac737509268065cdfd69d544a59c85ab8",
                                             "Created": 1398108222,
                                             "CreatedBy": "/bin/sh -c #(nop) MAINTAINER Example Maintainer <maintainer@example.test>",
                                             "Tags": [],
                                             "Size": 0,
                                             "Comment": ""
                                           },
                                           {
                                             "Id": "511136ea3c5a64f264b78b5433614aec563103b4d4702f3ba7d4d2698e22c158",
                                             "Created": 1371157430,
                                             "CreatedBy": "",
                                             "Tags": ["scratch12:latest", "scratch:latest"],
                                             "Size": 0,
                                             "Comment": "Imported from -"
                                           }
                                         ]
                                         """;

    /// <summary>
    /// Text between the first and the second entry of the history sample, after which the tests insert the item
    /// </summary>
    private const string HistorySampleFirstEntryEnd = "\"Comment\": \"\"\r\n  },\r\n";

    #endregion // Constants

    #region Properties

    /// <summary>
    /// Context for the tests
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify discovery leaves out a list item that is not a JSON object and returns the container next to it
    /// </summary>
    /// <param name="itemJson">JSON of the item that is not an object</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    [DataRow("\"text\"")]
    [DataRow("7")]
    [DataRow("true")]
    [DataRow("false")]
    [DataRow("null")]
    [DataRow("[{ \"Id\": \"nested\" }]")]
    public async Task DockerInstanceClientDiscoverContainersAsyncWhenListItemIsNoObjectSkipsItAsync(string itemJson)
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = CreateHttpClient(handler);

        handler.AddJsonResponse("https://docker.example.test/v1.41/containers/json?all=1", $"[ {itemJson}, {ContainerSample} ]");
        handler.AddJsonResponse("https://docker.example.test/v1.41/images/d74508fb6632491cea586a1fd7d748dfc5274cd6fdfedee309ecf33cbc24e5f8/json", ImageInspectSample);

        var logger = new TestLogger<DockerInstanceClient>();
        var client = new DockerInstanceClient(logger, (_, _) => httpClient);

        var result = await client.DiscoverContainersAsync(CreateInstanceOptions(), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, $"A list item {itemJson} that is no object must not fail the discovery");
        Assert.IsNotNull(result.Data, "Discovery must return the containers");

        var container = Assert.ContainsSingle(result.Data, "Only the container object must be returned");

        Assert.AreEqual("8dfafdbc3a40", container.ContainerId, "The container object next to the item must be returned");
        Assert.DoesNotContain(entry => entry.EventId.Id == 3104, logger.Entries, "No failed discovery may be logged");
    }

    /// <summary>
    /// Verify resource sampling leaves out a list item that is not a JSON object, samples the container next to it and sends one stats request
    /// </summary>
    /// <param name="itemJson">JSON of the item that is not an object</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    [DataRow("\"text\"")]
    [DataRow("7")]
    [DataRow("true")]
    [DataRow("false")]
    [DataRow("null")]
    [DataRow("[{ \"Id\": \"nested\" }]")]
    public async Task DockerInstanceClientCollectContainerResourceUsageAsyncWhenListItemIsNoObjectSkipsItAsync(string itemJson)
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = CreateHttpClient(handler);

        handler.AddJsonResponse("https://docker.example.test/v1.41/containers/json", $"[ {itemJson}, {ContainerSample} ]");
        handler.AddJsonResponse("https://docker.example.test/v1.41/containers/8dfafdbc3a40/stats?stream=false", """{ "read": "2025-06-01T12:00:00Z" }""");

        var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(), (_, _) => httpClient);

        var result = await client.CollectContainerResourceUsageAsync(CreateInstanceOptions(), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, $"A list item {itemJson} that is no object must not fail the sampling");
        Assert.IsNotNull(result.Data, "Sampling must return the samples");

        var sample = Assert.ContainsSingle(result.Data, "Only the container object must be sampled");

        Assert.AreEqual("8dfafdbc3a40", sample.ContainerId, "The sample must belong to the container object");
        Assert.ContainsSingle(request => request.RequestUri.Contains("/stats", StringComparison.Ordinal),
                              handler.Requests,
                              "Exactly one stats request must be sent");
    }

    /// <summary>
    /// Verify resource sampling of a list that holds only items that are not JSON objects succeeds without a sample and without a stats request
    /// </summary>
    /// <param name="itemJson">JSON of the item that is not an object</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    [DataRow("\"text\"")]
    [DataRow("7")]
    [DataRow("true")]
    [DataRow("false")]
    [DataRow("null")]
    [DataRow("[{ \"Id\": \"nested\" }]")]
    public async Task DockerInstanceClientCollectContainerResourceUsageAsyncWhenListHoldsOnlyNoObjectItemsReturnsEmptySucceededAsync(string itemJson)
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = CreateHttpClient(handler);

        handler.AddJsonResponse("https://docker.example.test/v1.41/containers/json", $"[ {itemJson}, {itemJson} ]");

        var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(), (_, _) => httpClient);

        var result = await client.CollectContainerResourceUsageAsync(CreateInstanceOptions(), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, $"A list of only {itemJson} items must not fail the sampling");
        Assert.IsNotNull(result.Data, "Sampling must return a sample list");
        Assert.IsEmpty(result.Data, "No sample must be returned for items that are no objects");
        Assert.DoesNotContain(request => request.RequestUri.Contains("/stats", StringComparison.Ordinal),
                              handler.Requests,
                              "No stats request must be sent");
    }

    /// <summary>
    /// Verify an image history leaves out an entry that is not a JSON object and reads the other entries in order
    /// </summary>
    /// <param name="itemJson">JSON of the entry that is not an object</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    [DataRow("\"text\"")]
    [DataRow("7")]
    [DataRow("true")]
    [DataRow("false")]
    [DataRow("null")]
    [DataRow("[{ \"Id\": \"nested\" }]")]
    public async Task DockerInstanceClientGetImageHistoryAsyncWhenEntryIsNoObjectSkipsItAsync(string itemJson)
    {
        var sample = HistorySample.ReplaceLineEndings("\r\n");
        var insertAt = sample.IndexOf(HistorySampleFirstEntryEnd, StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, insertAt, "The test input must contain the end of the first entry");

        var json = sample.Insert(insertAt + HistorySampleFirstEntryEnd.Length, $"{itemJson},\r\n");

        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = CreateHttpClient(handler);

        handler.AddJsonResponse("https://docker.example.test/v1.41/images/sha256%3Alocal-image/history", json);

        var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(), (_, _) => httpClient);

        var result = await client.GetImageHistoryAsync(CreateInstanceOptions(), "sha256:local-image", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, $"A history entry {itemJson} that is no object must not fail the history");
        Assert.IsNotNull(result.Data, "Image history must expose the parsed entries");
        Assert.HasCount(3, result.Data, "Image history must expose the three entries of the sample and nothing for the left-out entry");
        Assert.AreEqual(new DateTimeOffset(2014, 4, 21, 19, 23, 50, TimeSpan.Zero), result.Data[0].CreatedAtUtc, "The first entry must keep its place");
        Assert.AreEqual(new DateTimeOffset(2014, 4, 21, 19, 23, 42, TimeSpan.Zero), result.Data[1].CreatedAtUtc, "The second entry must follow the first");
        Assert.AreEqual(new DateTimeOffset(2013, 6, 13, 21, 3, 50, TimeSpan.Zero), result.Data[2].CreatedAtUtc, "The third entry must come last");
    }

    /// <summary>
    /// Verify discovery leaves out a container whose ID is missing, null, a number, empty, blank or only a control character and returns the container next to it
    /// </summary>
    /// <param name="idMemberJson">JSON member that replaces the ID member of the sample</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    [DataRow("\"Unrelated\": 1")]
    [DataRow("\"Id\": null")]
    [DataRow("\"Id\": 7")]
    [DataRow("\"Id\": \"\"")]
    [DataRow("\"Id\": \"   \"")]
    [DataRow("\"Id\": \"\\u0001\"")]
    public async Task DockerInstanceClientDiscoverContainersAsyncWhenContainerIdIsMissingOrBlankSkipsContainerAsync(string idMemberJson)
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = CreateHttpClient(handler);

        handler.AddJsonResponse("https://docker.example.test/v1.41/containers/json?all=1", $"[ {CreateContainerWithoutValidId(idMemberJson)}, {ContainerSample} ]");
        handler.AddJsonResponse("https://docker.example.test/v1.41/images/d74508fb6632491cea586a1fd7d748dfc5274cd6fdfedee309ecf33cbc24e5f8/json", ImageInspectSample);

        var logger = new TestLogger<DockerInstanceClient>();
        var client = new DockerInstanceClient(logger, (_, _) => httpClient);

        var result = await client.DiscoverContainersAsync(CreateInstanceOptions(), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, $"A container with {idMemberJson} must not fail the discovery");
        Assert.IsNotNull(result.Data, "Discovery must return the containers");

        var container = Assert.ContainsSingle(result.Data, "Only the container with a storable ID must be returned");

        Assert.AreEqual("8dfafdbc3a40", container.ContainerId, "The container with the ID must be returned");
    }

    /// <summary>
    /// Verify discovery of a list whose only object has no ID succeeds without a container and sends no image inspect request
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task DockerInstanceClientDiscoverContainersAsyncWhenListHoldsOnlyContainersWithoutIdReturnsEmptySucceededAsync()
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = CreateHttpClient(handler);

        handler.AddJsonResponse("https://docker.example.test/v1.41/containers/json?all=1", $"[ {CreateContainerWithoutValidId("\"Unrelated\": 1")} ]");

        var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(), (_, _) => httpClient);

        var result = await client.DiscoverContainersAsync(CreateInstanceOptions(), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "A list of only containers without an ID must not fail the discovery");
        Assert.IsNotNull(result.Data, "Discovery must return a container list");
        Assert.IsEmpty(result.Data, "No container without an ID may be returned");
        Assert.DoesNotContain(request => request.RequestUri.Contains("/images/", StringComparison.Ordinal),
                              handler.Requests,
                              "No image inspect request may be sent");
    }

    /// <summary>
    /// Verify resource sampling leaves out a container whose ID is missing, null, a number, empty, blank or only a control character, without a stats request
    /// </summary>
    /// <param name="idMemberJson">JSON member that replaces the ID member of the sample</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    [DataRow("\"Unrelated\": 1")]
    [DataRow("\"Id\": null")]
    [DataRow("\"Id\": 7")]
    [DataRow("\"Id\": \"\"")]
    [DataRow("\"Id\": \"   \"")]
    [DataRow("\"Id\": \"\\u0001\"")]
    public async Task DockerInstanceClientCollectContainerResourceUsageAsyncWhenContainerIdIsMissingOrBlankSkipsContainerAsync(string idMemberJson)
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = CreateHttpClient(handler);

        handler.AddJsonResponse("https://docker.example.test/v1.41/containers/json", $"[ {CreateContainerWithoutValidId(idMemberJson)}, {ContainerSample} ]");
        handler.AddJsonResponse("https://docker.example.test/v1.41/containers/8dfafdbc3a40/stats?stream=false", """{ "read": "2025-06-01T12:00:00Z" }""");

        var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(), (_, _) => httpClient);

        var result = await client.CollectContainerResourceUsageAsync(CreateInstanceOptions(), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, $"A container with {idMemberJson} must not fail the sampling");
        Assert.IsNotNull(result.Data, "Sampling must return the samples");

        var sample = Assert.ContainsSingle(result.Data, "Only the container with a storable ID must be sampled");

        Assert.AreEqual("8dfafdbc3a40", sample.ContainerId, "The sample must belong to the container with the ID");
        Assert.ContainsSingle(request => request.RequestUri.Contains("/stats", StringComparison.Ordinal),
                              handler.Requests,
                              "Exactly one stats request must be sent");
    }

    /// <summary>
    /// Create the container sample with another name and its ID member replaced by the given member
    /// </summary>
    /// <param name="idMemberJson">JSON member that replaces the ID member of the sample</param>
    /// <returns>Container JSON</returns>
    private static string CreateContainerWithoutValidId(string idMemberJson)
    {
        return ContainerSample.Replace("\"Id\": \"8dfafdbc3a40\"", idMemberJson, StringComparison.Ordinal)
                              .Replace("/boring_feynman", "/left_out", StringComparison.Ordinal);
    }

    /// <summary>
    /// Create an HTTP client for the Docker Engine of the tests
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

    #endregion // Methods
}