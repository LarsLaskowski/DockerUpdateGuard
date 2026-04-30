using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Telemetry;

namespace DockerUpdateGuard.Docker;

/// <summary>
/// Conservative Docker engine adapter for HTTP based endpoints
/// </summary>
public class DockerInstanceClient : IDockerInstanceClient
{
    #region Fields

    /// <summary>
    /// Image reference parser
    /// </summary>
    private static readonly IImageReferenceParser ImageReferenceParser = new ImageReferenceParser();

    /// <summary>
    /// HTTP-client factory
    /// </summary>
    private readonly Func<DockerInstanceOptions, Uri, HttpClient> _httpClientFactory;

    /// <summary>
    /// Logger
    /// </summary>
    private readonly ILogger<DockerInstanceClient> _logger;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="logger">Logger</param>
    public DockerInstanceClient(ILogger<DockerInstanceClient> logger)
        : this(logger, CreateHttpClient)
    {
    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="httpClientFactory">HTTP client factory</param>
    public DockerInstanceClient(ILogger<DockerInstanceClient> logger,
                                Func<DockerInstanceOptions, Uri, HttpClient> httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    #endregion // Constructors

    #region Static methods

    /// <summary>
    /// Populate runtime containers with repository digests resolved from Docker image inspect
    /// </summary>
    /// <param name="httpClient">Docker engine client</param>
    /// <param name="containers">Discovered containers</param>
    /// <param name="requestTimeoutSeconds">Configured request timeout in seconds</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private static async Task PopulateRepositoryDigestsAsync(HttpClient httpClient,
                                                             IReadOnlyList<RuntimeContainerDescriptor> containers,
                                                             int requestTimeoutSeconds,
                                                             CancellationToken cancellationToken)
    {
        var digestsByImageId = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var imageIds = containers.Where(entity => string.IsNullOrWhiteSpace(entity.ImageDigest)
                                                  && string.IsNullOrWhiteSpace(entity.LocalImageId) == false)
                                 .Select(entity => entity.LocalImageId!)
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .ToArray();

        foreach (var imageId in imageIds)
        {
            digestsByImageId[imageId] = await GetRepositoryDigestsAsync(httpClient,
                                                                        imageId,
                                                                        requestTimeoutSeconds,
                                                                        cancellationToken).ConfigureAwait(false);
        }

        foreach (var container in containers)
        {
            if (string.IsNullOrWhiteSpace(container.LocalImageId)
                || digestsByImageId.TryGetValue(container.LocalImageId, out var repoDigests) == false)
            {
                continue;
            }

            container.ImageDigest = ResolveRepositoryDigest(container.ImageReference, repoDigests);
        }
    }

    /// <summary>
    /// Read repository digests for a local Docker image
    /// </summary>
    /// <param name="httpClient">Docker engine client</param>
    /// <param name="imageId">Local image identifier</param>
    /// <param name="requestTimeoutSeconds">Configured request timeout in seconds</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Repository digests</returns>
    private static async Task<IReadOnlyList<string>> GetRepositoryDigestsAsync(HttpClient httpClient,
                                                                               string imageId,
                                                                               int requestTimeoutSeconds,
                                                                               CancellationToken cancellationToken)
    {
        var inspectResult = await ReadImageInspectAsync(httpClient,
                                                        imageId,
                                                        requestTimeoutSeconds,
                                                        cancellationToken).ConfigureAwait(false);

        if (inspectResult.Status != ExternalOperationStatus.Succeeded || inspectResult.Data is null)
        {
            return [];
        }

        return inspectResult.Data.RepoDigests;
    }

    /// <summary>
    /// Resolve the repository digest that belongs to the reported image reference
    /// </summary>
    /// <param name="imageReference">Reported image reference</param>
    /// <param name="repoDigests">Available repository digests</param>
    /// <returns>Matching digest</returns>
    private static string? ResolveRepositoryDigest(string imageReference, IReadOnlyList<string> repoDigests)
    {
        if (repoDigests.Count == 0)
        {
            return null;
        }

        if (TryParseImageReference(imageReference, out var parsedImageReference) == false
            || parsedImageReference is null)
        {
            return ExtractDigest(repoDigests[0]);
        }

        foreach (var repoDigest in repoDigests)
        {
            if (TryParseImageReference(repoDigest, out var parsedRepoDigest) == false
                || parsedRepoDigest is null)
            {
                continue;
            }

            if (string.Equals(parsedRepoDigest.Registry,
                              parsedImageReference.Registry,
                              StringComparison.OrdinalIgnoreCase)
                && string.Equals(parsedRepoDigest.Repository,
                                 parsedImageReference.Repository,
                                 StringComparison.OrdinalIgnoreCase))
            {
                return parsedRepoDigest.Digest;
            }
        }

        return repoDigests.Count == 1 ? ExtractDigest(repoDigests[0]) : null;
    }

    /// <summary>
    /// Extract the digest portion from a repository digest reference
    /// </summary>
    /// <param name="repositoryDigest">Repository digest reference</param>
    /// <returns>Digest</returns>
    private static string? ExtractDigest(string repositoryDigest)
    {
        var separatorIndex = repositoryDigest.IndexOf('@');

        return separatorIndex >= 0 && separatorIndex < repositoryDigest.Length - 1
                   ? repositoryDigest[(separatorIndex + 1)..].Trim().ToLowerInvariant()
                   : null;
    }

    /// <summary>
    /// Parse an image reference without throwing for invalid inputs
    /// </summary>
    /// <param name="value">Image reference text</param>
    /// <param name="imageReference">Parsed image reference</param>
    /// <returns>True when parsing succeeded</returns>
    private static bool TryParseImageReference(string value, out ImageReference? imageReference)
    {
        imageReference = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            imageReference = ImageReferenceParser.Parse(value);

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Create a linked cancellation token source that enforces the configured request timeout
    /// </summary>
    /// <param name="requestTimeoutSeconds">Configured request timeout in seconds</param>
    /// <param name="cancellationToken">Upstream cancellation token</param>
    /// <returns>Linked cancellation token source</returns>
    private static CancellationTokenSource CreateRequestTimeoutCancellationTokenSource(int requestTimeoutSeconds, CancellationToken cancellationToken)
    {
        var requestTimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        requestTimeoutSource.CancelAfter(TimeSpan.FromSeconds(requestTimeoutSeconds));

        return requestTimeoutSource;
    }

    /// <summary>
    /// Build an HTTP client for the target Docker endpoint
    /// </summary>
    /// <param name="instanceOptions">Docker instance options</param>
    /// <param name="engineUri">Resolved engine URI</param>
    /// <returns>HTTP client</returns>
    private static HttpClient CreateHttpClient(DockerInstanceOptions instanceOptions, Uri engineUri)
    {
        if (Uri.TryCreate(instanceOptions.BaseUrl, UriKind.Absolute, out var parsedUri)
            && parsedUri.Scheme == "unix")
        {
            return CreateUnixSocketHttpClient(parsedUri.AbsolutePath, instanceOptions);
        }

        if (Uri.TryCreate(instanceOptions.BaseUrl, UriKind.Absolute, out parsedUri)
            && parsedUri.Scheme == "npipe")
        {
            return CreateNamedPipeHttpClient(parsedUri, instanceOptions);
        }

        var handler = new HttpClientHandler
                      {
                          UseProxy = false
                      };

        if (instanceOptions.SkipCertificateValidation)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        var certificatePath = instanceOptions.CertificatePath;

        if (string.IsNullOrWhiteSpace(certificatePath) == false
            && File.Exists(certificatePath))
        {
            handler.ClientCertificates.Add(LoadCertificate(certificatePath));
        }

        return new HttpClient(handler)
               {
                   BaseAddress = engineUri,
                   Timeout = Timeout.InfiniteTimeSpan,
               };
    }

    /// <summary>
    /// Build an HTTP client backed by a Unix domain socket
    /// </summary>
    /// <param name="socketPath">Unix socket path</param>
    /// <param name="instanceOptions">Docker instance options</param>
    /// <returns>HTTP client</returns>
    private static HttpClient CreateUnixSocketHttpClient(string socketPath, DockerInstanceOptions instanceOptions)
    {
        var endpoint = new UnixDomainSocketEndPoint(socketPath);
        var handler = new SocketsHttpHandler
                      {
                          UseProxy = false,
                          ConnectCallback = async (context, cancellationToken) =>
                                            {
                                                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

                                                try
                                                {
                                                    await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);

                                                    return new NetworkStream(socket, ownsSocket: true);
                                                }
                                                catch
                                                {
                                                    socket.Dispose();

                                                    throw;
                                                }
                                            }
                      };

        return new HttpClient(handler)
               {
                   BaseAddress = new Uri("http://localhost/"),
                   Timeout = Timeout.InfiniteTimeSpan,
               };
    }

    /// <summary>
    /// Build an HTTP client backed by a Windows named pipe
    /// </summary>
    /// <param name="pipeUri">Named-pipe URI</param>
    /// <param name="instanceOptions">Docker instance options</param>
    /// <returns>HTTP client</returns>
    private static HttpClient CreateNamedPipeHttpClient(Uri pipeUri, DockerInstanceOptions instanceOptions)
    {
        var serverName = string.IsNullOrWhiteSpace(pipeUri.Host) || pipeUri.Host == "."
                             ? "."
                             : pipeUri.Host;
        var pipeName = GetNamedPipeName(pipeUri);
        var handler = new SocketsHttpHandler
                      {
                          UseProxy = false,
                          ConnectCallback = async (context, cancellationToken) =>
                                            {
                                                var stream = new NamedPipeClientStream(serverName,
                                                                                       pipeName,
                                                                                       PipeDirection.InOut,
                                                                                       PipeOptions.Asynchronous);

                                                try
                                                {
                                                    await stream.ConnectAsync(cancellationToken).ConfigureAwait(false);

                                                    return stream;
                                                }
                                                catch
                                                {
                                                    stream.Dispose();

                                                    throw;
                                                }
                                            }
                      };

        return new HttpClient(handler)
               {
                   BaseAddress = new Uri("http://localhost/"),
                   Timeout = Timeout.InfiniteTimeSpan,
               };
    }

    /// <summary>
    /// Attempt to create an HTTP engine URI
    /// </summary>
    /// <param name="instanceOptions">Docker instance options</param>
    /// <param name="engineUri">Resolved URI</param>
    /// <returns>True when supported</returns>
    private static bool TryCreateEngineUri(DockerInstanceOptions instanceOptions, out Uri? engineUri)
    {
        engineUri = null;

        if (Uri.TryCreate(instanceOptions.BaseUrl, UriKind.Absolute, out var parsedUri) == false)
        {
            return false;
        }

        if (parsedUri.Scheme == Uri.UriSchemeHttp
            || parsedUri.Scheme == Uri.UriSchemeHttps)
        {
            engineUri = EnsureTrailingSlash(parsedUri);

            return true;
        }

        if (parsedUri.Scheme == "tcp")
        {
            var builder = new UriBuilder(parsedUri)
                          {
                              Scheme = instanceOptions.UseTls ? Uri.UriSchemeHttps : Uri.UriSchemeHttp,
                          };

            engineUri = EnsureTrailingSlash(builder.Uri);

            return true;
        }

        if (parsedUri.Scheme == "unix"
            || parsedUri.Scheme == "npipe")
        {
            engineUri = new Uri("http://localhost/");

            return true;
        }

        return false;
    }

    /// <summary>
    /// Ensure a URI uses a trailing slash for relative requests
    /// </summary>
    /// <param name="uri">Original URI</param>
    /// <returns>Normalized URI</returns>
    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var builder = new UriBuilder(uri);

        if (builder.Path.EndsWith('/') == false)
        {
            builder.Path = $"{builder.Path.TrimEnd('/')}/";
        }

        return builder.Uri;
    }

    /// <summary>
    /// Extract the pipe name from an npipe URI
    /// </summary>
    /// <param name="pipeUri">Named-pipe URI</param>
    /// <returns>Pipe name</returns>
    private static string GetNamedPipeName(Uri pipeUri)
    {
        ArgumentNullException.ThrowIfNull(pipeUri);

        var pipePath = pipeUri.AbsolutePath.Trim('/');

        if (pipePath.StartsWith("pipe/", StringComparison.OrdinalIgnoreCase))
        {
            pipePath = pipePath["pipe/".Length..];
        }

        if (string.IsNullOrWhiteSpace(pipePath))
        {
            throw new InvalidOperationException($"The Docker named pipe URI '{pipeUri}' does not contain a pipe name");
        }

        return pipePath;
    }

    /// <summary>
    /// Load a client certificate from the configured file path
    /// </summary>
    /// <param name="certificatePath">Certificate file path</param>
    /// <returns>Loaded certificate</returns>
    private static X509Certificate2 LoadCertificate(string certificatePath)
    {
        var extension = Path.GetExtension(certificatePath);

        return string.Equals(extension,
                             ".pfx",
                             StringComparison.OrdinalIgnoreCase)
               || string.Equals(extension,
                                ".p12",
                                StringComparison.OrdinalIgnoreCase)
                   ? X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password: null)
                   : X509CertificateLoader.LoadCertificateFromFile(certificatePath);
    }

    /// <summary>
    /// Parse a container document
    /// </summary>
    /// <param name="element">JSON element</param>
    /// <returns>Container descriptor</returns>
    private static RuntimeContainerDescriptor ParseContainer(JsonElement element)
    {
        var labels = element.TryGetProperty("Labels", out var labelsElement) && labelsElement.ValueKind == JsonValueKind.Object
                         ? labelsElement
                         : default;

        return new RuntimeContainerDescriptor
               {
                   ContainerId = TryGetString(element, "Id") ?? string.Empty,
                   Name = GetPrimaryName(element),
                   ImageReference = TryGetString(element, "Image") ?? string.Empty,
                   LocalImageId = TryGetString(element, "ImageID"),
                   ComposeProject = TryGetLabel(labels, "com.docker.compose.project"),
                   StackName = TryGetLabel(labels, "com.docker.stack.namespace"),
                   ServiceName = TryGetLabel(labels, "com.docker.swarm.service.name"),
                   RuntimeStatus = ParseRuntimeStatus(TryGetString(element, "State")),
                   IsRunning = string.Equals(TryGetString(element, "State"),
                                             "running",
                                             StringComparison.OrdinalIgnoreCase),
               };
    }

    /// <summary>
    /// Parse a runtime container resource sample
    /// </summary>
    /// <param name="containerElement">Container element</param>
    /// <param name="statsElement">Stats element</param>
    /// <returns>Resource descriptor</returns>
    private static RuntimeContainerResourceDescriptor ParseResourceSample(JsonElement containerElement, JsonElement statsElement)
    {
        var recordedAtUtc = TryParseTimestamp(TryGetString(statsElement, "read"));

        return new RuntimeContainerResourceDescriptor
               {
                   ContainerId = TryGetString(containerElement, "Id") ?? string.Empty,
                   ContainerName = GetPrimaryName(containerElement),
                   CpuPercent = ParseCpuPercent(statsElement),
                   MemoryUsageBytes = ParseMemoryUsage(statsElement),
                   MemoryLimitBytes = ParseMemoryLimit(statsElement),
                   NetworkRxBytesTotal = ParseNetworkTotal(statsElement, "rx_bytes"),
                   NetworkTxBytesTotal = ParseNetworkTotal(statsElement, "tx_bytes"),
                   RecordedAtUtc = recordedAtUtc ?? DateTimeOffset.UtcNow,
               };
    }

    /// <summary>
    /// Extract the primary container name
    /// </summary>
    /// <param name="element">JSON element</param>
    /// <returns>Container name</returns>
    private static string GetPrimaryName(JsonElement element)
    {
        if (element.TryGetProperty("Names", out var namesElement)
            && namesElement.ValueKind == JsonValueKind.Array)
        {
            var firstName = namesElement.EnumerateArray()
                                        .Select(nameElement => nameElement.GetString())
                                        .FirstOrDefault(name => string.IsNullOrWhiteSpace(name) == false);

            if (string.IsNullOrWhiteSpace(firstName) == false)
            {
                return firstName.Trim('/');
            }
        }

        return TryGetString(element, "Id") ?? string.Empty;
    }

    /// <summary>
    /// Read an optional string property
    /// </summary>
    /// <param name="element">JSON element</param>
    /// <param name="propertyName">Property name</param>
    /// <returns>String value</returns>
    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        if (element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        return null;
    }

    /// <summary>
    /// Read a label from a labels object
    /// </summary>
    /// <param name="labelsElement">Labels element</param>
    /// <param name="name">Label name</param>
    /// <returns>Label value</returns>
    private static string? TryGetLabel(JsonElement labelsElement, string name)
    {
        if (labelsElement.ValueKind == JsonValueKind.Object
            && labelsElement.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        return null;
    }

    /// <summary>
    /// Parse the container runtime status
    /// </summary>
    /// <param name="state">Raw state value</param>
    /// <returns>Runtime status</returns>
    private static ContainerRuntimeStatus ParseRuntimeStatus(string? state)
    {
        return state?.ToLowerInvariant() switch
               {
                   "running" => ContainerRuntimeStatus.Running,
                   "paused" => ContainerRuntimeStatus.Paused,
                   "restarting" => ContainerRuntimeStatus.Restarting,
                   "exited" => ContainerRuntimeStatus.Exited,
                   "dead" => ContainerRuntimeStatus.Dead,
                   _ => ContainerRuntimeStatus.NotSet,
               };
    }

    /// <summary>
    /// Parse CPU percentage from a stats document
    /// </summary>
    /// <param name="statsElement">Stats element</param>
    /// <returns>CPU percentage</returns>
    private static decimal ParseCpuPercent(JsonElement statsElement)
    {
        if (statsElement.TryGetProperty("cpu_stats", out var cpuStats) == false
            || statsElement.TryGetProperty("precpu_stats", out var preCpuStats) == false)
        {
            return 0;
        }

        var cpuTotal = TryGetInt64(cpuStats, "cpu_usage", "total_usage");
        var preCpuTotal = TryGetInt64(preCpuStats, "cpu_usage", "total_usage");
        var systemTotal = TryGetInt64(cpuStats, "system_cpu_usage");
        var preSystemTotal = TryGetInt64(preCpuStats, "system_cpu_usage");
        var cpuDelta = cpuTotal - preCpuTotal;
        var systemDelta = systemTotal - preSystemTotal;

        if (cpuDelta <= 0 || systemDelta <= 0)
        {
            return 0;
        }

        var onlineCpuCount = cpuStats.TryGetProperty("online_cpus", out var onlineCpusElement)
                             && onlineCpusElement.TryGetInt32(out var onlineCpus)
                             && onlineCpus > 0
                                 ? onlineCpus
                                 : GetPerCpuCount(cpuStats);

        if (onlineCpuCount <= 0)
        {
            onlineCpuCount = 1;
        }

        var cpuPercent = (decimal)cpuDelta / systemDelta * onlineCpuCount * 100m;

        return cpuPercent < 0 ? 0 : Math.Round(cpuPercent, 4);
    }

    /// <summary>
    /// Parse memory usage in bytes
    /// </summary>
    /// <param name="statsElement">Stats element</param>
    /// <returns>Memory usage</returns>
    private static long ParseMemoryUsage(JsonElement statsElement)
    {
        return statsElement.TryGetProperty("memory_stats", out var memoryStats)
                   ? TryGetInt64(memoryStats, "usage")
                   : 0;
    }

    /// <summary>
    /// Parse memory limit in bytes
    /// </summary>
    /// <param name="statsElement">Stats element</param>
    /// <returns>Memory limit</returns>
    private static long ParseMemoryLimit(JsonElement statsElement)
    {
        return statsElement.TryGetProperty("memory_stats", out var memoryStats)
                   ? TryGetInt64(memoryStats, "limit")
                   : 0;
    }

    /// <summary>
    /// Parse cumulative network totals across all networks
    /// </summary>
    /// <param name="statsElement">Stats element</param>
    /// <param name="propertyName">Property name</param>
    /// <returns>Total bytes</returns>
    private static long ParseNetworkTotal(JsonElement statsElement, string propertyName)
    {
        if (statsElement.TryGetProperty("networks", out var networksElement) == false
            || networksElement.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        var total = 0L;

        foreach (var networkProperty in networksElement.EnumerateObject())
        {
            if (networkProperty.Value.TryGetProperty(propertyName, out var property)
                && property.TryGetInt64(out var value))
            {
                total += value;
            }
        }

        return total;
    }

    /// <summary>
    /// Determine the number of per-CPU entries reported by Docker
    /// </summary>
    /// <param name="cpuStats">CPU stats element</param>
    /// <returns>CPU count</returns>
    private static int GetPerCpuCount(JsonElement cpuStats)
    {
        if (cpuStats.TryGetProperty("cpu_usage", out var cpuUsage) == false
            || cpuUsage.TryGetProperty("percpu_usage", out var perCpuUsage) == false
            || perCpuUsage.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return perCpuUsage.GetArrayLength();
    }

    /// <summary>
    /// Read a nested Int64 property
    /// </summary>
    /// <param name="element">Element</param>
    /// <param name="propertyNames">Property path</param>
    /// <returns>Int64 value or zero</returns>
    private static long TryGetInt64(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var property) == false)
            {
                return 0;
            }

            element = property;
        }

        return element.TryGetInt64(out var value) ? value : 0;
    }

    /// <summary>
    /// Parse an ISO timestamp
    /// </summary>
    /// <param name="value">Timestamp string</param>
    /// <returns>Parsed timestamp</returns>
    private static DateTimeOffset? TryParseTimestamp(string? value)
    {
        return DateTimeOffset.TryParse(value, out var timestamp) ? timestamp : null;
    }

    /// <summary>
    /// Parse Docker image inspect payload
    /// </summary>
    /// <param name="element">Root JSON element</param>
    /// <returns>Inspect data</returns>
    private static DockerImageInspectData ParseImageInspect(JsonElement element)
    {
        var environmentVariables = new List<string>();
        var rootFsLayers = new List<string>();

        if (element.TryGetProperty("Config", out var configElement)
            && configElement.ValueKind == JsonValueKind.Object
            && configElement.TryGetProperty("Env", out var envElement)
            && envElement.ValueKind == JsonValueKind.Array)
        {
            environmentVariables.AddRange(envElement.EnumerateArray()
                                                    .Select(item => item.GetString())
                                                    .Where(value => string.IsNullOrWhiteSpace(value) == false)
                                                    .Cast<string>());
        }

        if (element.TryGetProperty("RootFS", out var rootFsElement)
            && rootFsElement.ValueKind == JsonValueKind.Object
            && rootFsElement.TryGetProperty("Layers", out var layersElement)
            && layersElement.ValueKind == JsonValueKind.Array)
        {
            rootFsLayers.AddRange(layersElement.EnumerateArray()
                                               .Select(item => item.GetString())
                                               .Where(value => string.IsNullOrWhiteSpace(value) == false)
                                               .Cast<string>());
        }

        return new DockerImageInspectData
               {
                   Id = TryGetString(element, "Id") ?? string.Empty,
                   RepoTags = ReadStringArray(element, "RepoTags"),
                   RepoDigests = ReadStringArray(element, "RepoDigests"),
                   EnvironmentVariables = environmentVariables,
                   RootFsLayers = rootFsLayers,
                   CreatedAtUtc = TryParseTimestamp(TryGetString(element, "Created")),
                   OperatingSystem = TryGetString(element, "Os"),
                   Architecture = TryGetString(element, "Architecture"),
               };
    }

    /// <summary>
    /// Parse Docker image history entry
    /// </summary>
    /// <param name="element">History JSON element</param>
    /// <returns>History entry data</returns>
    private static DockerImageHistoryEntryData ParseImageHistoryEntry(JsonElement element)
    {
        return new DockerImageHistoryEntryData
               {
                   CreatedAtUtc = TryParseTimestamp(TryGetString(element, "Created")),
                   CreatedBy = TryGetString(element, "CreatedBy"),
                   Comment = TryGetString(element, "Comment"),
                   Tags = ReadStringArray(element, "Tags"),
               };
    }

    /// <summary>
    /// Read a string array property
    /// </summary>
    /// <param name="element">JSON element</param>
    /// <param name="propertyName">Property name</param>
    /// <returns>String values</returns>
    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property) == false
            || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
                       .Select(item => item.GetString())
                       .Where(value => string.IsNullOrWhiteSpace(value) == false)
                       .Cast<string>()
                       .ToArray();
    }

    /// <summary>
    /// Read image inspect data from the Docker engine
    /// </summary>
    /// <param name="httpClient">Docker HTTP client</param>
    /// <param name="imageReferenceOrId">Image reference or identifier</param>
    /// <param name="requestTimeoutSeconds">Configured request timeout in seconds</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Inspect result</returns>
    private static async Task<ExternalOperationResult<DockerImageInspectData>> ReadImageInspectAsync(HttpClient httpClient,
                                                                                                     string imageReferenceOrId,
                                                                                                     int requestTimeoutSeconds,
                                                                                                     CancellationToken cancellationToken)
    {
        try
        {
            using var requestTimeoutSource = CreateRequestTimeoutCancellationTokenSource(requestTimeoutSeconds, cancellationToken);
            using var response = await httpClient.GetAsync($"v1.41/images/{Uri.EscapeDataString(imageReferenceOrId)}/json", requestTimeoutSource.Token)
                                                 .ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return ExternalOperationResult<DockerImageInspectData>.NotFound($"Docker image '{imageReferenceOrId}' was not found");
            }

            if (response.IsSuccessStatusCode == false)
            {
                var body = await response.Content.ReadAsStringAsync(requestTimeoutSource.Token)
                                                 .ConfigureAwait(false);

                return ExternalOperationResult<DockerImageInspectData>.Failed($"Docker image inspect for '{imageReferenceOrId}' returned {(int)response.StatusCode}: {body}");
            }

            var responseStream = await response.Content.ReadAsStreamAsync(requestTimeoutSource.Token)
                                                       .ConfigureAwait(false);

            await using (responseStream.ConfigureAwait(false))
            {
                using var jsonDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: requestTimeoutSource.Token)
                                                           .ConfigureAwait(false);

                return ExternalOperationResult<DockerImageInspectData>.Succeeded(ParseImageInspect(jsonDocument.RootElement));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return ExternalOperationResult<DockerImageInspectData>.Failed($"Docker image inspect for '{imageReferenceOrId}' timed out after {requestTimeoutSeconds} seconds");
        }
    }

    /// <summary>
    /// Read image history data from the Docker engine
    /// </summary>
    /// <param name="httpClient">Docker HTTP client</param>
    /// <param name="imageReferenceOrId">Image reference or identifier</param>
    /// <param name="requestTimeoutSeconds">Configured request timeout in seconds</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>History result</returns>
    private static async Task<ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>> ReadImageHistoryAsync(HttpClient httpClient,
                                                                                                                         string imageReferenceOrId,
                                                                                                                         int requestTimeoutSeconds,
                                                                                                                         CancellationToken cancellationToken)
    {
        try
        {
            using var requestTimeoutSource = CreateRequestTimeoutCancellationTokenSource(requestTimeoutSeconds, cancellationToken);
            using var response = await httpClient.GetAsync($"v1.41/images/{Uri.EscapeDataString(imageReferenceOrId)}/history", requestTimeoutSource.Token)
                                                 .ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.NotFound($"Docker image '{imageReferenceOrId}' was not found");
            }

            if (response.IsSuccessStatusCode == false)
            {
                var body = await response.Content.ReadAsStringAsync(requestTimeoutSource.Token)
                                                 .ConfigureAwait(false);

                return ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.Failed($"Docker image history for '{imageReferenceOrId}' returned {(int)response.StatusCode}: {body}");
            }

            var responseStream = await response.Content.ReadAsStreamAsync(requestTimeoutSource.Token)
                                                       .ConfigureAwait(false);

            await using (responseStream.ConfigureAwait(false))
            {
                using var jsonDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: requestTimeoutSource.Token)
                                                           .ConfigureAwait(false);
                var historyEntries = new List<DockerImageHistoryEntryData>();

                foreach (var historyElement in jsonDocument.RootElement.EnumerateArray())
                {
                    historyEntries.Add(ParseImageHistoryEntry(historyElement));
                }

                return ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.Succeeded(historyEntries);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.Failed($"Docker image history for '{imageReferenceOrId}' timed out after {requestTimeoutSeconds} seconds");
        }
    }

    #endregion // Static methods

    #region Methods

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>> DiscoverContainersAsync(DockerInstanceOptions instanceOptions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instanceOptions);

        if (instanceOptions.Enabled == false)
        {
            _logger.DockerInstanceDiscoverySkippedDisabled(instanceOptions.Name);

            return ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.NotConfigured($"Docker instance '{instanceOptions.Name}' is disabled");
        }

        if (TryCreateEngineUri(instanceOptions, out var engineUri) == false || engineUri is null)
        {
            _logger.DockerInstanceEndpointUnsupported(instanceOptions.Name, instanceOptions.BaseUrl);

            return ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Unsupported($"Docker instance '{instanceOptions.Name}' uses an unsupported endpoint '{instanceOptions.BaseUrl}'");
        }

        using var activity = DockerUpdateGuardTelemetry.ActivitySource.StartActivity(TelemetryActivityNames.DockerEngineRequest, ActivityKind.Client);

        activity?.SetTag(TelemetryTagNames.DockerInstanceName, instanceOptions.Name);

        try
        {
            using var httpClient = _httpClientFactory(instanceOptions, engineUri);
            using var requestTimeoutSource = CreateRequestTimeoutCancellationTokenSource(instanceOptions.RequestTimeoutSeconds, cancellationToken);
            using var response = await httpClient.GetAsync("v1.41/containers/json?all=1", requestTimeoutSource.Token)
                                                 .ConfigureAwait(false);

            if (response.IsSuccessStatusCode == false)
            {
                var body = await response.Content.ReadAsStringAsync(requestTimeoutSource.Token)
                                                 .ConfigureAwait(false);

                _logger.DockerInstanceDiscoveryResponseFailed(instanceOptions.Name, (int)response.StatusCode);

                return ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Failed($"Docker instance '{instanceOptions.Name}' returned {(int)response.StatusCode}: {body}");
            }

            var responseStreamTask = response.Content.ReadAsStreamAsync(requestTimeoutSource.Token);
            var responseStream = await responseStreamTask.ConfigureAwait(false);

            await using (responseStream.ConfigureAwait(false))
            {
                using var jsonDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: requestTimeoutSource.Token)
                                                           .ConfigureAwait(false);
                var containers = new List<RuntimeContainerDescriptor>();

                foreach (var element in jsonDocument.RootElement.EnumerateArray())
                {
                    containers.Add(ParseContainer(element));
                }

                await PopulateRepositoryDigestsAsync(httpClient,
                                                     containers,
                                                     instanceOptions.RequestTimeoutSeconds,
                                                     cancellationToken).ConfigureAwait(false);

                _logger.DockerInstanceDiscoverySucceeded(instanceOptions.Name, containers.Count);

                return ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Succeeded(containers);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.DockerInstanceDiscoveryTimedOut(instanceOptions.Name, instanceOptions.RequestTimeoutSeconds);

            return ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Failed($"Docker container discovery for '{instanceOptions.Name}' timed out after {instanceOptions.RequestTimeoutSeconds} seconds");
        }
        catch (Exception exception)
        {
            _logger.DockerInstanceDiscoveryFailed(exception, instanceOptions.Name);

            return ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Failed($"Docker container discovery failed for '{instanceOptions.Name}': {exception.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<IReadOnlyList<RuntimeContainerResourceDescriptor>>> CollectContainerResourceUsageAsync(DockerInstanceOptions instanceOptions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instanceOptions);

        if (instanceOptions.Enabled == false)
        {
            return ExternalOperationResult<IReadOnlyList<RuntimeContainerResourceDescriptor>>.NotConfigured($"Docker instance '{instanceOptions.Name}' is disabled");
        }

        if (TryCreateEngineUri(instanceOptions, out var engineUri) == false || engineUri is null)
        {
            return ExternalOperationResult<IReadOnlyList<RuntimeContainerResourceDescriptor>>.Unsupported($"Docker instance '{instanceOptions.Name}' uses an unsupported endpoint '{instanceOptions.BaseUrl}'");
        }

        try
        {
            using var httpClient = _httpClientFactory(instanceOptions, engineUri);
            using var requestTimeoutSource = CreateRequestTimeoutCancellationTokenSource(instanceOptions.RequestTimeoutSeconds, cancellationToken);
            using var response = await httpClient.GetAsync("v1.41/containers/json", requestTimeoutSource.Token)
                                                 .ConfigureAwait(false);

            if (response.IsSuccessStatusCode == false)
            {
                var body = await response.Content.ReadAsStringAsync(requestTimeoutSource.Token)
                                                 .ConfigureAwait(false);

                return ExternalOperationResult<IReadOnlyList<RuntimeContainerResourceDescriptor>>.Failed($"Docker instance '{instanceOptions.Name}' returned {(int)response.StatusCode}: {body}");
            }

            var responseStream = await response.Content.ReadAsStreamAsync(requestTimeoutSource.Token)
                                                       .ConfigureAwait(false);

            await using (responseStream.ConfigureAwait(false))
            {
                using var jsonDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: requestTimeoutSource.Token)
                                                           .ConfigureAwait(false);
                var samples = new List<RuntimeContainerResourceDescriptor>();

                foreach (var containerElement in jsonDocument.RootElement.EnumerateArray())
                {
                    var containerId = TryGetString(containerElement, "Id");

                    if (string.IsNullOrWhiteSpace(containerId))
                    {
                        continue;
                    }

                    using var statsTimeoutSource = CreateRequestTimeoutCancellationTokenSource(instanceOptions.RequestTimeoutSeconds, cancellationToken);
                    using var statsResponse = await httpClient.GetAsync($"v1.41/containers/{Uri.EscapeDataString(containerId)}/stats?stream=false", statsTimeoutSource.Token)
                                                              .ConfigureAwait(false);

                    if (statsResponse.IsSuccessStatusCode == false)
                    {
                        continue;
                    }

                    var statsStream = await statsResponse.Content.ReadAsStreamAsync(statsTimeoutSource.Token)
                                                                 .ConfigureAwait(false);

                    await using (statsStream.ConfigureAwait(false))
                    {
                        using var statsDocument = await JsonDocument.ParseAsync(statsStream, cancellationToken: statsTimeoutSource.Token)
                                                                    .ConfigureAwait(false);
                        samples.Add(ParseResourceSample(containerElement, statsDocument.RootElement));
                    }
                }

                return ExternalOperationResult<IReadOnlyList<RuntimeContainerResourceDescriptor>>.Succeeded(samples);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return ExternalOperationResult<IReadOnlyList<RuntimeContainerResourceDescriptor>>.Failed($"Docker container resource sampling timed out for '{instanceOptions.Name}' after {instanceOptions.RequestTimeoutSeconds} seconds");
        }
        catch (Exception exception)
        {
            return ExternalOperationResult<IReadOnlyList<RuntimeContainerResourceDescriptor>>.Failed($"Docker container resource sampling failed for '{instanceOptions.Name}': {exception.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<DockerImageInspectData>> InspectImageAsync(DockerInstanceOptions instanceOptions,
                                                                                         string imageReferenceOrId,
                                                                                         CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instanceOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageReferenceOrId);

        if (instanceOptions.Enabled == false)
        {
            return ExternalOperationResult<DockerImageInspectData>.NotConfigured($"Docker instance '{instanceOptions.Name}' is disabled");
        }

        if (TryCreateEngineUri(instanceOptions, out var engineUri) == false || engineUri is null)
        {
            return ExternalOperationResult<DockerImageInspectData>.Unsupported($"Docker instance '{instanceOptions.Name}' uses an unsupported endpoint '{instanceOptions.BaseUrl}'");
        }

        try
        {
            using var httpClient = _httpClientFactory(instanceOptions, engineUri);

            return await ReadImageInspectAsync(httpClient,
                                               imageReferenceOrId,
                                               instanceOptions.RequestTimeoutSeconds,
                                               cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ExternalOperationResult<DockerImageInspectData>.Failed($"Docker image inspect failed for '{instanceOptions.Name}': {exception.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>> GetImageHistoryAsync(DockerInstanceOptions instanceOptions,
                                                                                                                string imageReferenceOrId,
                                                                                                                CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instanceOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageReferenceOrId);

        if (instanceOptions.Enabled == false)
        {
            return ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.NotConfigured($"Docker instance '{instanceOptions.Name}' is disabled");
        }

        if (TryCreateEngineUri(instanceOptions, out var engineUri) == false || engineUri is null)
        {
            return ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.Unsupported($"Docker instance '{instanceOptions.Name}' uses an unsupported endpoint '{instanceOptions.BaseUrl}'");
        }

        try
        {
            using var httpClient = _httpClientFactory(instanceOptions, engineUri);

            return await ReadImageHistoryAsync(httpClient,
                                               imageReferenceOrId,
                                               instanceOptions.RequestTimeoutSeconds,
                                               cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.Failed($"Docker image history failed for '{instanceOptions.Name}': {exception.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<long>> GetHostMemoryTotalAsync(DockerInstanceOptions instanceOptions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instanceOptions);

        if (instanceOptions.Enabled == false)
        {
            return ExternalOperationResult<long>.NotConfigured($"Docker instance '{instanceOptions.Name}' is disabled");
        }

        if (TryCreateEngineUri(instanceOptions, out var engineUri) == false || engineUri is null)
        {
            return ExternalOperationResult<long>.Unsupported($"Docker instance '{instanceOptions.Name}' uses an unsupported endpoint '{instanceOptions.BaseUrl}'");
        }

        try
        {
            using var httpClient = _httpClientFactory(instanceOptions, engineUri);
            using var requestTimeoutSource = CreateRequestTimeoutCancellationTokenSource(instanceOptions.RequestTimeoutSeconds, cancellationToken);
            using var response = await httpClient.GetAsync("v1.41/info", requestTimeoutSource.Token)
                                                 .ConfigureAwait(false);

            if (response.IsSuccessStatusCode == false)
            {
                var body = await response.Content.ReadAsStringAsync(requestTimeoutSource.Token)
                                                 .ConfigureAwait(false);

                return ExternalOperationResult<long>.Failed($"Docker instance '{instanceOptions.Name}' returned {(int)response.StatusCode}: {body}");
            }

            var responseStream = await response.Content.ReadAsStreamAsync(requestTimeoutSource.Token)
                                                       .ConfigureAwait(false);

            await using (responseStream.ConfigureAwait(false))
            {
                using var jsonDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: requestTimeoutSource.Token)
                                                           .ConfigureAwait(false);

                return ExternalOperationResult<long>.Succeeded(TryGetInt64(jsonDocument.RootElement, "MemTotal"));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return ExternalOperationResult<long>.Failed($"Docker host info request timed out for '{instanceOptions.Name}' after {instanceOptions.RequestTimeoutSeconds} seconds");
        }
        catch (Exception exception)
        {
            return ExternalOperationResult<long>.Failed($"Docker host info request failed for '{instanceOptions.Name}': {exception.Message}");
        }
    }

    #endregion // Methods
}