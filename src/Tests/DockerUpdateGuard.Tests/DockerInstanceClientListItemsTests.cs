using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Docker;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for the reading of list items and of the string list items of <see cref="DockerInstanceClient"/>
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
    /// Image inspect built from the field examples of the <c>ImageInspect</c> definition of the Docker Engine API definition (API v1.41),
    /// with the placeholder <c>ITEM</c> where the tests insert an item that is not a string
    /// </summary>
    private const string ImageInspectDefinitionSample = """
                                                        {
                                                          "Id": "sha256:ec3f0931a6e6b6855d76b2d7b0be30e81860baccd891b2e243280bf1cd8ad710",
                                                          "RepoTags": ["example:1.0", ITEM, "example:latest", "example:stable", "internal.registry.example.com:5000/example:1.0"],
                                                          "RepoDigests": ["example@sha256:afcc7f1ac1b49db317a7196c902e61c6c3c4607d63599ee1a82d702d249a0ccb", ITEM, "internal.registry.example.com:5000/example@sha256:b69959407d21e8a062e0416bf13405bb2b71ed7a84dde4158ebafacfa06f5578"],
                                                          "Config": { "Env": [ITEM, "PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"] },
                                                          "RootFS": { "Type": "layers", "Layers": ["sha256:1834950e52ce4d5a88a1bbd131c537f4d0e56d10ff0dd69e66be3b7dfa9df7e6", ITEM, "sha256:5f70bf18a086007016e948b04aed3b82103a36bea41755b6cddfaf10ace3c6ef"] }
                                                        }
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
    /// Verify an image history leaves out a tag that is not a JSON string and reads the other tags and entries as without it
    /// </summary>
    /// <param name="itemJson">JSON of the tag that is not a string</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    [DataRow("7")]
    [DataRow("1.5")]
    [DataRow("true")]
    [DataRow("false")]
    [DataRow("null")]
    [DataRow("{ \"a\": \"b\" }")]
    [DataRow("[\"x\"]")]
    public async Task DockerInstanceClientGetImageHistoryAsyncWhenTagIsNoStringSkipsItAsync(string itemJson)
    {
        const string originalTags = "\"Tags\": [\"ubuntu:lucid\", \"ubuntu:10.04\"]";

        var json = HistorySample.Replace(originalTags, $"\"Tags\": [\"ubuntu:lucid\", {itemJson}, \"ubuntu:10.04\"]", StringComparison.Ordinal);

        Assert.AreNotEqual(HistorySample, json, "The test input must carry the item");

        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = CreateHttpClient(handler);

        handler.AddJsonResponse("https://docker.example.test/v1.41/images/sha256%3Alocal-image/history", json);

        var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(), (_, _) => httpClient);

        var result = await client.GetImageHistoryAsync(CreateInstanceOptions(), "sha256:local-image", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, $"A tag {itemJson} that is no string must not fail the history");
        Assert.IsNotNull(result.Data, "Image history must expose the parsed entries");
        Assert.HasCount(3, result.Data, "Image history must expose the three entries of the sample");
        AssertItems(result.Data[0].Tags, "The first entry must keep its two string tags in order", "ubuntu:lucid", "ubuntu:10.04");
        Assert.AreEqual("/bin/sh -c #(nop) ADD file:eb15dbd63394e063b805a3c32ca7bf0266ef64676d5a6fab4801f2e81e2a5148 in /", result.Data[0].CreatedBy, "The first entry must keep its other fields");
        AssertItems(result.Data[2].Tags, "The third entry must keep its tags", "scratch12:latest", "scratch:latest");
    }

    /// <summary>
    /// Verify an image inspect leaves out an item that is not a JSON string in the repo tags, repo digests, environment variables and layers
    /// </summary>
    /// <param name="itemJson">JSON of the item that is not a string</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    [DataRow("7")]
    [DataRow("1.5")]
    [DataRow("true")]
    [DataRow("false")]
    [DataRow("null")]
    [DataRow("{ \"a\": \"b\" }")]
    [DataRow("[\"x\"]")]
    public async Task DockerInstanceClientInspectImageAsyncWhenListItemIsNoStringSkipsItAsync(string itemJson)
    {
        var json = ImageInspectDefinitionSample.Replace("ITEM", itemJson, StringComparison.Ordinal);

        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = CreateHttpClient(handler);

        handler.AddJsonResponse("https://docker.example.test/v1.41/images/example%3A1.0/json", json);

        var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(), (_, _) => httpClient);

        var result = await client.InspectImageAsync(CreateInstanceOptions(), "example:1.0", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, $"An item {itemJson} that is no string must not fail the inspect");
        Assert.IsNotNull(result.Data, "Inspect must expose the parsed image");
        Assert.AreEqual("sha256:ec3f0931a6e6b6855d76b2d7b0be30e81860baccd891b2e243280bf1cd8ad710", result.Data.Id, "The ID must be read");
        AssertItems(result.Data.RepoTags, "The repo tags must hold exactly the string items in order", "example:1.0", "example:latest", "example:stable", "internal.registry.example.com:5000/example:1.0");
        AssertItems(result.Data.RepoDigests,
                    "The repo digests must hold exactly the string items in order",
                    "example@sha256:afcc7f1ac1b49db317a7196c902e61c6c3c4607d63599ee1a82d702d249a0ccb",
                    "internal.registry.example.com:5000/example@sha256:b69959407d21e8a062e0416bf13405bb2b71ed7a84dde4158ebafacfa06f5578");
        AssertItems(result.Data.EnvironmentVariables, "The environment variables must hold exactly the string items", "PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin");
        AssertItems(result.Data.RootFsLayers,
                    "The layers must hold exactly the string items in order",
                    "sha256:1834950e52ce4d5a88a1bbd131c537f4d0e56d10ff0dd69e66be3b7dfa9df7e6",
                    "sha256:5f70bf18a086007016e948b04aed3b82103a36bea41755b6cddfaf10ace3c6ef");
    }

    /// <summary>
    /// Verify discovery resolves the digest of an image whose repo digests hold an item that is not a JSON string next to the matching digest
    /// </summary>
    /// <param name="itemJson">JSON of the item that is not a string</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    [DataRow("7")]
    [DataRow("true")]
    [DataRow("{ \"a\": \"b\" }")]
    public async Task DockerInstanceClientDiscoverContainersAsyncWhenRepoDigestIsNoStringResolvesDigestAsync(string itemJson)
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = CreateHttpClient(handler);

        handler.AddJsonResponse("https://docker.example.test/v1.41/containers/json?all=1", $"[ {ContainerSample} ]");
        handler.AddJsonResponse("https://docker.example.test/v1.41/images/d74508fb6632491cea586a1fd7d748dfc5274cd6fdfedee309ecf33cbc24e5f8/json",
                                $$"""{ "RepoDigests": [{{itemJson}}, "ubuntu@sha256:7777777777777777777777777777777777777777777777777777777777777777"] }""");

        var logger = new TestLogger<DockerInstanceClient>();
        var client = new DockerInstanceClient(logger, (_, _) => httpClient);

        var result = await client.DiscoverContainersAsync(CreateInstanceOptions(), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, $"A repo digest {itemJson} that is no string must not fail the discovery");
        Assert.IsNotNull(result.Data, "Discovery must return the containers");

        var container = Assert.ContainsSingle(result.Data, "The container must be returned");

        Assert.AreEqual("sha256:" + new string('7', 64), container.ImageDigest, "The digest next to the item must be resolved");
        Assert.DoesNotContain(entry => entry.EventId.Id == 3112, logger.Entries, "No skipped image inspect may be logged");
    }

    /// <summary>
    /// Verify an image inspect answer that is not a JSON object still fails with the fixed unexpected-error reason
    /// </summary>
    /// <param name="bodyJson">JSON of the answer</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    [DataRow("[]")]
    [DataRow("\"text\"")]
    [DataRow("7")]
    public async Task DockerInstanceClientInspectImageAsyncWhenBodyIsNoObjectReturnsUnexpectedErrorAsync(string bodyJson)
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = CreateHttpClient(handler);

        handler.AddJsonResponse("https://docker.example.test/v1.41/images/sha256%3Alocal-image/json", bodyJson);

        var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(), (_, _) => httpClient);

        var result = await client.InspectImageAsync(CreateInstanceOptions(), "sha256:local-image", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, $"An answer {bodyJson} that is no object must fail the inspect");
        Assert.AreEqual("Docker image inspect failed for 'Production': unexpected error", result.Message, "The failure must carry the fixed reason");
    }

    /// <summary>
    /// Verify an image history answer that is not a JSON array still fails with the fixed unexpected-error reason
    /// </summary>
    /// <param name="bodyJson">JSON of the answer</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    [DataRow("{}")]
    [DataRow("\"text\"")]
    [DataRow("7")]
    public async Task DockerInstanceClientGetImageHistoryAsyncWhenBodyIsNoArrayReturnsUnexpectedErrorAsync(string bodyJson)
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = CreateHttpClient(handler);

        handler.AddJsonResponse("https://docker.example.test/v1.41/images/sha256%3Alocal-image/history", bodyJson);

        var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(), (_, _) => httpClient);

        var result = await client.GetImageHistoryAsync(CreateInstanceOptions(), "sha256:local-image", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, $"An answer {bodyJson} that is no array must fail the history");
        Assert.AreEqual("Docker image history failed for 'Production': unexpected error", result.Message, "The failure must carry the fixed reason");
    }

    /// <summary>
    /// Assert a list holds exactly the expected items in order
    /// </summary>
    /// <param name="actual">Actual list</param>
    /// <param name="message">Assertion message</param>
    /// <param name="expected">Expected items</param>
    private static void AssertItems(IReadOnlyList<string> actual, string message, params string[] expected)
    {
        Assert.AreEqual(string.Join("\n", expected), string.Join("\n", actual), message);
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