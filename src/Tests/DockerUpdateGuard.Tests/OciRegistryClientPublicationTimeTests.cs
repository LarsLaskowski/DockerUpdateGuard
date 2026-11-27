using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

using Microsoft.Extensions.Logging;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for the publication time of the tags <see cref="OciRegistryClient"/> reports
/// </summary>
[TestClass]
public class OciRegistryClientPublicationTimeTests
{
    #region Constants

    /// <summary>
    /// Address of the manifest of the tag under test
    /// </summary>
    private const string ManifestUri = "https://ghcr.io/v2/acme/api/manifests/1.0";

    /// <summary>
    /// Address of the configuration blob of the real platform manifest
    /// </summary>
    private const string RealBlobUri = "https://ghcr.io/v2/acme/api/blobs/sha256%3A313029fcb3acece69f9f3294eb1344c8e1ade8ab93d3b42b03e368994da1b587";

    /// <summary>
    /// Address of the real platform manifest
    /// </summary>
    private const string RealPlatformManifestUri = "https://ghcr.io/v2/acme/api/manifests/sha256%3Afbbd521bd989893b6c350877d17e1eb1126b6a1bfb715c28e6945f1c82ae0093";

    /// <summary>
    /// Address of the simple configuration blob
    /// </summary>
    private const string BlobUri = "https://ghcr.io/v2/acme/api/blobs/sha256%3Aconfig";

    /// <summary>
    /// Digest the registry reports for the tag
    /// </summary>
    private const string TagDigest = "sha256:tag-digest";

    /// <summary>
    /// Manifest without a top-level created time and with a configuration digest
    /// </summary>
    private const string ManifestWithoutCreated = """{ "schemaVersion": 2, "config": { "digest": "sha256:config" } }""";

    /// <summary>
    /// Manifest of the linux/amd64 platform (real sample of ghcr.io/home-assistant/home-assistant:stable, reduced to two layers)
    /// </summary>
    private const string RealPlatformManifest = """
                                                {
                                                  "schemaVersion": 2,
                                                  "mediaType": "application/vnd.oci.image.manifest.v1+json",
                                                  "config": {
                                                    "mediaType": "application/vnd.oci.image.config.v1+json",
                                                    "digest": "sha256:313029fcb3acece69f9f3294eb1344c8e1ade8ab93d3b42b03e368994da1b587",
                                                    "size": 14998
                                                  },
                                                  "layers": [
                                                    {
                                                      "mediaType": "application/vnd.oci.image.layer.v1.tar+zstd",
                                                      "digest": "sha256:6f6d00869972c40dea99d3d113335842e71ee3a6f9ce362a0f152de7983aed94",
                                                      "size": 3812482
                                                    },
                                                    {
                                                      "mediaType": "application/vnd.oci.image.layer.v1.tar+zstd",
                                                      "digest": "sha256:d2b3247c142d41b9e4b99b5ac19dcca45d2890db72163f624e0afe35fc35466a",
                                                      "size": 14002470
                                                    }
                                                  ]
                                                }
                                                """;

    /// <summary>
    /// Configuration blob of the real platform manifest (real sample, reduced to the architecture, the configuration, the creation time and the operating system)
    /// </summary>
    private const string RealConfig = """
                                      {
                                        "architecture": "amd64",
                                        "config": {
                                          "Env": [
                                            "PATH=/usr/local/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin",
                                            "LANG=C.UTF-8",
                                            "S6_BEHAVIOUR_IF_STAGE2_FAILS=2",
                                            "S6_CMD_WAIT_FOR_SERVICES_MAXTIME=0",
                                            "S6_CMD_WAIT_FOR_SERVICES=1",
                                            "S6_SERVICES_READYTIME=50",
                                            "PIP_EXTRA_INDEX_URL=https://wheels.home-assistant.io/musllinux-index/",
                                            "UV_EXTRA_INDEX_URL=https://wheels.home-assistant.io/musllinux-index/",
                                            "S6_SERVICES_GRACETIME=240000",
                                            "UV_SYSTEM_PYTHON=true",
                                            "UV_NO_CACHE=true"
                                          ],
                                          "Entrypoint": [
                                            "/init"
                                          ],
                                          "WorkingDir": "/config",
                                          "Labels": {
                                            "io.hass.arch": "amd64",
                                            "io.hass.base.arch": "amd64",
                                            "io.hass.base.image": "ghcr.io/home-assistant/base:3.24",
                                            "io.hass.base.name": "python",
                                            "io.hass.base.version": "2026.10.3",
                                            "io.hass.type": "core",
                                            "io.hass.version": "2026.11.3",
                                            "org.opencontainers.image.authors": "The Home Assistant Authors",
                                            "org.opencontainers.image.created": "2026-11-23 20:54:07+00:00",
                                            "org.opencontainers.image.description": "Open-source home automation platform running on Python 3",
                                            "org.opencontainers.image.documentation": "https://www.home-assistant.io/docs/",
                                            "org.opencontainers.image.licenses": "Apache-2.0",
                                            "org.opencontainers.image.source": "https://github.com/home-assistant/core",
                                            "org.opencontainers.image.title": "Home Assistant",
                                            "org.opencontainers.image.url": "https://www.home-assistant.io/",
                                            "org.opencontainers.image.version": "2026.11.3"
                                          },
                                          "Shell": [
                                            "/bin/ash",
                                            "-o",
                                            "pipefail",
                                            "-c"
                                          ]
                                        },
                                        "created": "2026-11-23T20:55:47.388026494Z",
                                        "os": "linux"
                                      }
                                      """;

    /// <summary>
    /// Image index of the real sample, with an attestation manifest after each platform manifest
    /// </summary>
    private const string RealIndex = """
                                     {
                                       "schemaVersion": 2,
                                       "mediaType": "application/vnd.oci.image.index.v1+json",
                                       "manifests": [
                                         {
                                           "mediaType": "application/vnd.oci.image.manifest.v1+json",
                                           "digest": "sha256:fbbd521bd989893b6c350877d17e1eb1126b6a1bfb715c28e6945f1c82ae0093",
                                           "size": 4488,
                                           "platform": {
                                             "architecture": "amd64",
                                             "os": "linux"
                                           }
                                         },
                                         {
                                           "mediaType": "application/vnd.oci.image.manifest.v1+json",
                                           "digest": "sha256:74ce6925ef89e320a513225ca1ceb74ee90879400cd9fc89303e7bf420ea630f",
                                           "size": 838,
                                           "annotations": {
                                             "vnd.docker.reference.digest": "sha256:fbbd521bd989893b6c350877d17e1eb1126b6a1bfb715c28e6945f1c82ae0093",
                                             "vnd.docker.reference.type": "attestation-manifest"
                                           },
                                           "platform": {
                                             "architecture": "unknown",
                                             "os": "unknown"
                                           }
                                         },
                                         {
                                           "mediaType": "application/vnd.oci.image.manifest.v1+json",
                                           "digest": "sha256:ee500645d61b5483d0b05eebd4f2de22c390618ec178df0e1114a77d40d6d650",
                                           "size": 4488,
                                           "platform": {
                                             "architecture": "arm64",
                                             "os": "linux"
                                           }
                                         },
                                         {
                                           "mediaType": "application/vnd.oci.image.manifest.v1+json",
                                           "digest": "sha256:9902d80d91773d0f4147cc401bb19ea03637c1e80a5f6f4e8334be1c493e2623",
                                           "size": 838,
                                           "annotations": {
                                             "vnd.docker.reference.digest": "sha256:ee500645d61b5483d0b05eebd4f2de22c390618ec178df0e1114a77d40d6d650",
                                             "vnd.docker.reference.type": "attestation-manifest"
                                           },
                                           "platform": {
                                             "architecture": "unknown",
                                             "os": "unknown"
                                           }
                                         }
                                       ]
                                     }
                                     """;

    #endregion // Constants

    #region Properties

    /// <summary>
    /// Context for the tests
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify a configuration blob whose created time has no offset gives a publication time in UTC
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    [OSCondition(OperatingSystems.Linux)]
    public async Task OciRegistryClientPublicationTimeGetTagAsyncConfigCreatedWithoutOffsetReadsUtcAsync()
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        handler.AddResponse(ManifestUri, CreateManifestResponse(TagDigest, ManifestWithoutCreated));
        handler.AddJsonResponse(BlobUri, """{ "created": "2026-03-04T05:06:07", "os": "linux" }""");

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        ExternalOperationResult<DockerHubTagData> result;

        using (new LocalTimeZoneScope("Etc/GMT+5"))
        {
            result = await client.GetTagAsync(CreateReference(), "linux", "amd64", TestContext.CancellationToken).ConfigureAwait(false);
        }

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The lookup must succeed");
        Assert.IsNotNull(result.Data, "The lookup must return tag data");
        Assert.IsNotNull(result.Data.PublishedAtUtc, "The publication time must be known");
        Assert.AreEqual(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero), result.Data.PublishedAtUtc.Value, "A created time without an offset must be read as UTC");
        Assert.AreEqual(TimeSpan.Zero, result.Data.PublishedAtUtc.Value.Offset, "The publication time must be kept with offset zero");
    }

    /// <summary>
    /// Verify a configuration blob whose created time is the epoch without an offset leaves the publication time unknown
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    [OSCondition(OperatingSystems.Linux)]
    public async Task OciRegistryClientPublicationTimeGetTagAsyncEpochCreatedWithoutOffsetLeavesTimeUnknownAsync()
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        handler.AddResponse(ManifestUri, CreateManifestResponse(TagDigest, ManifestWithoutCreated));
        handler.AddJsonResponse(BlobUri, """{ "created": "1970-01-01T00:00:00", "os": "linux" }""");

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        ExternalOperationResult<DockerHubTagData> result;

        using (new LocalTimeZoneScope("Etc/GMT+5"))
        {
            result = await client.GetTagAsync(CreateReference(), "linux", "amd64", TestContext.CancellationToken).ConfigureAwait(false);
        }

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The lookup must succeed");
        Assert.IsNotNull(result.Data, "The lookup must return tag data");
        Assert.IsNull(result.Data.PublishedAtUtc, "An epoch created time without an offset is the epoch in UTC and must leave the publication time unknown");
    }

    /// <summary>
    /// Verify a manifest without a created time takes the publication time from the configuration blob
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC6")]
    public async Task OciRegistryClientGetTagAsyncManifestWithoutCreatedUsesConfigCreatedAsync()
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        handler.AddResponse(ManifestUri, CreateManifestResponse(TagDigest, RealPlatformManifest));
        handler.AddJsonResponse(RealBlobUri, RealConfig);

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.GetTagAsync(CreateReference(), "linux", "amd64", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The lookup must succeed");
        Assert.IsNotNull(result.Data, "The lookup must return tag data");
        Assert.AreEqual(DateTimeOffset.Parse("2026-11-23T20:55:47.388026494Z", CultureInfo.InvariantCulture),
                        result.Data.PublishedAtUtc,
                        "The publication time must be the created time of the configuration blob");
        Assert.AreEqual(TagDigest, result.Data.Digest, "The digest must stay the one of the manifest answer");
        Assert.AreSequenceEqual([ManifestUri, RealBlobUri],
                                RequestedUris(handler),
                                "Exactly the manifest and its configuration blob must be requested");
    }

    /// <summary>
    /// Verify an index takes the publication time from the configuration blob of the selected platform manifest
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC6")]
    public async Task OciRegistryClientGetTagAsyncIndexUsesSelectedPlatformConfigCreatedAsync()
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        handler.AddResponse(ManifestUri, CreateIndexResponse("sha256:index-digest", RealIndex));
        handler.AddResponse(RealPlatformManifestUri, CreateManifestResponse("sha256:platform-digest", RealPlatformManifest));
        handler.AddJsonResponse(RealBlobUri, RealConfig);

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.GetTagAsync(CreateReference(), "linux", "amd64", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The lookup must succeed");
        Assert.IsNotNull(result.Data, "The lookup must return tag data");
        Assert.AreEqual(DateTimeOffset.Parse("2026-11-23T20:55:47.388026494Z", CultureInfo.InvariantCulture),
                        result.Data.PublishedAtUtc,
                        "The publication time must be the created time of the configuration of the selected platform manifest");
        Assert.AreEqual("sha256:index-digest", result.Data.Digest, "The digest must stay the one of the index answer");
        Assert.AreSequenceEqual([ManifestUri, RealPlatformManifestUri, RealBlobUri],
                                RequestedUris(handler),
                                "The index, the selected platform manifest and its configuration blob must be requested");
    }

    /// <summary>
    /// Verify a created time of the blob that is unusable leaves the publication time unknown
    /// </summary>
    /// <param name="createdMember">Created member of the configuration blob, empty for none</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC7")]
    [DataRow("""
             "created": "1970-01-01T00:00:00Z",
             """)]
    [DataRow("""
             "created": "0001-01-01T00:00:00Z",
             """)]
    [DataRow("""
             "created": "1969-12-31T23:59:59Z",
             """)]
    [DataRow("")]
    [DataRow("""
             "created": 1700000000,
             """)]
    [DataRow("""
             "created": "not a date",
             """)]
    public async Task OciRegistryClientGetTagAsyncUnusableConfigCreatedReturnsUnknownPublishedTimeAsync(string createdMember)
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        handler.AddResponse(ManifestUri, CreateManifestResponse(TagDigest, ManifestWithoutCreated));
        handler.AddJsonResponse(BlobUri, $$"""{ "architecture": "amd64", {{createdMember}} "os": "linux" }""");

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.GetTagAsync(CreateReference(), "linux", "amd64", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "An unusable created time must not fail the lookup");
        Assert.IsNotNull(result.Data, "The lookup must return tag data");
        Assert.IsNull(result.Data.PublishedAtUtc, "An unusable created time must leave the publication time unknown");
        Assert.AreEqual(TagDigest, result.Data.Digest, "The digest must stay the one of the manifest answer");
        Assert.AreSequenceEqual([ManifestUri, BlobUri],
                                RequestedUris(handler),
                                "The configuration blob must have been requested once");
    }

    /// <summary>
    /// Verify a created time of the manifest at the epoch is unusable and falls back to the configuration blob
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC7")]
    public async Task OciRegistryClientGetTagAsyncEpochManifestCreatedFallsBackToConfigCreatedAsync()
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        handler.AddResponse(ManifestUri,
                            CreateManifestResponse(TagDigest, """{ "schemaVersion": 2, "created": "1970-01-01T00:00:00Z", "config": { "digest": "sha256:config" } }"""));
        handler.AddJsonResponse(BlobUri, """{ "created": "2026-03-04T05:06:07Z" }""");

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.GetTagAsync(CreateReference(), "linux", "amd64", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The lookup must succeed");
        Assert.IsNotNull(result.Data, "The lookup must return tag data");
        Assert.AreEqual(DateTimeOffset.Parse("2026-03-04T05:06:07Z", CultureInfo.InvariantCulture),
                        result.Data.PublishedAtUtc,
                        "An epoch created time of the manifest must not win over the configuration blob");
    }

    /// <summary>
    /// Verify a blob answer that is not a success leaves the publication time unknown without failing the lookup or logging a warning
    /// </summary>
    /// <param name="answer">Kind of the blob answer</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC7")]
    [DataRow("NotFound")]
    [DataRow("ServerError")]
    [DataRow("UnauthorizedWithoutChallenge")]
    [DataRow("UnauthorizedTokenRequestFails")]
    public async Task OciRegistryClientGetTagAsyncConfigBlobAnswerFailsReturnsSucceededWithoutPublishedTimeAsync(string answer)
    {
        var logger = new TestLogger<OciRegistryClient>();

        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        handler.AddResponse(ManifestUri, CreateManifestResponse(TagDigest, ManifestWithoutCreated));
        handler.AddResponse(BlobUri, CreateBlobAnswer(answer));

        var client = new OciRegistryClient(httpClient, logger);

        var result = await client.GetTagAsync(CreateReference(), "linux", "amd64", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "A blob answer that is not a success must not fail the tag lookup");
        Assert.IsNotNull(result.Data, "The lookup must return tag data");
        Assert.AreEqual(TagDigest, result.Data.Digest, "The digest must stay the one of the manifest answer");
        Assert.IsNull(result.Data.PublishedAtUtc, "A blob answer that is not a success must leave the publication time unknown");
        Assert.Contains(BlobUri, RequestedUris(handler), "The configuration blob must have been requested");
        Assert.DoesNotContain(entry => entry.EventId.Id == 2100, logger.Entries, "A status answer is no error and must not be logged as warning 2100");
    }

    /// <summary>
    /// Verify a blob request that errors leaves the publication time unknown, succeeds and logs one warning
    /// </summary>
    /// <param name="fault">Kind of the blob fault</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC7")]
    [DataRow("Transport")]
    [DataRow("Timeout")]
    [DataRow("MalformedJson")]
    public async Task OciRegistryClientGetTagAsyncConfigBlobErrorReturnsSucceededWithoutPublishedTimeAsync(string fault)
    {
        var logger = new TestLogger<OciRegistryClient>();
        var requestedUris = new List<string>();

        using var handler = new DelegateHttpMessageHandler(request => AnswerBlobWithFault(request, fault, requestedUris));
        using var httpClient = new HttpClient(handler)
                               {
                                   Timeout = TimeSpan.FromSeconds(30),
                               };

        var client = new OciRegistryClient(httpClient, logger);

        var result = await client.GetTagAsync(CreateReference(), "linux", "amd64", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "A blob error must not fail the tag lookup");
        Assert.IsNotNull(result.Data, "The lookup must return tag data");
        Assert.AreEqual(TagDigest, result.Data.Digest, "The digest must stay the one of the manifest answer");
        Assert.IsNull(result.Data.PublishedAtUtc, "A blob error must leave the publication time unknown");
        Assert.Contains(BlobUri, requestedUris, "The configuration blob must have been requested");

        var warning = Assert.ContainsSingle(entry => entry.EventId.Id == 2100 && entry.LogLevel == LogLevel.Warning,
                                            logger.Entries,
                                            "A blob error must be logged once as warning 2100");

        Assert.IsNotNull(warning.Exception, "The warning must carry the exception");
    }

    /// <summary>
    /// Verify a cancellation by the caller during the blob request still propagates
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC7")]
    public async Task OciRegistryClientGetTagAsyncCallerCancelledDuringConfigBlobThrowsAsync()
    {
        var logger = new TestLogger<OciRegistryClient>();

        using var callerCancellation = new CancellationTokenSource();
        using var handler = new DelegateHttpMessageHandler(request => request.RequestUri!.AbsolutePath.Contains("/blobs/", StringComparison.Ordinal)
                                                                          ? CancelCaller(callerCancellation)
                                                                          : CreateManifestResponse(TagDigest, ManifestWithoutCreated));
        using var httpClient = new HttpClient(handler)
                               {
                                   Timeout = TimeSpan.FromSeconds(30),
                               };

        var client = new OciRegistryClient(httpClient, logger);

        await Assert.ThrowsAsync<OperationCanceledException>(() => client.GetTagAsync(CreateReference(), "linux", "amd64", callerCancellation.Token),
                                                             "The caller's cancellation during the blob request must propagate").ConfigureAwait(false);

        Assert.DoesNotContain(entry => entry.EventId.Id == 2100, logger.Entries, "A cancellation by the caller must not be logged as a failed call");
    }

    /// <summary>
    /// Verify a tag listing with a publication bound drops the tags published before it by the configuration blob
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC8")]
    public async Task OciRegistryClientGetTagsAsyncPublishedSinceDropsOlderTagsAsync()
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        handler.AddJsonResponse("https://ghcr.io/v2/acme/api/tags/list?n=100", """{ "name": "acme/api", "tags": [ "1.0", "1.5", "2.0", "3.0" ] }""");
        AddTagWithBlob(handler, "1.0", "sha256:digest-10", "2020-01-01T00:00:00Z");
        AddTagWithBlob(handler, "1.5", "sha256:current", "2020-01-01T00:00:00Z");
        AddTagWithBlob(handler, "2.0", "sha256:digest-20", "2020-01-01T00:00:00Z");
        AddTagWithBlob(handler, "3.0", "sha256:digest-30", "2026-06-01T00:00:00Z");

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.GetTagsAsync("ghcr.io",
                                               "acme/api",
                                               queryOptions: new RegistryTagQueryOptions
                                                             {
                                                                 CurrentTag = "2.0",
                                                                 CurrentDigest = "sha256:current",
                                                                 PublishedSinceUtc = DateTimeOffset.Parse("2025-01-01T00:00:00Z", CultureInfo.InvariantCulture),
                                                             },
                                               cancellationToken: TestContext.CancellationToken)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The listing must succeed");
        Assert.IsNotNull(result.Data, "The listing must return the tags");
        Assert.AreSequenceEqual(["1.5", "2.0", "3.0"],
                                result.Data.Select(entity => entity.Tag)
                                           .ToArray(),
                                "The tag published before the bound must be dropped; the current tag and the tag with the current digest must stay");
    }

    /// <summary>
    /// Verify a manifest without a configuration digest sends no blob request
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC9")]
    public async Task OciRegistryClientGetTagAsyncManifestWithoutConfigDigestSendsNoBlobRequestAsync()
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        handler.AddResponse(ManifestUri, CreateManifestResponse(TagDigest, """{ "schemaVersion": 2 }"""));

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.GetTagAsync(CreateReference(), "linux", "amd64", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The lookup must succeed");
        Assert.IsNotNull(result.Data, "The lookup must return tag data");
        Assert.IsNull(result.Data.PublishedAtUtc, "Without a configuration digest the publication time must stay unknown");
        Assert.AreSequenceEqual([ManifestUri], RequestedUris(handler), "Only the manifest may be requested");
    }

    /// <summary>
    /// Verify a manifest with a usable created time wins and sends no blob request
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC6")]
    [TestCategory("AC9")]
    public async Task OciRegistryClientGetTagAsyncManifestCreatedWinsWithoutBlobRequestAsync()
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        handler.AddResponse(ManifestUri,
                            CreateManifestResponse(TagDigest, """{ "schemaVersion": 2, "created": "2026-05-01T10:00:00Z", "config": { "digest": "sha256:config" } }"""));
        handler.AddJsonResponse(BlobUri, """{ "created": "2020-01-01T00:00:00Z" }""");

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.GetTagAsync(CreateReference(), "linux", "amd64", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The lookup must succeed");
        Assert.IsNotNull(result.Data, "The lookup must return tag data");
        Assert.AreEqual(DateTimeOffset.Parse("2026-05-01T10:00:00Z", CultureInfo.InvariantCulture),
                        result.Data.PublishedAtUtc,
                        "A usable created time of the manifest must win");
        Assert.AreSequenceEqual([ManifestUri], RequestedUris(handler), "No blob may be requested when the manifest has a usable created time");
    }

    /// <summary>
    /// Verify a listing requests the blob of each inspected tag at most once and none for a tag whose manifest has a created time
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC9")]
    public async Task OciRegistryClientGetTagsAsyncSendsAtMostOneBlobRequestPerInspectedTagAsync()
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        handler.AddJsonResponse("https://ghcr.io/v2/acme/api/tags/list?n=100", """{ "name": "acme/api", "tags": [ "1.0", "2.0", "3.0", "4.0" ] }""");
        AddTagWithBlob(handler, "1.0", "sha256:digest-10", "2026-01-01T00:00:00Z");
        AddTagWithBlob(handler, "2.0", "sha256:digest-20", "2026-02-01T00:00:00Z");
        AddTagWithBlob(handler, "3.0", "sha256:digest-30", "2026-03-01T00:00:00Z");
        handler.AddResponse("https://ghcr.io/v2/acme/api/manifests/4.0",
                            CreateManifestResponse("sha256:digest-40", """{ "schemaVersion": 2, "created": "2026-04-01T00:00:00Z", "config": { "digest": "sha256:config-4.0" } }"""));
        handler.AddJsonResponse("https://ghcr.io/v2/acme/api/blobs/sha256%3Aconfig-4.0", """{ "created": "2020-01-01T00:00:00Z" }""");

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.GetTagsAsync("ghcr.io", "acme/api", cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The listing must succeed");
        Assert.AreSequenceEqual([
                                    "https://ghcr.io/v2/acme/api/blobs/sha256%3Aconfig-1.0",
                                    "https://ghcr.io/v2/acme/api/blobs/sha256%3Aconfig-2.0",
                                    "https://ghcr.io/v2/acme/api/blobs/sha256%3Aconfig-3.0"
                                ],
                                RequestedUris(handler).Where(uri => uri.Contains("/blobs/", StringComparison.Ordinal))
                                                      .ToArray(),
                                "Each inspected tag without a created time must request its blob once, and the tag with one none");
    }

    #endregion // Methods

    #region Static methods

    /// <summary>
    /// Add the manifest of a tag without a created time and the configuration blob that carries one
    /// </summary>
    /// <param name="handler">Handler that answers the requests</param>
    /// <param name="tag">Tag</param>
    /// <param name="digest">Digest of the tag</param>
    /// <param name="created">Created time of the configuration blob</param>
    private static void AddTagWithBlob(SequenceHttpMessageHandler handler, string tag, string digest, string created)
    {
        handler.AddResponse($"https://ghcr.io/v2/acme/api/manifests/{tag}",
                            CreateManifestResponse(digest, $$"""{ "schemaVersion": 2, "config": { "digest": "sha256:config-{{tag}}" } }"""));
        handler.AddJsonResponse($"https://ghcr.io/v2/acme/api/blobs/sha256%3Aconfig-{tag}", $$"""{ "created": "{{created}}" }""");
    }

    /// <summary>
    /// Read the addresses the handler observed
    /// </summary>
    /// <param name="handler">Handler that observed the requests</param>
    /// <returns>Request addresses in order</returns>
    private static string[] RequestedUris(SequenceHttpMessageHandler handler)
    {
        return handler.Requests.Select(request => request.RequestUri)
                               .ToArray();
    }

    /// <summary>
    /// Create the reference of the tag under test
    /// </summary>
    /// <returns>Image reference</returns>
    private static ImageReference CreateReference()
    {
        return new ImageReference
               {
                   Registry = "ghcr.io",
                   Repository = "acme/api",
                   Tag = "1.0",
               };
    }

    /// <summary>
    /// Create a manifest response with a digest header
    /// </summary>
    /// <param name="digest">Manifest digest</param>
    /// <param name="jsonContent">Manifest JSON</param>
    /// <returns>Manifest response</returns>
    private static HttpResponseMessage CreateManifestResponse(string digest, string jsonContent)
    {
        return CreateDigestResponse(digest, jsonContent, "application/vnd.oci.image.manifest.v1+json");
    }

    /// <summary>
    /// Create an image index response with a digest header
    /// </summary>
    /// <param name="digest">Index digest</param>
    /// <param name="jsonContent">Index JSON</param>
    /// <returns>Index response</returns>
    private static HttpResponseMessage CreateIndexResponse(string digest, string jsonContent)
    {
        return CreateDigestResponse(digest, jsonContent, "application/vnd.oci.image.index.v1+json");
    }

    /// <summary>
    /// Create a response with a digest header
    /// </summary>
    /// <param name="digest">Digest</param>
    /// <param name="jsonContent">JSON content</param>
    /// <param name="mediaType">Media type of the content</param>
    /// <returns>Response</returns>
    private static HttpResponseMessage CreateDigestResponse(string digest, string jsonContent, string mediaType)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
                       {
                           Content = new StringContent(jsonContent,
                                                       Encoding.UTF8,
                                                       mediaType),
                       };

        response.Headers.TryAddWithoutValidation("Docker-Content-Digest", digest);

        return response;
    }

    /// <summary>
    /// Create the answer of the blob request that is not a success
    /// </summary>
    /// <param name="answer">Kind of the answer</param>
    /// <returns>Response</returns>
    private static HttpResponseMessage CreateBlobAnswer(string answer)
    {
        var response = new HttpResponseMessage(answer switch
                                               {
                                                   "NotFound" => HttpStatusCode.NotFound,
                                                   "ServerError" => HttpStatusCode.InternalServerError,
                                                   _ => HttpStatusCode.Unauthorized,
                                               });

        if (answer == "UnauthorizedTokenRequestFails")
        {
            response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer",
                                                                               "realm=\"https://ghcr.io/token\",service=\"ghcr.io\",scope=\"repository:acme/api:pull\""));
        }

        return response;
    }

    /// <summary>
    /// Answer the manifest request and let the blob request fail the given way
    /// </summary>
    /// <param name="request">Request</param>
    /// <param name="fault">Kind of the blob fault</param>
    /// <param name="requestedUris">Addresses requested so far, extended by this request</param>
    /// <returns>Response</returns>
    private static HttpResponseMessage AnswerBlobWithFault(HttpRequestMessage request, string fault, List<string> requestedUris)
    {
        requestedUris.Add(request.RequestUri!.AbsoluteUri);

        if (request.RequestUri.AbsolutePath.Contains("/blobs/", StringComparison.Ordinal) == false)
        {
            return CreateManifestResponse(TagDigest, ManifestWithoutCreated);
        }

        return fault switch
               {
                   "Transport" => throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (registry.internal.test:443)"),
                   "Timeout" => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 30 seconds elapsing.", new TimeoutException()),
                   _ => new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{ \"created\": ", Encoding.UTF8, "application/json"),
                        },
               };
    }

    /// <summary>
    /// Cancel the caller's token and raise the cancellation the way a cancelled request does
    /// </summary>
    /// <param name="callerCancellation">Cancellation source of the caller</param>
    /// <returns>Never returns</returns>
    private static HttpResponseMessage CancelCaller(CancellationTokenSource callerCancellation)
    {
        callerCancellation.Cancel();

        throw new OperationCanceledException(callerCancellation.Token);
    }

    #endregion // Static methods
}