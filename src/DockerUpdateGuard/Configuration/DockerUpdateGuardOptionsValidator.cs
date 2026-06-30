using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Configuration;

/// <summary>
/// Validates the host configuration before startup completes
/// </summary>
public class DockerUpdateGuardOptionsValidator : IValidateOptions<DockerUpdateGuardOptions>
{
    #region Fields

    /// <summary>
    /// Configuration root
    /// </summary>
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

    #region Static methods

    /// <summary>
    /// Validate database options
    /// </summary>
    /// <param name="options">Database options</param>
    /// <param name="failures">Failure list</param>
    private static void ValidateDatabaseOptions(DatabaseOptions options, List<string> failures)
    {
        if (options.MaxConnectionRetryCount <= 0)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Database:MaxConnectionRetryCount' must be greater than zero");
        }

        if (options.MaxConnectionRetryDelaySeconds <= 0)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Database:MaxConnectionRetryDelaySeconds' must be greater than zero");
        }

        if (options.MigrationStartupTimeoutSeconds <= 0)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Database:MigrationStartupTimeoutSeconds' must be greater than zero");
        }

        if (options.MigrationRetryDelaySeconds <= 0)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Database:MigrationRetryDelaySeconds' must be greater than zero");
        }
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

        if (options.RequestTimeoutSeconds is <= 0 or > 300)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerHub:RequestTimeoutSeconds' must be between 1 and 300");
        }

        if (options.MaxParallelRequests is <= 0 or > 32)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerHub:MaxParallelRequests' must be between 1 and 32");
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

        if (options.RequestTimeoutSeconds is <= 0 or > 300)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Vulnerabilities:RequestTimeoutSeconds' must be between 1 and 300");
        }
    }

    /// <summary>
    /// Validate scanning options
    /// </summary>
    /// <param name="options">Scanning options</param>
    /// <param name="failures">Failure list</param>
    private static void ValidateScanningOptions(ScanningOptions options, ICollection<string> failures)
    {
        if (options.DiscoveryIntervalMinutes is <= 0 or > 1440)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Scanning:DiscoveryIntervalMinutes' must be between 1 and 1440");
        }

        if (options.OwnImageBaseScanIntervalMinutes is <= 0 or > 10080)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Scanning:OwnImageBaseScanIntervalMinutes' must be between 1 and 10080");
        }

        if (options.DockerHubRequestLimitWindowHours is <= 0 or > 168)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Scanning:DockerHubRequestLimitWindowHours' must be between 1 and 168");
        }

        if (options.DockerHubRequestLimitPerWindow is <= 0 or > 100000)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Scanning:DockerHubRequestLimitPerWindow' must be between 1 and 100000");
        }

        if (options.DockerHubReservedManualRequestsPerWindow is < 0 or > 100000)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Scanning:DockerHubReservedManualRequestsPerWindow' must be between 0 and 100000");
        }

        if (options.DockerHubReservedManualRequestsPerWindow >= options.DockerHubRequestLimitPerWindow)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Scanning:DockerHubReservedManualRequestsPerWindow' must be smaller than DockerHubRequestLimitPerWindow");
        }

        if (options.DockerHubAccountDiscoveryIntervalMinutes is <= 0 or > 10080)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Scanning:DockerHubAccountDiscoveryIntervalMinutes' must be between 1 and 10080");
        }

        if (options.RuntimeImageUpdateScanIntervalMinutes is <= 0 or > 1440)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Scanning:RuntimeImageUpdateScanIntervalMinutes' must be between 1 and 1440");
        }

        if (options.ResourceStatisticsIntervalMinutes is <= 0 or > 1440)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Scanning:ResourceStatisticsIntervalMinutes' must be between 1 and 1440");
        }

        if (options.VulnerabilityRefreshIntervalMinutes is <= 0 or > 10080)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Scanning:VulnerabilityRefreshIntervalMinutes' must be between 1 and 10080");
        }

        if (options.CleanupIntervalMinutes is <= 0 or > 10080)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Scanning:CleanupIntervalMinutes' must be between 1 and 10080");
        }

        if (options.RetryCount is < 0 or > 10)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Scanning:RetryCount' must be between 0 and 10");
        }

        if (options.RetainScanRunsDays is <= 0 or > 3650)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Scanning:RetainScanRunsDays' must be between 1 and 3650");
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

            if (instance.RequestTimeoutSeconds is <= 0 or > 300)
            {
                failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerInstances:{instance.Name}:RequestTimeoutSeconds' must be between 1 and 300");
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
                else if (portainerUri.Scheme == Uri.UriSchemeHttp && instance.Portainer.AllowInsecureHttp == false)
                {
                    failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerInstances:{instance.Name}:Portainer:BaseUrl' uses plaintext HTTP; API keys, passwords and tokens will be transmitted unencrypted — set '{DockerUpdateGuardOptions.SectionName}:DockerInstances:{instance.Name}:Portainer:AllowInsecureHttp: true' to explicitly allow it");
                }

                var hasApiToken = string.IsNullOrWhiteSpace(instance.Portainer.ApiToken) == false;
                var hasUsernamePassword = string.IsNullOrWhiteSpace(instance.Portainer.Username) == false
                                          && string.IsNullOrWhiteSpace(instance.Portainer.Password) == false;

                if (hasApiToken == false && hasUsernamePassword == false)
                {
                    failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerInstances:{instance.Name}:Portainer' must have either ApiToken or Username+Password configured");
                }

                if (instance.Portainer.RequestTimeoutSeconds is <= 0 or > 300)
                {
                    failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerInstances:{instance.Name}:Portainer:RequestTimeoutSeconds' must be between 1 and 300");
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

    #endregion // Static methods

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

        ValidateDatabaseOptions(options.Database, failures);
        ValidateDockerHubOptions(options.DockerHub, failures);
        ValidateVulnerabilityOptions(options.Vulnerabilities, failures);
        ValidateScanningOptions(options.Scanning, failures);
        ValidateDockerInstances(options.DockerInstances, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    #endregion // Methods
}