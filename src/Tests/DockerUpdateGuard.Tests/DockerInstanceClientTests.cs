using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Docker;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

using Microsoft.Extensions.Logging;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DockerInstanceClient"/>
/// </summary>
[TestClass]
public partial class DockerInstanceClientTests
{
    #region Constants

    /// <summary>
    /// Local image reference used by the pooled-client tests
    /// </summary>
    private const string PooledImageReference = "sha256:local-image";

    /// <summary>
    /// Image inspect endpoint URI used by the pooled-client tests
    /// </summary>
    private const string PooledImageInspectUri = "https://docker.example.test/v1.41/images/sha256%3Alocal-image/json";

    /// <summary>
    /// Image inspect payload used by the pooled-client tests
    /// </summary>
    private const string PooledImageInspectJson = """
                                                  { "Id": "sha256:local-image", "RepoDigests": [] }
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
    /// Verify disabled Docker instances return a not-configured result and log the skip decision
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientDiscoverContainersAsyncWhenInstanceDisabledReturnsNotConfiguredAndLogsSkipAsync()
    {
        var logger = new TestLogger<DockerInstanceClient>();
        var client = new DockerInstanceClient(logger);
        var instanceOptions = new DockerInstanceOptions
                              {
                                  Name = "Production",
                                  BaseUrl = "https://docker.example.test",
                                  Enabled = false,
                              };

        var result = await client.DiscoverContainersAsync(instanceOptions, CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.NotConfigured,
                        result.Status,
                        "Disabled Docker instances must return a not-configured result");
        Assert.Contains(entry => entry.EventId.Id == 3100
                                 && entry.LogLevel == LogLevel.Information
                                 && entry.Message.Contains("Production", StringComparison.Ordinal),
                        logger.Entries,
                        "Disabled Docker instances must log the skip decision with the configured instance name");
    }

    /// <summary>
    /// Verify unsupported Docker endpoints return an unsupported result and log the warning
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientDiscoverContainersAsyncWithUnsupportedEndpointReturnsUnsupportedAndLogsWarningAsync()
    {
        var logger = new TestLogger<DockerInstanceClient>();
        var client = new DockerInstanceClient(logger);
        var instanceOptions = new DockerInstanceOptions
                              {
                                  Name = "Production",
                                  BaseUrl = "ssh://docker.example.test",
                                  Enabled = true,
                              };

        var result = await client.DiscoverContainersAsync(instanceOptions, CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Unsupported,
                        result.Status,
                        "Unsupported Docker endpoints must return an unsupported result");
        Assert.Contains(entry => entry.EventId.Id == 3101
                                 && entry.LogLevel == LogLevel.Warning
                                 && entry.Message.Contains("ssh://docker.example.test", StringComparison.Ordinal),
                        logger.Entries,
                        "Unsupported Docker endpoints must log the rejected endpoint value as a warning");
    }

    /// <summary>
    /// Verify Docker discovery timeouts return a failed result and the dedicated timeout log
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientDiscoverContainersAsyncWhenRequestTimesOutReturnsFailedAndLogsTimeoutAsync()
    {
        var logger = new TestLogger<DockerInstanceClient>();
        using var httpClient = new HttpClient(new TimeoutHttpMessageHandler())
                               {
                                   BaseAddress = new Uri("https://docker.example.test/"),
                                   Timeout = Timeout.InfiniteTimeSpan,
                               };
        var client = new DockerInstanceClient(logger,
                                              (_, _) => httpClient);
        var instanceOptions = new DockerInstanceOptions
                              {
                                  Name = "Docker Desktop",
                                  BaseUrl = "https://docker.example.test",
                                  Enabled = true,
                                  RequestTimeoutSeconds = 1,
                              };

        var result = await client.DiscoverContainersAsync(instanceOptions, CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed,
                        result.Status,
                        "Timed-out Docker discovery must return a failed result");
        Assert.AreEqual("Docker container discovery for 'Docker Desktop' timed out after 1 seconds",
                        result.Message,
                        "Timed-out Docker discovery must surface a clear timeout message");
        Assert.Contains(entry => entry.EventId.Id == 3105
                                 && entry.LogLevel == LogLevel.Warning
                                 && entry.Message.Contains("Docker Desktop", StringComparison.Ordinal),
                        logger.Entries,
                        "Timed-out Docker discovery must log the dedicated timeout warning");
        Assert.DoesNotContain(entry => entry.EventId.Id == 3104,
                              logger.Entries,
                              "Timed-out Docker discovery must avoid the generic exception-based discovery log");
    }

    /// <summary>
    /// Verify runtime container discovery resolves the repository digest instead of keeping the Docker-internal image identifier
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientDiscoverContainersAsyncResolvesRepositoryDigestFromImageInspectAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler)
                         {
                             BaseAddress = new Uri("https://docker.example.test/"),
                         };

        try
        {
            handler.AddJsonResponse("https://docker.example.test/v1.41/containers/json?all=1",
                                    """
                                    [
                                      {
                                        "Id": "container-1",
                                        "Names": ["/dockerupdateguard"],
                                        "Image": "docker.io/networlddev/dockerupdateguard:latest",
                                        "ImageID": "sha256:cbfa80145a774dd9ce48406330404ce7bb2577cc90a8f2e392f92ad6005aa7c0",
                                        "State": "running",
                                        "Labels": {}
                                      }
                                    ]
                                    """);
            handler.AddJsonResponse("https://docker.example.test/v1.41/images/sha256%3Acbfa80145a774dd9ce48406330404ce7bb2577cc90a8f2e392f92ad6005aa7c0/json",
                                    """
                                    {
                                      "RepoDigests": [
                                        "networlddev/dockerupdateguard@sha256:776c51f79a433935b07226bc9d86761ab43ab265045a83f7803aeefe7c36b561",
                                        "networlddev/other@sha256:1111111111111111111111111111111111111111111111111111111111111111"
                                      ]
                                    }
                                    """);

            var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(),
                                                  (_, _) => httpClient);
            var instanceOptions = new DockerInstanceOptions
                                  {
                                      Name = "Production",
                                      BaseUrl = "https://docker.example.test",
                                      Enabled = true,
                                  };

            var result = await client.DiscoverContainersAsync(instanceOptions, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "Container discovery must succeed when the Docker engine returns container and image inspect payloads");
            Assert.IsNotNull(result.Data, "Container discovery must return discovered containers");
            Assert.AreEqual("sha256:776c51f79a433935b07226bc9d86761ab43ab265045a83f7803aeefe7c36b561",
                            result.Data.Single().ImageDigest,
                            "Container discovery must expose the matching repository digest instead of the Docker-internal image identifier");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify named-pipe Docker endpoints are accepted for local Windows Docker Desktop instances
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientDiscoverContainersAsyncWithNamedPipeEndpointUsesEngineRequestsAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler)
                         {
                             BaseAddress = new Uri("http://localhost/"),
                         };

        try
        {
            handler.AddJsonResponse("http://localhost/v1.41/containers/json?all=1",
                                    """
                                    [
                                      {
                                        "Id": "container-1",
                                        "Names": ["/dockerupdateguard"],
                                        "Image": "docker.io/networlddev/dockerupdateguard:latest",
                                        "State": "running",
                                        "Labels": {}
                                      }
                                    ]
                                    """);

            var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(),
                                                  (_, engineUri) =>
                                                  {
                                                      Assert.AreEqual("http://localhost/",
                                                                      engineUri.AbsoluteUri,
                                                                      "Named-pipe Docker endpoints must use a localhost HTTP base address for API requests");

                                                      return httpClient;
                                                  });
            var instanceOptions = new DockerInstanceOptions
                                  {
                                      Name = "Production",
                                      BaseUrl = "npipe://./pipe/docker_engine",
                                      Enabled = true,
                                  };

            var result = await client.DiscoverContainersAsync(instanceOptions, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "Named-pipe Docker endpoints must be supported for container discovery");
            Assert.IsNotNull(result.Data, "Named-pipe Docker endpoints must return discovered containers");
            Assert.HasCount(1,
                            result.Data,
                            "Named-pipe Docker endpoints must allow Docker engine container queries");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify runtime container discovery does not fall back to the Docker-internal image identifier when no repository digest is available
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientDiscoverContainersAsyncWithoutRepositoryDigestKeepsDigestEmptyAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler)
                         {
                             BaseAddress = new Uri("https://docker.example.test/"),
                         };

        try
        {
            handler.AddJsonResponse("https://docker.example.test/v1.41/containers/json?all=1",
                                    """
                                    [
                                      {
                                        "Id": "container-1",
                                        "Names": ["/dockerupdateguard"],
                                        "Image": "docker.io/networlddev/dockerupdateguard:latest",
                                        "ImageID": "sha256:cbfa80145a774dd9ce48406330404ce7bb2577cc90a8f2e392f92ad6005aa7c0",
                                        "State": "running",
                                        "Labels": {}
                                      }
                                    ]
                                    """);
            handler.AddJsonResponse("https://docker.example.test/v1.41/images/sha256%3Acbfa80145a774dd9ce48406330404ce7bb2577cc90a8f2e392f92ad6005aa7c0/json",
                                    """
                                    {
                                      "RepoDigests": []
                                    }
                                    """);

            var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(),
                                                  (_, _) => httpClient);
            var instanceOptions = new DockerInstanceOptions
                                  {
                                      Name = "Production",
                                      BaseUrl = "https://docker.example.test",
                                      Enabled = true,
                                  };

            var result = await client.DiscoverContainersAsync(instanceOptions, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "Container discovery must still succeed when no repository digest can be resolved from the local image");
            Assert.IsNotNull(result.Data, "Container discovery must return discovered containers");
            Assert.IsNull(result.Data.Single().ImageDigest,
                          "Container discovery must not reuse the Docker-internal image identifier as a repository digest");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify Docker image inspect parsing exposes environment variables and rootfs layers
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientInspectImageAsyncParsesEnvironmentVariablesAndRootFsLayersAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler)
                         {
                             BaseAddress = new Uri("https://docker.example.test/"),
                         };

        try
        {
            handler.AddJsonResponse("https://docker.example.test/v1.41/images/sha256%3Alocal-image/json",
                                    """
                                    {
                                      "Id": "sha256:local-image",
                                      "Created": "2025-06-01T12:00:00Z",
                                      "Os": "linux",
                                      "Architecture": "amd64",
                                      "RepoTags": [
                                        "docker.io/company/api:1.0.0"
                                      ],
                                      "RepoDigests": [
                                        "docker.io/company/api@sha256:current"
                                      ],
                                      "Config": {
                                        "Env": [
                                          "DOTNET_VERSION=9.0.13",
                                          "ASPNET_VERSION=9.0.13"
                                        ]
                                      },
                                      "RootFS": {
                                        "Layers": [
                                          "sha256:layer-1",
                                          "sha256:layer-2"
                                        ]
                                      }
                                    }
                                    """);

            var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(),
                                                  (_, _) => httpClient);
            var instanceOptions = new DockerInstanceOptions
                                  {
                                      Name = "Production",
                                      BaseUrl = "https://docker.example.test",
                                      Enabled = true,
                                  };

            var result = await client.InspectImageAsync(instanceOptions, "sha256:local-image", CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "Image inspect must succeed when the Docker engine returns an inspect payload");
            Assert.IsNotNull(result.Data, "Image inspect must expose the parsed inspect payload");
            Assert.AreSequenceEqual(["DOTNET_VERSION=9.0.13", "ASPNET_VERSION=9.0.13"], result.Data.EnvironmentVariables.ToArray(), "Image inspect must expose environment variables from the Docker image config");
            Assert.AreSequenceEqual(["sha256:layer-1", "sha256:layer-2"], result.Data.RootFsLayers.ToArray(), "Image inspect must expose the rootfs layer chain");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify Docker image history parsing exposes created-by text and tags
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientGetImageHistoryAsyncParsesHistoryEntriesAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler)
                         {
                             BaseAddress = new Uri("https://docker.example.test/"),
                         };

        try
        {
            handler.AddJsonResponse("https://docker.example.test/v1.41/images/sha256%3Alocal-image/history",
                                    """
                                    [
                                      {
                                        "Created": "2025-06-02T12:00:00Z",
                                        "CreatedBy": "/bin/sh -c #(nop)  ENV DOTNET_VERSION=9.0.13",
                                        "Comment": "final stage",
                                        "Tags": [
                                          "docker.io/company/api:1.0.0"
                                        ]
                                      }
                                    ]
                                    """);

            var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(),
                                                  (_, _) => httpClient);
            var instanceOptions = new DockerInstanceOptions
                                  {
                                      Name = "Production",
                                      BaseUrl = "https://docker.example.test",
                                      Enabled = true,
                                  };

            var result = await client.GetImageHistoryAsync(instanceOptions, "sha256:local-image", CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "Image history must succeed when the Docker engine returns a history payload");
            Assert.IsNotNull(result.Data, "Image history must expose parsed history entries");
            Assert.AreEqual("/bin/sh -c #(nop)  ENV DOTNET_VERSION=9.0.13",
                            result.Data.Single().CreatedBy,
                            "Image history must expose the created-by command text");
            Assert.AreSequenceEqual(["docker.io/company/api:1.0.0"], result.Data.Single().Tags.ToArray(), "Image history must expose tags when the Docker engine returns them");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify repeated engine calls reuse a single pooled HTTP client instead of recreating one per request
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientReusesPooledHttpClientAcrossEngineCallsAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler)
                         {
                             BaseAddress = new Uri("https://docker.example.test/"),
                         };
        var factoryInvocationCount = 0;

        try
        {
            handler.AddJsonResponse(PooledImageInspectUri, PooledImageInspectJson);
            handler.AddJsonResponse(PooledImageInspectUri, PooledImageInspectJson);

            var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(),
                                                  (_, _) =>
                                                  {
                                                      Interlocked.Increment(ref factoryInvocationCount);

                                                      return httpClient;
                                                  });
            var instanceOptions = new DockerInstanceOptions
                                  {
                                      Name = "Production",
                                      BaseUrl = "https://docker.example.test",
                                      Enabled = true,
                                  };

            var firstResult = await client.InspectImageAsync(instanceOptions, PooledImageReference, TestContext.CancellationToken)
                                          .ConfigureAwait(false);
            var secondResult = await client.InspectImageAsync(instanceOptions, PooledImageReference, TestContext.CancellationToken)
                                           .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            firstResult.Status,
                            "The first engine call must succeed against the pooled HTTP client");
            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            secondResult.Status,
                            "The second engine call must succeed against the pooled HTTP client");
            Assert.AreEqual(1,
                            factoryInvocationCount,
                            "Repeated engine calls for the same instance must reuse a single pooled HTTP client instead of creating one per request");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify disposing the adapter disposes the pooled HTTP clients it created
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientDisposeDisposesPooledHttpClientsAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler)
                         {
                             BaseAddress = new Uri("https://docker.example.test/"),
                         };

        try
        {
            handler.AddJsonResponse(PooledImageInspectUri, PooledImageInspectJson);

            var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(),
                                                  (_, _) => httpClient);
            var instanceOptions = new DockerInstanceOptions
                                  {
                                      Name = "Production",
                                      BaseUrl = "https://docker.example.test",
                                      Enabled = true,
                                  };

            var result = await client.InspectImageAsync(instanceOptions, PooledImageReference, TestContext.CancellationToken)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "The pooled HTTP client must serve the engine call before the adapter is disposed");

            client.Dispose();

            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => httpClient.GetAsync("v1.41/info", TestContext.CancellationToken),
                                                                     "Disposing the adapter must dispose the pooled HTTP clients it created")
                        .ConfigureAwait(false);
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify the Docker HTTP-client factory loads a configured client certificate for TLS endpoints
    /// </summary>
    [TestMethod]
    public void DockerInstanceClientCreateHttpClientWithClientCertificateReturnsConfiguredClient()
    {
        var createHttpClientMethod = typeof(DockerInstanceClient).GetMethod("CreateHttpClient",
                                                                            BindingFlags.Static | BindingFlags.NonPublic);
        var certificatePath = Path.Combine(Path.GetTempPath(), $"dug-client-cert-{Guid.NewGuid():N}.pfx");

        Assert.IsNotNull(createHttpClientMethod, "The Docker HTTP-client factory must remain discoverable for transport tests");

        try
        {
            using (var rsa = RSA.Create(2048))
            {
                var certificateRequest = new CertificateRequest("CN=DockerUpdateGuardClientCertificateTest",
                                                                rsa,
                                                                HashAlgorithmName.SHA256,
                                                                RSASignaturePadding.Pkcs1);
                using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
                                                                            DateTimeOffset.UtcNow.AddDays(1));

                File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx));
            }

            var instanceOptions = new DockerInstanceOptions
                                  {
                                      Name = "Production",
                                      BaseUrl = "https://docker.example.test",
                                      Enabled = true,
                                      CertificatePath = certificatePath,
                                  };
            var engineUri = new Uri("https://docker.example.test/");
            var logger = new TestLogger<DockerInstanceClient>();

            using var httpClient = (HttpClient?)createHttpClientMethod.Invoke(null, [instanceOptions, engineUri, logger]);

            Assert.IsNotNull(httpClient, "The Docker HTTP-client factory must return an HTTP client when a client certificate is configured");
            Assert.AreEqual("https://docker.example.test/",
                            httpClient.BaseAddress?.AbsoluteUri,
                            "TLS Docker endpoints with a client certificate must use the resolved engine URI as base address");

            var clientCertificates = GetClientCertificates(httpClient);

            Assert.IsNotNull(clientCertificates, "A valid client certificate on an https endpoint must be attached to the handler");
            Assert.HasCount(1, clientCertificates, "Exactly one client certificate must be attached");
            Assert.IsTrue(((X509Certificate2)clientCertificates[0]).HasPrivateKey,
                          "The attached client certificate must carry its private key");
        }
        finally
        {
            File.Delete(certificatePath);
        }
    }

    /// <summary>
    /// Verify a plain http endpoint does not load the configured client certificate
    /// </summary>
    [TestMethod]
    public void DockerInstanceClientCreateHttpClientWithPlainHttpEndpointIgnoresCertificatePath()
    {
        var createHttpClientMethod = typeof(DockerInstanceClient).GetMethod("CreateHttpClient",
                                                                            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.IsNotNull(createHttpClientMethod, "The Docker HTTP-client factory must remain discoverable for transport tests");

        var instanceOptions = new DockerInstanceOptions
                              {
                                  Name = "Production",
                                  BaseUrl = "http://docker.example.test",
                                  Enabled = true,
                                  CertificatePath = Path.Combine(Path.GetTempPath(), $"dug-missing-{Guid.NewGuid():N}", "client.pfx"),
                              };
        var engineUri = new Uri("http://docker.example.test/");

        using var httpClient = (HttpClient?)createHttpClientMethod.Invoke(null, [instanceOptions, engineUri, new TestLogger<DockerInstanceClient>()]);

        Assert.IsNotNull(httpClient, "A plain http endpoint must create a client even when the certificate path does not exist");

        var clientCertificates = GetClientCertificates(httpClient);

        Assert.IsTrue(clientCertificates is null || clientCertificates.Count == 0,
                      "A plain http endpoint must not attach a client certificate");
    }

    /// <summary>
    /// Verify an unusable client certificate on an https endpoint ends the operation as failed with the instance name
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientInspectImageAsyncWithMissingClientCertificateReturnsFailedAsync()
    {
        using var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>());
        var instanceOptions = new DockerInstanceOptions
                              {
                                  Name = "Production",
                                  BaseUrl = "https://docker.example.test",
                                  Enabled = true,
                                  CertificatePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "client.pfx"),
                              };

        var result = await client.InspectImageAsync(instanceOptions, PooledImageReference, TestContext.CancellationToken)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed,
                        result.Status,
                        "A missing client certificate must end the operation as failed");
        Assert.AreEqual("Docker image inspect failed for 'Production': The client certificate of Docker instance 'Production' could not be loaded: the file does not exist",
                        result.Message,
                        "The failure must carry the operation frame, the instance name and the reason");
    }

    /// <summary>
    /// Verify a failed HTTP-client creation is not cached in the pool
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientInspectImageAsyncAfterClientCreationFailureCreatesClientAgainAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler)
                         {
                             BaseAddress = new Uri("https://docker.example.test/"),
                         };
        var factoryInvocationCount = 0;

        try
        {
            handler.AddJsonResponse(PooledImageInspectUri, PooledImageInspectJson);

            var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(),
                                                  (_, _) =>
                                                  {
                                                      if (Interlocked.Increment(ref factoryInvocationCount) == 1)
                                                      {
                                                          throw new InvalidOperationException("Client creation failed");
                                                      }

                                                      return httpClient;
                                                  });
            var instanceOptions = new DockerInstanceOptions
                                  {
                                      Name = "Production",
                                      BaseUrl = "https://docker.example.test",
                                      Enabled = true,
                                  };

            var firstResult = await client.InspectImageAsync(instanceOptions, PooledImageReference, TestContext.CancellationToken)
                                          .ConfigureAwait(false);
            var secondResult = await client.InspectImageAsync(instanceOptions, PooledImageReference, TestContext.CancellationToken)
                                           .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Failed,
                            firstResult.Status,
                            "The operation whose client creation threw must end as failed");
            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            secondResult.Status,
                            "The next operation must create the client again instead of replaying the cached failure");
            Assert.AreEqual(2,
                            factoryInvocationCount,
                            "A failed client creation must not stay in the pool");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify named-pipe Docker endpoints create a localhost-based HTTP client
    /// </summary>
    [TestMethod]
    public void DockerInstanceClientCreateHttpClientWithNamedPipeEndpointReturnsLocalhostBaseAddress()
    {
        var createHttpClientMethod = typeof(DockerInstanceClient).GetMethod("CreateHttpClient",
                                                                            BindingFlags.Static | BindingFlags.NonPublic);
        var instanceOptions = new DockerInstanceOptions
                              {
                                  Name = "Production",
                                  BaseUrl = "npipe://./pipe/docker_engine",
                                  Enabled = true,
                                  RequestTimeoutSeconds = 42,
                              };
        var engineUri = new Uri("http://localhost/");
        var logger = new TestLogger<DockerInstanceClient>();

        Assert.IsNotNull(createHttpClientMethod, "The Docker HTTP-client factory must remain discoverable for transport tests");

        using var httpClient = (HttpClient?)createHttpClientMethod.Invoke(null, [instanceOptions, engineUri, logger]);

        Assert.IsNotNull(httpClient, "The Docker HTTP-client factory must return an HTTP client for named-pipe endpoints");
        Assert.AreEqual("http://localhost/",
                        httpClient.BaseAddress?.AbsoluteUri,
                        "Named-pipe Docker endpoints must use a localhost base address for relative Docker API calls");
        Assert.AreEqual(Timeout.InfiniteTimeSpan,
                        httpClient.Timeout,
                        "Named-pipe Docker endpoints must keep HttpClient.Timeout disabled because request timeouts are enforced per request");
    }

    /// <summary>
    /// Verify the Docker HTTP-client factory logs a warning only once per instance when server certificate validation is disabled
    /// </summary>
    [TestMethod]
    public void DockerInstanceClientCreateHttpClientWhenSkipCertificateValidationLogsWarningOncePerInstance()
    {
        var createHttpClientMethod = typeof(DockerInstanceClient).GetMethod("CreateHttpClient",
                                                                            BindingFlags.Static | BindingFlags.NonPublic);
        var instanceName = $"tls-bypass-{Guid.NewGuid():N}";
        var instanceOptions = new DockerInstanceOptions
                              {
                                  Name = instanceName,
                                  BaseUrl = "https://docker.example.test",
                                  Enabled = true,
                                  SkipCertificateValidation = true,
                              };
        var engineUri = new Uri("https://docker.example.test/");
        var logger = new TestLogger<DockerInstanceClient>();

        Assert.IsNotNull(createHttpClientMethod, "The Docker HTTP-client factory must remain discoverable for transport tests");

        using var firstHttpClient = (HttpClient?)createHttpClientMethod.Invoke(null, [instanceOptions, engineUri, logger]);
        using var secondHttpClient = (HttpClient?)createHttpClientMethod.Invoke(null, [instanceOptions, engineUri, logger]);

        Assert.IsNotNull(firstHttpClient, "The Docker HTTP-client factory must return an HTTP client for TLS endpoints");
        Assert.IsNotNull(secondHttpClient, "The Docker HTTP-client factory must return an HTTP client on repeated calls for TLS endpoints");
        Assert.ContainsSingle(entry => entry.EventId.Id == 3106
                                       && entry.LogLevel == LogLevel.Warning
                                       && entry.Message.Contains(instanceName, StringComparison.Ordinal),
                              logger.Entries,
                              "Disabling server certificate validation must emit the auditable warning exactly once per instance to avoid flooding the log under active polling");
    }

    /// <summary>
    /// Verify the Docker HTTP-client factory does not warn when server certificate validation stays enabled
    /// </summary>
    [TestMethod]
    public void DockerInstanceClientCreateHttpClientWhenCertificateValidationEnabledDoesNotWarn()
    {
        var createHttpClientMethod = typeof(DockerInstanceClient).GetMethod("CreateHttpClient",
                                                                            BindingFlags.Static | BindingFlags.NonPublic);
        var instanceOptions = new DockerInstanceOptions
                              {
                                  Name = "Production",
                                  BaseUrl = "https://docker.example.test",
                                  Enabled = true,
                                  SkipCertificateValidation = false,
                              };
        var engineUri = new Uri("https://docker.example.test/");
        var logger = new TestLogger<DockerInstanceClient>();

        Assert.IsNotNull(createHttpClientMethod, "The Docker HTTP-client factory must remain discoverable for transport tests");

        using var httpClient = (HttpClient?)createHttpClientMethod.Invoke(null, [instanceOptions, engineUri, logger]);

        Assert.IsNotNull(httpClient, "The Docker HTTP-client factory must return an HTTP client for TLS endpoints");
        Assert.DoesNotContain(entry => entry.EventId.Id == 3106,
                              logger.Entries,
                              "The Docker HTTP-client factory must not warn about disabled certificate validation when it stays enabled");
    }

    /// <summary>
    /// Verify an unsupported endpoint with user info is reported and logged with the user info masked
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientDiscoverContainersAsyncWithUnsupportedEndpointUserInfoMasksUserInfoAsync()
    {
        var logger = new TestLogger<DockerInstanceClient>();
        var client = new DockerInstanceClient(logger);
        var instanceOptions = new DockerInstanceOptions
                              {
                                  Name = "Production",
                                  BaseUrl = "ssh://user:secret@docker.example.test",
                                  Enabled = true,
                              };

        var result = await client.DiscoverContainersAsync(instanceOptions, CancellationToken.None)
                                 .ConfigureAwait(false);

        var warning = logger.Entries.Single(entry => entry.EventId.Id == 3101);

        Assert.AreEqual(ExternalOperationStatus.Unsupported,
                        result.Status,
                        "An unsupported endpoint must return an unsupported result");
        Assert.IsNotNull(result.Message, "An unsupported endpoint must produce a message");
        Assert.Contains("ssh://***@docker.example.test/",
                        result.Message,
                        "The result message must name the endpoint with its user info masked");
        Assert.DoesNotContain("secret", result.Message, "The result message must not contain the password");
        Assert.Contains("ssh://***@docker.example.test/",
                        warning.Message,
                        "The rejected endpoint log must name the endpoint with its user info masked");
        Assert.DoesNotContain("secret", warning.Message, "The rejected endpoint log must not contain the password");
    }

    /// <summary>
    /// Verify the other operations report an unsupported endpoint with user info masked
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientOperationsWithUnsupportedEndpointUserInfoMaskUserInfoAsync()
    {
        var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>());
        var instanceOptions = new DockerInstanceOptions
                              {
                                  Name = "Production",
                                  BaseUrl = "ssh://user:secret@docker.example.test",
                                  Enabled = true,
                              };

        var usage = await client.CollectContainerResourceUsageAsync(instanceOptions, CancellationToken.None).ConfigureAwait(false);
        var inspect = await client.InspectImageAsync(instanceOptions, "sha256:image", CancellationToken.None).ConfigureAwait(false);
        var history = await client.GetImageHistoryAsync(instanceOptions, "sha256:image", CancellationToken.None).ConfigureAwait(false);
        var memory = await client.GetHostMemoryTotalAsync(instanceOptions, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Unsupported, usage.Status, "Resource sampling must report an unsupported endpoint");
        Assert.AreEqual(ExternalOperationStatus.Unsupported, inspect.Status, "Image inspection must report an unsupported endpoint");
        Assert.AreEqual(ExternalOperationStatus.Unsupported, history.Status, "Image history must report an unsupported endpoint");
        Assert.AreEqual(ExternalOperationStatus.Unsupported, memory.Status, "Host memory lookup must report an unsupported endpoint");
        Assert.Contains("ssh://***@docker.example.test/", usage.Message ?? string.Empty, "Resource sampling must mask the user info");
        Assert.Contains("ssh://***@docker.example.test/", inspect.Message ?? string.Empty, "Image inspection must mask the user info");
        Assert.Contains("ssh://***@docker.example.test/", history.Message ?? string.Empty, "Image history must mask the user info");
        Assert.Contains("ssh://***@docker.example.test/", memory.Message ?? string.Empty, "Host memory lookup must mask the user info");
        Assert.DoesNotContain("secret", usage.Message ?? string.Empty, "Resource sampling must not contain the password");
        Assert.DoesNotContain("secret", inspect.Message ?? string.Empty, "Image inspection must not contain the password");
        Assert.DoesNotContain("secret", history.Message ?? string.Empty, "Image history must not contain the password");
        Assert.DoesNotContain("secret", memory.Message ?? string.Empty, "Host memory lookup must not contain the password");
    }

    /// <summary>
    /// Verify a named-pipe endpoint with user info and no pipe name fails with the user info masked
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientDiscoverContainersAsyncWithNamedPipeWithoutPipeNameMasksUserInfoAsync()
    {
        var logger = new TestLogger<DockerInstanceClient>();
        var client = new DockerInstanceClient(logger);
        var instanceOptions = new DockerInstanceOptions
                              {
                                  Name = "Production",
                                  BaseUrl = "npipe://user:secret@pipehost/",
                                  Enabled = true,
                              };

        var result = await client.DiscoverContainersAsync(instanceOptions, CancellationToken.None)
                                 .ConfigureAwait(false);

        var failure = logger.Entries.Single(entry => entry.EventId.Id == 3104);

        Assert.AreEqual(ExternalOperationStatus.Failed,
                        result.Status,
                        "A named-pipe endpoint without a pipe name must fail discovery");
        Assert.IsNotNull(result.Message, "A failed discovery must produce a message");
        Assert.Contains("npipe://***@pipehost/",
                        result.Message,
                        "The failure message must name the pipe URI with its user info masked");
        Assert.DoesNotContain("secret", result.Message, "The failure message must not contain the password");
        Assert.IsNotNull(failure.Exception, "The discovery failure must be logged with its exception");
        Assert.DoesNotContain("secret", failure.Exception.Message, "The logged exception must not contain the password");
    }

    /// <summary>
    /// Verify a transport failure in a Docker Engine operation reports the fixed connection-failed reason and logs the exception
    /// </summary>
    /// <param name="operation">Name of the operation</param>
    /// <param name="expectedMessage">Expected failure message</param>
    /// <param name="eventId">Expected event id of the logged failure</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("Discovery", "Docker container discovery failed for 'Production': connection failed", 3104)]
    [DataRow("Sampling", "Docker container resource sampling failed for 'Production': connection failed", 3107)]
    [DataRow("Inspect", "Docker image inspect failed for 'Production': connection failed", 3108)]
    [DataRow("History", "Docker image history failed for 'Production': connection failed", 3109)]
    [DataRow("HostInfo", "Docker host info request failed for 'Production': connection failed", 3110)]
    public async Task DockerInstanceClientOperationWhenTransportFailsReportsConnectionFailedAsync(string operation, string expectedMessage, int eventId)
    {
        var injected = new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (docker.internal.test:2376)");
        var logger = new TestLogger<DockerInstanceClient>();

        using var handler = new RetryProbeHttpMessageHandler([null], HttpStatusCode.OK)
                            {
                                FailureFactory = () => injected,
                            };
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://docker.example.test/"),
                               };

        var client = new DockerInstanceClient(logger, (_, _) => httpClient);

        var message = await RunOperationAsync(client, operation, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(expectedMessage, message, "A transport failure must report the operation frame and the fixed connection-failed reason");
        Assert.DoesNotContain("internal.test", message, "The exception text must not reach the result message");
        Assert.Contains(entry => entry.EventId.Id == eventId && ReferenceEquals(entry.Exception, injected),
                        logger.Entries,
                        "The transport failure must be logged with the exception");
    }

    /// <summary>
    /// Verify an unexpected exception while creating the HTTP client reports the fixed unexpected-error reason
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientInspectImageAsyncWhenClientCreationThrowsReportsUnexpectedErrorAsync()
    {
        var injected = new InvalidOperationException("Client creation failed for docker.internal.test");
        var logger = new TestLogger<DockerInstanceClient>();
        var client = new DockerInstanceClient(logger, (_, _) => throw injected);
        var instanceOptions = new DockerInstanceOptions
                              {
                                  Name = "Production",
                                  BaseUrl = "https://docker.example.test",
                                  Enabled = true,
                              };

        var result = await client.InspectImageAsync(instanceOptions, PooledImageReference, TestContext.CancellationToken)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A failed client creation must end the operation as failed");
        Assert.AreEqual("Docker image inspect failed for 'Production': unexpected error", result.Message, "An unknown exception must report the fixed unexpected-error reason");
        Assert.Contains(entry => entry.EventId.Id == 3108 && ReferenceEquals(entry.Exception, injected),
                        logger.Entries,
                        "The failure must be logged with the exception");
    }

    /// <summary>
    /// Verify container discovery bounds the error body of a failed response to an excerpt
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientDiscoverContainersAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync()
    {
        using var handler = new DelegateHttpMessageHandler(_ => CreateLongErrorBodyResponse());
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://docker.example.test/"),
                               };

        var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(), (_, _) => httpClient);

        var message = await RunOperationAsync(client, "Discovery", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual($"Docker instance 'Production' returned 400: {OversizedRemoteText.CreateExpectedExcerpt()}",
                        message,
                        "The failure message must keep its text and carry the first 500 characters of the body followed by the ellipsis");
        Assert.DoesNotContain(new string('x', 501), message, "The failure message must not carry more than 500 characters of the body");
    }

    /// <summary>
    /// Verify container discovery replaces a line break of the error body by a space
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientDiscoverContainersAsyncWhenErrorBodyHasLineBreakReplacesItAsync()
    {
        var lineFeed = char.ConvertFromUtf32(0x000A);
        var body = $"{{\"message\":\"bad{lineFeed}request\"}}";

        Assert.Contains(lineFeed, body, "The body must contain the line feed");

        using var handler = new DelegateHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
                                                                {
                                                                    Content = new StringContent(body),
                                                                });
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://docker.example.test/"),
                               };

        var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(), (_, _) => httpClient);

        var message = await RunOperationAsync(client, "Discovery", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("Docker instance 'Production' returned 400: {\"message\":\"bad request\"}", message, "The line break of the body must become one space");
    }

    /// <summary>
    /// Verify resource sampling bounds the error body of a failed response to an excerpt
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientCollectContainerResourceUsageAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync()
    {
        using var handler = new DelegateHttpMessageHandler(_ => CreateLongErrorBodyResponse());
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://docker.example.test/"),
                               };

        var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(), (_, _) => httpClient);

        var message = await RunOperationAsync(client, "Sampling", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual($"Docker instance 'Production' returned 400: {OversizedRemoteText.CreateExpectedExcerpt()}",
                        message,
                        "The failure message must keep its text and carry the first 500 characters of the body followed by the ellipsis");
        Assert.DoesNotContain(new string('x', 501), message, "The failure message must not carry more than 500 characters of the body");
    }

    /// <summary>
    /// Verify image inspect bounds the error body of a failed response to an excerpt
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientInspectImageAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync()
    {
        using var handler = new DelegateHttpMessageHandler(_ => CreateLongErrorBodyResponse());
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://docker.example.test/"),
                               };

        var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(), (_, _) => httpClient);

        var message = await RunOperationAsync(client, "Inspect", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual($"Docker image inspect for 'sha256:local-image' returned 400: {OversizedRemoteText.CreateExpectedExcerpt()}",
                        message,
                        "The failure message must keep its text and carry the first 500 characters of the body followed by the ellipsis");
        Assert.DoesNotContain(new string('x', 501), message, "The failure message must not carry more than 500 characters of the body");
    }

    /// <summary>
    /// Verify image history bounds the error body of a failed response to an excerpt
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientGetImageHistoryAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync()
    {
        using var handler = new DelegateHttpMessageHandler(_ => CreateLongErrorBodyResponse());
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://docker.example.test/"),
                               };

        var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(), (_, _) => httpClient);

        var message = await RunOperationAsync(client, "History", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual($"Docker image history for 'sha256:local-image' returned 400: {OversizedRemoteText.CreateExpectedExcerpt()}",
                        message,
                        "The failure message must keep its text and carry the first 500 characters of the body followed by the ellipsis");
        Assert.DoesNotContain(new string('x', 501), message, "The failure message must not carry more than 500 characters of the body");
    }

    /// <summary>
    /// Verify host info bounds the error body of a failed response to an excerpt
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientGetHostMemoryTotalAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync()
    {
        using var handler = new DelegateHttpMessageHandler(_ => CreateLongErrorBodyResponse());
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://docker.example.test/"),
                               };

        var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(), (_, _) => httpClient);

        var message = await RunOperationAsync(client, "HostInfo", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual($"Docker instance 'Production' returned 400: {OversizedRemoteText.CreateExpectedExcerpt()}",
                        message,
                        "The failure message must keep its text and carry the first 500 characters of the body followed by the ellipsis");
        Assert.DoesNotContain(new string('x', 501), message, "The failure message must not carry more than 500 characters of the body");
    }

    /// <summary>
    /// Verify discovery replaces control characters in the identifiers, names, image fields and labels of a container
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientDiscoverContainersAsyncWhenFieldsHaveControlCharactersReplacesThemAsync()
    {
        var containersJson = """
                             [
                               {
                                 "Id": "c\u000Aid\u202E1",
                                 "Names": ["/na\u000Ame\u202Ex"],
                                 "Image": "docker.io/net\u001Bworld/app:1\u200B0",
                                 "ImageID": "sha256:img\u001B\u200Bx",
                                 "State": "running",
                                 "Labels": {
                                   "com.docker.compose.project": "pro\u000Aj\u202Ex",
                                   "com.docker.stack.namespace": "sta\u000Ack\u202Ex",
                                   "com.docker.swarm.service.name": "ser\u000Avice\u202Ex"
                                 }
                               }
                             ]
                             """;
        var inspectJson = """
                          { "Id": "sha256:img", "RepoDigests": ["acme/app@sha256:ab\u202Ecd"] }
                          """;

        Assert.Contains("\\u202E", containersJson, "The container input must carry the escaped format character");
        Assert.Contains("\\u000A", containersJson, "The container input must carry the escaped line feed");
        Assert.Contains("\\u202E", inspectJson, "The inspect input must carry the escaped format character");

        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://docker.example.test/"),
                               };

        handler.AddJsonResponse("https://docker.example.test/v1.41/containers/json?all=1", containersJson);
        handler.AddJsonResponse("https://docker.example.test/v1.41/images/sha256%3Aimg%20%20x/json", inspectJson);

        var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(), (_, _) => httpClient);
        var instanceOptions = new DockerInstanceOptions
                              {
                                  Name = "Production",
                                  BaseUrl = "https://docker.example.test",
                                  Enabled = true,
                              };

        var result = await client.DiscoverContainersAsync(instanceOptions, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "Discovery must succeed for a well-formed answer");
        Assert.IsNotNull(result.Data, "Discovery must return the containers");

        var container = Assert.ContainsSingle(result.Data, "Discovery must return the one container");

        Assert.AreEqual("c id 1", container.ContainerId, "The container ID must carry a space for each replaced character");
        Assert.AreEqual("na me x", container.Name, "The name must be cleaned");
        Assert.AreEqual("docker.io/net world/app:1 0", container.ImageReference, "The image reference must carry a space for each replaced character");
        Assert.AreEqual("sha256:img  x", container.LocalImageId, "The local image ID must carry a space for each replaced character");
        Assert.AreEqual("pro j x", container.ComposeProject, "The compose project label must be cleaned");
        Assert.AreEqual("sta ck x", container.StackName, "The stack namespace label must be cleaned");
        Assert.AreEqual("ser vice x", container.ServiceName, "The swarm service name label must be cleaned");
        Assert.Contains(request => request.RequestUri == "https://docker.example.test/v1.41/images/sha256%3Aimg%20%20x/json",
                        handler.Requests,
                        "The image inspect request must name the cleaned local image ID");
        Assert.AreEqual("sha256:ab cd", container.ImageDigest, "The resolved digest must come from the cleaned repository digest");
    }

    /// <summary>
    /// Verify resource sampling replaces control characters in the identifier and name of a container
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientCollectContainerResourceUsageAsyncWhenFieldsHaveControlCharactersReplacesThemAsync()
    {
        var containersJson = """
                             [
                               { "Id": "c\u000Aid\u202E1", "Names": ["/na\u000Ame\u202Ex"] }
                             ]
                             """;

        Assert.Contains("\\u202E", containersJson, "The container input must carry the escaped format character");
        Assert.Contains("\\u000A", containersJson, "The container input must carry the escaped line feed");

        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://docker.example.test/"),
                               };

        handler.AddJsonResponse("https://docker.example.test/v1.41/containers/json", containersJson);
        handler.AddJsonResponse("https://docker.example.test/v1.41/containers/c%20id%201/stats?stream=false", """{ "read": "2025-06-01T12:00:00Z" }""");

        var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(), (_, _) => httpClient);
        var instanceOptions = new DockerInstanceOptions
                              {
                                  Name = "Production",
                                  BaseUrl = "https://docker.example.test",
                                  Enabled = true,
                              };

        var result = await client.CollectContainerResourceUsageAsync(instanceOptions, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "Sampling must succeed for a well-formed answer");
        Assert.IsNotNull(result.Data, "Sampling must return the samples");

        var sample = Assert.ContainsSingle(result.Data, "Sampling must return the sample of the one container whose stats were requested under the cleaned ID");

        Assert.AreEqual("c id 1", sample.ContainerId, "The sample must carry the cleaned container ID");
        Assert.AreEqual("na me x", sample.ContainerName, "The sample must carry the cleaned container name");
    }

    /// <summary>
    /// Verify image inspect replaces control characters in every text field it exposes
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientInspectImageAsyncWhenFieldsHaveControlCharactersReplacesThemAsync()
    {
        var json = """
                   {
                     "Id": "sha256:lo\u000Acal\u202E1",
                     "Os": "li\u000Anux\u202Ex",
                     "Architecture": "am\u000Ad\u202E64",
                     "RepoTags": ["docker.io/co\u000Amp\u202Eany/api:1.0.0"],
                     "RepoDigests": ["docker.io/company/api@sha256:cu\u000Arr\u202Eent"],
                     "Config": { "Env": ["DOTNET_VERSION=9.\u000A0\u202E.13", "\u001B"] },
                     "RootFS": { "Layers": ["sha256:lay\u000Aer\u202E-1", "\u200B"] }
                   }
                   """;

        Assert.Contains("\\u202E", json, "The test input must carry the escaped format character");
        Assert.Contains("\\u000A", json, "The test input must carry the escaped line feed");

        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://docker.example.test/"),
                               };

        handler.AddJsonResponse("https://docker.example.test/v1.41/images/sha256%3Alocal-image/json", json);

        var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(), (_, _) => httpClient);
        var instanceOptions = new DockerInstanceOptions
                              {
                                  Name = "Production",
                                  BaseUrl = "https://docker.example.test",
                                  Enabled = true,
                              };

        var result = await client.InspectImageAsync(instanceOptions, "sha256:local-image", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "Image inspect must succeed for a well-formed answer");
        Assert.IsNotNull(result.Data, "Image inspect must expose the parsed payload");
        Assert.AreEqual("sha256:lo cal 1", result.Data.Id, "The image ID must be cleaned");
        Assert.AreEqual("li nux x", result.Data.OperatingSystem, "The operating system must be cleaned");
        Assert.AreEqual("am d 64", result.Data.Architecture, "The architecture must be cleaned");
        Assert.AreSequenceEqual(["docker.io/co mp any/api:1.0.0"], result.Data.RepoTags.ToArray(), "The repo tags must be cleaned");
        Assert.AreSequenceEqual(["docker.io/company/api@sha256:cu rr ent"], result.Data.RepoDigests.ToArray(), "The repo digests must be cleaned");
        Assert.AreSequenceEqual(["DOTNET_VERSION=9. 0 .13"], result.Data.EnvironmentVariables.ToArray(), "The environment variables must be cleaned and one that is only replaced characters must be dropped like a blank one");
        Assert.AreSequenceEqual(["sha256:lay er -1"], result.Data.RootFsLayers.ToArray(), "The layers must be cleaned and one that is only replaced characters must be dropped like a blank one");
    }

    /// <summary>
    /// Verify image history replaces control characters in the created-by command, comment and tags
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientGetImageHistoryAsyncWhenFieldsHaveControlCharactersReplacesThemAsync()
    {
        var json = """
                   [
                     {
                       "Created": "2025-06-02T12:00:00Z",
                       "CreatedBy": "/bin/sh -c #(nop)\u000A ENV A=1\u202E2",
                       "Comment": "fi\u000Anal\u202E stage",
                       "Tags": ["docker.io/co\u000Amp\u202Eany/api:1.0.0"]
                     }
                   ]
                   """;

        Assert.Contains("\\u202E", json, "The test input must carry the escaped format character");
        Assert.Contains("\\u000A", json, "The test input must carry the escaped line feed");

        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://docker.example.test/"),
                               };

        handler.AddJsonResponse("https://docker.example.test/v1.41/images/sha256%3Alocal-image/history", json);

        var client = new DockerInstanceClient(new TestLogger<DockerInstanceClient>(), (_, _) => httpClient);
        var instanceOptions = new DockerInstanceOptions
                              {
                                  Name = "Production",
                                  BaseUrl = "https://docker.example.test",
                                  Enabled = true,
                              };

        var result = await client.GetImageHistoryAsync(instanceOptions, "sha256:local-image", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "Image history must succeed for a well-formed answer");
        Assert.IsNotNull(result.Data, "Image history must expose the parsed entries");

        var entry = Assert.ContainsSingle(result.Data, "Image history must expose the one entry");

        Assert.AreEqual("/bin/sh -c #(nop)  ENV A=1 2", entry.CreatedBy, "The created-by command must be cleaned");
        Assert.AreEqual("fi nal  stage", entry.Comment, "The comment must be cleaned");
        Assert.AreSequenceEqual(["docker.io/co mp any/api:1.0.0"], entry.Tags.ToArray(), "The tags must be cleaned");
    }

    /// <summary>
    /// Create a Bad Request response with an oversized body
    /// </summary>
    /// <returns>Response</returns>
    private static HttpResponseMessage CreateLongErrorBodyResponse()
    {
        return new HttpResponseMessage(HttpStatusCode.BadRequest)
               {
                   Content = new StringContent(new string('x', OversizedRemoteText.BodyLength)),
               };
    }

    /// <summary>
    /// Run one Docker Engine operation and return its result message
    /// </summary>
    /// <param name="client">Client under test</param>
    /// <param name="operation">Name of the operation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result message</returns>
    private static async Task<string> RunOperationAsync(DockerInstanceClient client, string operation, CancellationToken cancellationToken)
    {
        var instanceOptions = new DockerInstanceOptions
                              {
                                  Name = "Production",
                                  BaseUrl = "https://docker.example.test",
                                  Enabled = true,
                              };

        var (status, message) = operation switch
                                {
                                    "Discovery" => Summarize(await client.DiscoverContainersAsync(instanceOptions, cancellationToken).ConfigureAwait(false)),
                                    "Sampling" => Summarize(await client.CollectContainerResourceUsageAsync(instanceOptions, cancellationToken).ConfigureAwait(false)),
                                    "Inspect" => Summarize(await client.InspectImageAsync(instanceOptions, PooledImageReference, cancellationToken).ConfigureAwait(false)),
                                    "History" => Summarize(await client.GetImageHistoryAsync(instanceOptions, PooledImageReference, cancellationToken).ConfigureAwait(false)),
                                    _ => Summarize(await client.GetHostMemoryTotalAsync(instanceOptions, cancellationToken).ConfigureAwait(false)),
                                };

        Assert.AreEqual(ExternalOperationStatus.Failed, status, "A transport failure must end the operation as failed");

        return message;
    }

    /// <summary>
    /// Reduce an operation result to its status and message
    /// </summary>
    /// <typeparam name="T">Result data type</typeparam>
    /// <param name="result">Operation result</param>
    /// <returns>Status and message</returns>
    private static (ExternalOperationStatus Status, string Message) Summarize<T>(ExternalOperationResult<T> result)
    {
        return (result.Status, result.Message ?? string.Empty);
    }

    /// <summary>
    /// Read the client certificates of the socket handler behind an HTTP client
    /// </summary>
    /// <param name="httpClient">HTTP client created by the Docker HTTP-client factory</param>
    /// <returns>Client certificates, <c>null</c> when none are configured</returns>
    private static X509CertificateCollection? GetClientCertificates(HttpClient httpClient)
    {
        var handlerField = typeof(HttpMessageInvoker).GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.IsNotNull(handlerField, "The handler field of the HTTP client must be discoverable for transport tests");

        var handler = handlerField.GetValue(httpClient) as SocketsHttpHandler;

        Assert.IsNotNull(handler, "The Docker HTTP-client factory must create a socket handler for http and https endpoints");

        return handler.SslOptions.ClientCertificates;
    }

    #endregion // Methods
}