using System.Reflection;

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
        Assert.IsFalse(logger.Entries.Any(entry => entry.EventId.Id == 3104),
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
            Assert.AreEqual(1,
                            result.Data.Count,
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
            CollectionAssert.AreEqual(new[] { "DOTNET_VERSION=9.0.13", "ASPNET_VERSION=9.0.13" },
                                      result.Data.EnvironmentVariables.ToArray(),
                                      "Image inspect must expose environment variables from the Docker image config");
            CollectionAssert.AreEqual(new[] { "sha256:layer-1", "sha256:layer-2" },
                                      result.Data.RootFsLayers.ToArray(),
                                      "Image inspect must expose the rootfs layer chain");
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
            CollectionAssert.AreEqual(new[] { "docker.io/company/api:1.0.0" },
                                      result.Data.Single().Tags.ToArray(),
                                      "Image history must expose tags when the Docker engine returns them");
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
    /// Verify the Docker HTTP-client factory logs a warning when server certificate validation is disabled
    /// </summary>
    [TestMethod]
    public void DockerInstanceClientCreateHttpClientWhenSkipCertificateValidationLogsWarning()
    {
        var createHttpClientMethod = typeof(DockerInstanceClient).GetMethod("CreateHttpClient",
                                                                            BindingFlags.Static | BindingFlags.NonPublic);
        var instanceOptions = new DockerInstanceOptions
                              {
                                  Name = "Production",
                                  BaseUrl = "https://docker.example.test",
                                  Enabled = true,
                                  SkipCertificateValidation = true,
                              };
        var engineUri = new Uri("https://docker.example.test/");
        var logger = new TestLogger<DockerInstanceClient>();

        Assert.IsNotNull(createHttpClientMethod, "The Docker HTTP-client factory must remain discoverable for transport tests");

        using var httpClient = (HttpClient?)createHttpClientMethod.Invoke(null, [instanceOptions, engineUri, logger]);

        Assert.IsNotNull(httpClient, "The Docker HTTP-client factory must return an HTTP client for TLS endpoints");
        Assert.Contains(entry => entry.EventId.Id == 3106
                                 && entry.LogLevel == LogLevel.Warning
                                 && entry.Message.Contains("Production", StringComparison.Ordinal),
                        logger.Entries,
                        "Disabling server certificate validation must emit an auditable warning naming the affected instance");
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

    #endregion // Methods
}