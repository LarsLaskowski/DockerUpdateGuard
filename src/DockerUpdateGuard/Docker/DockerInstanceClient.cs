using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Telemetry;

namespace DockerUpdateGuard.Docker;

/// <summary>
/// Conservative Docker engine adapter for HTTP based endpoints
/// </summary>
public class DockerInstanceClient : IDockerInstanceClient
{
    #region Fields

    private readonly ILogger<DockerInstanceClient> _logger;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="logger">Logger</param>
    public DockerInstanceClient(ILogger<DockerInstanceClient> logger)
    {
        _logger = logger;
    }

    #endregion // Constructors

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
            using var httpClient = CreateHttpClient(instanceOptions, engineUri);
            using var response = await httpClient.GetAsync("v1.41/containers/json?all=1", cancellationToken)
                                                 .ConfigureAwait(false);

            if (response.IsSuccessStatusCode == false)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken)
                                                 .ConfigureAwait(false);

                _logger.DockerInstanceDiscoveryResponseFailed(instanceOptions.Name, (int)response.StatusCode);

                return ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Failed($"Docker instance '{instanceOptions.Name}' returned {(int)response.StatusCode}: {body}");
            }

            var responseStreamTask = response.Content.ReadAsStreamAsync(cancellationToken);
            var responseStream = await responseStreamTask.ConfigureAwait(false);

            await using (responseStream.ConfigureAwait(false))
            {
                using var jsonDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken)
                                                           .ConfigureAwait(false);
                var containers = new List<RuntimeContainerDescriptor>();

                foreach (var element in jsonDocument.RootElement.EnumerateArray())
                {
                    containers.Add(ParseContainer(element));
                }

                _logger.DockerInstanceDiscoverySucceeded(instanceOptions.Name, containers.Count);

                return ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Succeeded(containers);
            }
        }
        catch (Exception exception)
        {
            _logger.DockerInstanceDiscoveryFailed(exception, instanceOptions.Name);

            return ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Failed($"Docker container discovery failed for '{instanceOptions.Name}': {exception.Message}");
        }
    }

    /// <summary>
    /// Build an HTTP client for the target Docker endpoint
    /// </summary>
    /// <param name="instanceOptions">Docker instance options</param>
    /// <param name="engineUri">Resolved engine URI</param>
    /// <returns>HTTP client</returns>
    private static HttpClient CreateHttpClient(DockerInstanceOptions instanceOptions, Uri engineUri)
    {
        var handler = new HttpClientHandler();

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
                   Timeout = TimeSpan.FromSeconds(instanceOptions.RequestTimeoutSeconds),
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
                   ImageDigest = TryGetString(element, "ImageID"),
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

    #endregion // Methods
}