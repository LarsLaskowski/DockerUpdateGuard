using System.Net;
using System.Text;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Docker;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;

using Microsoft.Extensions.Logging;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DockerInstanceClient"/>
/// </summary>
[TestClass]
public class DockerInstanceClientTests
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
        Assert.IsTrue(logger.Entries.Any(entry => entry.EventId.Id == 3100
                                                  && entry.LogLevel == LogLevel.Information
                                                  && entry.Message.Contains("Production", StringComparison.Ordinal)),
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
        Assert.IsTrue(logger.Entries.Any(entry => entry.EventId.Id == 3101
                                                  && entry.LogLevel == LogLevel.Warning
                                                  && entry.Message.Contains("ssh://docker.example.test", StringComparison.Ordinal)),
                      "Unsupported Docker endpoints must log the rejected endpoint value as a warning");
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

    #endregion // Methods

    #region Helper types

    /// <summary>
    /// Sequence-based HTTP message handler for deterministic Docker engine tests
    /// </summary>
    private sealed class SequenceHttpMessageHandler : HttpMessageHandler
    {
        #region Fields

        private readonly Dictionary<string, Queue<HttpResponseMessage>> _responses = new(StringComparer.Ordinal);

        #endregion // Fields

        #region Methods

        /// <summary>
        /// Add a JSON response to the request sequence for a URI
        /// </summary>
        /// <param name="requestUri">Absolute request URI</param>
        /// <param name="jsonContent">JSON content</param>
        public void AddJsonResponse(string requestUri, string jsonContent)
        {
            AddResponse(requestUri,
                        new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(jsonContent,
                                                        Encoding.UTF8,
                                                        "application/json"),
                        });
        }

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request.RequestUri);

            if (_responses.TryGetValue(request.RequestUri.AbsoluteUri, out var queue)
                && queue.Count > 0)
            {
                return Task.FromResult(CloneResponse(queue.Dequeue()));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                                   {
                                       RequestMessage = request,
                                   });
        }

        /// <summary>
        /// Clone a configured response for repeatable handler usage
        /// </summary>
        /// <param name="response">Template response</param>
        /// <returns>Cloned response</returns>
        private static HttpResponseMessage CloneResponse(HttpResponseMessage response)
        {
            var clone = new HttpResponseMessage(response.StatusCode)
                        {
                            Content = response.Content is null
                                          ? null
                                          : new StringContent(response.Content.ReadAsStringAsync().GetAwaiter().GetResult(),
                                                              Encoding.UTF8,
                                                              response.Content.Headers.ContentType?.MediaType),
                        };

            foreach (var header in response.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }

        /// <summary>
        /// Add a response to the request sequence for a URI
        /// </summary>
        /// <param name="requestUri">Absolute request URI</param>
        /// <param name="response">Configured response</param>
        private void AddResponse(string requestUri, HttpResponseMessage response)
        {
            var normalizedRequestUri = new Uri(requestUri).AbsoluteUri;

            if (_responses.TryGetValue(normalizedRequestUri, out var queue) == false)
            {
                queue = new Queue<HttpResponseMessage>();

                _responses[normalizedRequestUri] = queue;
            }

            queue.Enqueue(response);
        }

        #endregion // Methods
    }

    #endregion // Helper types
}