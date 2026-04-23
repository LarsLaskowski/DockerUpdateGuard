using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Configuration;

/// <summary>
/// Validates the host configuration before startup completes
/// </summary>
public class DockerUpdateGuardOptionsValidator : IValidateOptions<DockerUpdateGuardOptions>
{
    #region Fields

    private readonly IConfiguration _configuration;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="configuration">Configuration root</param>
    public DockerUpdateGuardOptionsValidator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    #endregion // Constructors

    #region Methods

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, DockerUpdateGuardOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        var connectionString = DockerUpdateGuardConnectionStringResolver.ResolveConnectionString(options, _configuration);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:ConnectionString' or 'ConnectionStrings:{options.ConnectionStringName}' must be configured");
        }

        ValidateDockerHubOptions(options.DockerHub, failures);
        ValidateVulnerabilityOptions(options.Vulnerabilities, failures);
        ValidateScanningOptions(options.Scanning, failures);
        ValidateDockerInstances(options.DockerInstances, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Validate Docker Hub options
    /// </summary>
    /// <param name="options">Docker Hub options</param>
    /// <param name="failures">Failure list</param>
    private static void ValidateDockerHubOptions(DockerHubOptions options, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.Registry))
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerHub:Registry' must be configured");
        }

        if (options.RequestTimeoutSeconds <= 0)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerHub:RequestTimeoutSeconds' must be greater than zero");
        }

        if (options.MaxParallelRequests <= 0)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerHub:MaxParallelRequests' must be greater than zero");
        }
    }

    /// <summary>
    /// Validate vulnerability options
    /// </summary>
    /// <param name="options">Vulnerability options</param>
    /// <param name="failures">Failure list</param>
    private static void ValidateVulnerabilityOptions(VulnerabilityOptions options, ICollection<string> failures)
    {
        if (options.Enabled && options.Provider == VulnerabilityProviderKind.None)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Vulnerabilities:Provider' must be configured when vulnerability refresh is enabled");
        }

        if (options.Enabled && options.Provider == VulnerabilityProviderKind.Trivy && string.IsNullOrWhiteSpace(options.TrivyBaseUrl))
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Vulnerabilities:TrivyBaseUrl' must be configured when the Trivy provider is selected");
        }

        if (options.RequestTimeoutSeconds <= 0)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Vulnerabilities:RequestTimeoutSeconds' must be greater than zero");
        }
    }

    /// <summary>
    /// Validate scanning options
    /// </summary>
    /// <param name="options">Scanning options</param>
    /// <param name="failures">Failure list</param>
    private static void ValidateScanningOptions(ScanningOptions options, ICollection<string> failures)
    {
        if (options.DiscoveryIntervalMinutes <= 0)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Scanning:DiscoveryIntervalMinutes' must be greater than zero");
        }

        if (options.OwnImageBaseScanIntervalMinutes <= 0)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Scanning:OwnImageBaseScanIntervalMinutes' must be greater than zero");
        }

        if (options.DockerHubAccountDiscoveryIntervalMinutes <= 0)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Scanning:DockerHubAccountDiscoveryIntervalMinutes' must be greater than zero");
        }

        if (options.RuntimeImageUpdateScanIntervalMinutes <= 0)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Scanning:RuntimeImageUpdateScanIntervalMinutes' must be greater than zero");
        }

        if (options.VulnerabilityRefreshIntervalMinutes <= 0)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Scanning:VulnerabilityRefreshIntervalMinutes' must be greater than zero");
        }

        if (options.CleanupIntervalMinutes <= 0)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Scanning:CleanupIntervalMinutes' must be greater than zero");
        }

        if (options.RetainScanRunsDays <= 0)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Scanning:RetainScanRunsDays' must be greater than zero");
        }
    }

    /// <summary>
    /// Validate Docker instances
    /// </summary>
    /// <param name="instances">Configured instances</param>
    /// <param name="failures">Failure list</param>
    private static void ValidateDockerInstances(IEnumerable<DockerInstanceOptions> instances, ICollection<string> failures)
    {
        var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var instance in instances)
        {
            if (string.IsNullOrWhiteSpace(instance.Name))
            {
                failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerInstances:Name' must be configured");

                continue;
            }

            if (knownNames.Add(instance.Name.Trim()) == false)
            {
                failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerInstances' contains duplicate instance name '{instance.Name}'");
            }

            if (string.IsNullOrWhiteSpace(instance.BaseUrl))
            {
                failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerInstances:{instance.Name}:BaseUrl' must be configured");
            }
            else if (TryValidateDockerUri(instance.BaseUrl) == false)
            {
                failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerInstances:{instance.Name}:BaseUrl' must use http, https, tcp, unix or npipe");
            }

            if (instance.RequestTimeoutSeconds <= 0)
            {
                failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerInstances:{instance.Name}:RequestTimeoutSeconds' must be greater than zero");
            }

            if (instance.Portainer.Enabled)
            {
                if (string.IsNullOrWhiteSpace(instance.Portainer.BaseUrl))
                {
                    failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerInstances:{instance.Name}:Portainer:BaseUrl' must be configured when Portainer is enabled");
                }
                else if (Uri.TryCreate(instance.Portainer.BaseUrl, UriKind.Absolute, out var portainerUri) == false
                         || (portainerUri.Scheme != Uri.UriSchemeHttp
                             && portainerUri.Scheme != Uri.UriSchemeHttps))
                {
                    failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerInstances:{instance.Name}:Portainer:BaseUrl' must be an absolute http or https URI");
                }

                var hasApiToken = string.IsNullOrWhiteSpace(instance.Portainer.ApiToken) == false;
                var hasUsernamePassword = string.IsNullOrWhiteSpace(instance.Portainer.Username) == false
                                          && string.IsNullOrWhiteSpace(instance.Portainer.Password) == false;

                if (hasApiToken == false && hasUsernamePassword == false)
                {
                    failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerInstances:{instance.Name}:Portainer' must have either ApiToken or Username+Password configured");
                }

                if (instance.Portainer.RequestTimeoutSeconds <= 0)
                {
                    failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerInstances:{instance.Name}:Portainer:RequestTimeoutSeconds' must be greater than zero");
                }
            }
        }
    }

    /// <summary>
    /// Validate a Docker endpoint URI candidate
    /// </summary>
    /// <param name="value">Endpoint value</param>
    /// <returns>True when valid</returns>
    private static bool TryValidateDockerUri(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) == false)
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttp
               || uri.Scheme == Uri.UriSchemeHttps
               || uri.Scheme == "tcp"
               || uri.Scheme == "unix"
               || uri.Scheme == "npipe";
    }

    #endregion // Methods
}