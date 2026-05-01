using System.Net;
using System.Net.Http.Headers;
using System.Text;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="RegistryBaseImageResolver"/>
/// </summary>
[TestClass]
public class RegistryBaseImageResolverTests
{
    #region Methods

    /// <summary>
    /// Verify mixed-registry base-image chains use the correct registry API for each step
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RegistryBaseImageResolverResolveAsyncUsesRegistrySpecificEndpointsAcrossRegistryBoundariesAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler)
                         {
                             BaseAddress = new Uri("https://hub.docker.com/"),
                         };
        var dockerHubOptions = new TestOptionsMonitor<DockerUpdateGuardOptions>(new DockerUpdateGuardOptions
                                                                                {
                                                                                    DockerHub = new DockerHubOptions
                                                                                                {
                                                                                                    Registry = "docker.io",
                                                                                                    UserName = "acme",
                                                                                                    Pat = "test-pat",
                                                                                                },
                                                                                });

        try
        {
            handler.AddJsonResponse("https://auth.docker.io/token?service=registry.docker.io&scope=repository:library/app:pull",
                                    """
                                    {
                                      "token": "registry-token"
                                    }
                                    """);
            handler.AddJsonResponse("https://registry-1.docker.io/v2/library/app/manifests/latest",
                                    """
                                    {
                                      "schemaVersion": 2,
                                      "config": {
                                        "digest": "sha256:app-config"
                                      }
                                    }
                                    """);
            handler.AddJsonResponse("https://registry-1.docker.io/v2/library/app/blobs/sha256%3Aapp-config",
                                    """
                                    {
                                      "config": {
                                        "Labels": {
                                          "org.opencontainers.image.base.name": "mcr.microsoft.com/dotnet/runtime:8.0",
                                          "org.opencontainers.image.base.digest": "mcr.microsoft.com/dotnet/runtime@sha256:runtime"
                                        }
                                      }
                                    }
                                    """);
            handler.AddResponse("https://mcr.microsoft.com/v2/dotnet/runtime/manifests/sha256%3Aruntime",
                                CreateUnauthorizedResponse("https://mcr.microsoft.com/oauth2/token",
                                                           "mcr.microsoft.com",
                                                           "repository:dotnet/runtime:pull"));
            handler.AddJsonResponse("https://mcr.microsoft.com/oauth2/token?service=mcr.microsoft.com&scope=repository%3Adotnet%2Fruntime%3Apull",
                                    """
                                    {
                                      "access_token": "mcr-token"
                                    }
                                    """);
            handler.AddResponse("https://mcr.microsoft.com/v2/dotnet/runtime/manifests/sha256%3Aruntime",
                                CreateManifestResponse("sha256:runtime",
                                                       """
                                                       {
                                                         "schemaVersion": 2,
                                                         "config": {
                                                           "digest": "sha256:runtime-config"
                                                         }
                                                       }
                                                       """));
            handler.AddJsonResponse("https://mcr.microsoft.com/v2/dotnet/runtime/blobs/sha256%3Aruntime-config",
                                    """
                                    {
                                      "config": {
                                        "Env": [
                                          "DOTNET_RUNNING_IN_CONTAINER=true"
                                        ]
                                      }
                                    }
                                    """);

            var dockerHubClient = new DockerHubClient(httpClient,
                                                      new TestLogger<DockerHubClient>(),
                                                      dockerHubOptions);
            var ociRegistryClient = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());
            var registryMetadataService = new RegistryMetadataService([dockerHubClient, ociRegistryClient]);
            var resolver = new RegistryBaseImageResolver(registryMetadataService);

            var result = await resolver.ResolveAsync(new ImageReference
                                                     {
                                                         Registry = "docker.io",
                                                         Repository = "library/app",
                                                         Tag = "latest",
                                                     },
                                                     CancellationToken.None)
                                       .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "Base-image resolution must succeed when Docker Hub images reference an MCR parent image");
            Assert.IsNotNull(result.Data, "Base-image resolution must return the discovered chain");
            Assert.HasCount(1,
                            result.Data,
                            "The resolver must return the mixed-registry parent image");
            Assert.AreEqual("mcr.microsoft.com",
                            result.Data[0].Registry,
                            "The discovered parent image must preserve the MCR registry");
            Assert.AreEqual("sha256:runtime",
                            result.Data[0].Digest,
                            "The discovered parent image must normalize the digest label");
            Assert.Contains(request => request.RequestUri == "https://mcr.microsoft.com/v2/dotnet/runtime/manifests/sha256%3Aruntime",
                            handler.Requests,
                            "Cross-registry recursion must query the MCR manifest endpoint for MCR parent images");
            Assert.DoesNotContain(request => string.Equals(request.RequestUri, "https://registry-1.docker.io/v2/dotnet/runtime/manifests/sha256%3Aruntime", StringComparison.Ordinal),
                                  handler.Requests,
                                  "Cross-registry recursion must not fall back to Docker Hub endpoints for MCR parent images");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    #endregion // Methods

    #region Static methods

    /// <summary>
    /// Create a manifest response with a content digest header
    /// </summary>
    /// <param name="digest">Digest header value</param>
    /// <param name="json">Manifest JSON payload</param>
    /// <returns>Prepared response</returns>
    private static HttpResponseMessage CreateManifestResponse(string digest, string json)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
                       {
                           Content = new StringContent(json, Encoding.UTF8, "application/vnd.docker.distribution.manifest.v2+json"),
                       };

        response.Headers.TryAddWithoutValidation("Docker-Content-Digest", digest);

        return response;
    }

    /// <summary>
    /// Create an unauthorized bearer-auth challenge response
    /// </summary>
    /// <param name="realm">Bearer token endpoint</param>
    /// <param name="service">Registry service name</param>
    /// <param name="scope">Bearer scope</param>
    /// <returns>Prepared response</returns>
    private static HttpResponseMessage CreateUnauthorizedResponse(string realm, string service, string scope)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);

        response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer",
                                                                           $"realm=\"{realm}\",service=\"{service}\",scope=\"{scope}\""));

        return response;
    }

    #endregion // Static methods
}