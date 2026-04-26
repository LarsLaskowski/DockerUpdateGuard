using System.Net;
using System.Net.Http.Headers;
using System.Text;

using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="OciRegistryClient"/>
/// </summary>
[TestClass]
public class OciRegistryClientTests
{
    #region Methods

    /// <summary>
    /// Verify OCI registries can authenticate and return tag metadata
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetTagsAsyncReadsTagsFromMicrosoftRegistryAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/tags/list?n=100",
                                CreateUnauthorizedResponse("https://mcr.microsoft.com/oauth2/token",
                                                           "mcr.microsoft.com",
                                                           "repository:mssql/server:pull"));
            handler.AddJsonResponse("https://mcr.microsoft.com/oauth2/token?service=mcr.microsoft.com&scope=repository%3Amssql%2Fserver%3Apull",
                                    """
                                    {
                                      "access_token": "mcr-token"
                                    }
                                    """);
            handler.AddJsonResponse("https://mcr.microsoft.com/v2/mssql/server/tags/list?n=100",
                                    """
                                    {
                                      "name": "mssql/server",
                                      "tags": [
                                        "2019-CU32-GDR7-ubuntu-20.04"
                                      ]
                                    }
                                    """);
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/manifests/2019-CU32-GDR7-ubuntu-20.04",
                                CreateManifestResponse("sha256:mcr-tag",
                                                       """
                                                       {
                                                         "schemaVersion": 2,
                                                         "config": {
                                                           "digest": "sha256:config"
                                                         }
                                                       }
                                                       """));

            var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

            var result = await client.GetTagsAsync("mcr.microsoft.com",
                                                   "mssql/server",
                                                   CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "OCI registry tag lookup must succeed when the registry challenge and manifest requests complete");
            Assert.IsNotNull(result.Data, "OCI registry tag lookup must return tag data");
            Assert.AreEqual(1,
                            result.Data.Count,
                            "OCI registry tag lookup must return all discovered tags");
            Assert.AreEqual("2019-CU32-GDR7-ubuntu-20.04",
                            result.Data[0].Tag,
                            "OCI registry tag lookup must keep the returned tag name");
            Assert.AreEqual("sha256:mcr-tag",
                            result.Data[0].Digest,
                            "OCI registry tag lookup must expose the manifest digest");
            Assert.IsTrue(handler.Requests.Any(request => request.RequestUri == "https://mcr.microsoft.com/v2/mssql/server/tags/list?n=100"
                                                          && request.AuthorizationScheme == "Bearer"
                                                          && request.AuthorizationParameter == "mcr-token"),
                          "OCI registry tag lookup must retry the tags endpoint with the resolved bearer token");
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
    /// Sequence-based HTTP message handler for deterministic OCI registry tests
    /// </summary>
    private sealed class SequenceHttpMessageHandler : HttpMessageHandler
    {
        #region Fields

        private readonly Dictionary<string, Queue<HttpResponseMessage>> _responses = new(StringComparer.Ordinal);
        private readonly List<ObservedRequest> _requests = [];

        #endregion // Fields

        #region Properties

        /// <summary>
        /// Observed outbound requests
        /// </summary>
        public IReadOnlyList<ObservedRequest> Requests => _requests;

        #endregion // Properties

        #region Methods

        /// <summary>
        /// Add a response to the request sequence for a URI
        /// </summary>
        /// <param name="requestUri">Absolute request URI</param>
        /// <param name="response">Configured response</param>
        public void AddResponse(string requestUri, HttpResponseMessage response)
        {
            var normalizedRequestUri = new Uri(requestUri).AbsoluteUri;

            if (_responses.TryGetValue(normalizedRequestUri, out var queue) == false)
            {
                queue = new Queue<HttpResponseMessage>();
                _responses[normalizedRequestUri] = queue;
            }

            queue.Enqueue(response);
        }

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

            _requests.Add(new ObservedRequest
                          {
                              RequestUri = request.RequestUri.ToString(),
                              AuthorizationScheme = request.Headers.Authorization?.Scheme,
                              AuthorizationParameter = request.Headers.Authorization?.Parameter,
                          });

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

        #endregion // Methods
    }

    /// <summary>
    /// Observed outbound request data
    /// </summary>
    private sealed class ObservedRequest
    {
        #region Properties

        /// <summary>
        /// Authorization parameter
        /// </summary>
        public string? AuthorizationParameter { get; init; }

        /// <summary>
        /// Authorization scheme
        /// </summary>
        public string? AuthorizationScheme { get; init; }

        /// <summary>
        /// Request URI
        /// </summary>
        public string RequestUri { get; init; } = string.Empty;

        #endregion // Properties
    }

    #endregion // Helper types

    #region Static methods

    /// <summary>
    /// Create an unauthorized response with a bearer challenge
    /// </summary>
    /// <param name="realm">Token realm</param>
    /// <param name="service">Registry service</param>
    /// <param name="scope">Repository scope</param>
    /// <returns>Unauthorized response</returns>
    private static HttpResponseMessage CreateUnauthorizedResponse(string realm, string service, string scope)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);

        response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer",
                                                                           $"realm=\"{realm}\",service=\"{service}\",scope=\"{scope}\""));

        return response;
    }

    /// <summary>
    /// Create a manifest response with a digest header
    /// </summary>
    /// <param name="digest">Manifest digest</param>
    /// <param name="jsonContent">Manifest JSON</param>
    /// <returns>Manifest response</returns>
    private static HttpResponseMessage CreateManifestResponse(string digest, string jsonContent)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
                       {
                           Content = new StringContent(jsonContent,
                                                       Encoding.UTF8,
                                                       "application/vnd.oci.image.manifest.v1+json"),
                       };

        response.Headers.TryAddWithoutValidation("Docker-Content-Digest", digest);

        return response;
    }

    #endregion // Static methods
}