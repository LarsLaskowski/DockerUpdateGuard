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

        ValidateAbsoluteHttpUri(options.ApiBaseUrl,
                                $"{DockerUpdateGuardOptions.SectionName}:DockerHub:ApiBaseUrl",
                                failures);
    }

    /// <summary>
    /// Validate release metadata options
    /// </summary>
    /// <param name="options">Release metadata options</param>
    /// <param name="failures">Failure list</param>
    private static void ValidateReleaseMetadataOptions(ReleaseMetadataOptions options, ICollection<string> failures)
    {
        ValidateAbsoluteHttpUri(options.DotNetBaseUrl,
                                $"{DockerUpdateGuardOptions.SectionName}:ReleaseMetadata:DotNetBaseUrl",
                                failures);
        ValidateAbsoluteHttpUri(options.NginxBaseUrl,
                                $"{DockerUpdateGuardOptions.SectionName}:ReleaseMetadata:NginxBaseUrl",
                                failures);
    }

    /// <summary>
    /// Validate that a value is an absolute http or https URI
    /// </summary>
    /// <param name="value">Value to validate</param>
    /// <param name="propertyPath">Fully qualified configuration path</param>
    /// <param name="failures">Failure list</param>
    private static void ValidateAbsoluteHttpUri(string value, string propertyPath, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"'{propertyPath}' must be configured");

            return;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) == false
            || (uri.Scheme != Uri.UriSchemeHttp
                && uri.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add($"'{propertyPath}' must be an absolute http or https URI");
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

        if (options.Enabled && options.Provider == VulnerabilityProviderKind.Trivy)
        {
            ValidateAbsoluteHttpUri(options.TrivyBaseUrl ?? string.Empty,
                                    $"{DockerUpdateGuardOptions.SectionName}:Vulnerabilities:TrivyBaseUrl",
                                    failures);
        }

        if (options.RequestTimeoutSeconds is <= 0 or > 300)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Vulnerabilities:RequestTimeoutSeconds' must be between 1 and 300");
        }

        ValidateAbsoluteHttpUri(options.DockerScoutLoginUrl,
                                $"{DockerUpdateGuardOptions.SectionName}:Vulnerabilities:DockerScoutLoginUrl",
                                failures);
        ValidateAbsoluteHttpUri(options.DockerScoutBaseUrl,
                                $"{DockerUpdateGuardOptions.SectionName}:Vulnerabilities:DockerScoutBaseUrl",
                                failures);
    }

    /// <summary>
    /// Validate scanning options
    /// </summary>
    /// <param name="options">Scanning options</param>
    /// <param name="failures">Failure list</param>
    private static void ValidateScanningOptions(ScanningOptions options, ICollection<string> failures)
    {
        ValidateRange(options.DiscoveryIntervalMinutes,
                      1,
                      1440,
                      $"{DockerUpdateGuardOptions.SectionName}:Scanning:DiscoveryIntervalMinutes",
                      failures);
        ValidateRange(options.OwnImageBaseScanIntervalMinutes,
                      1,
                      10080,
                      $"{DockerUpdateGuardOptions.SectionName}:Scanning:OwnImageBaseScanIntervalMinutes",
                      failures);
        ValidateRange(options.DockerHubRequestLimitWindowHours,
                      1,
                      168,
                      $"{DockerUpdateGuardOptions.SectionName}:Scanning:DockerHubRequestLimitWindowHours",
                      failures);
        ValidateRange(options.DockerHubRequestLimitPerWindow,
                      1,
                      100000,
                      $"{DockerUpdateGuardOptions.SectionName}:Scanning:DockerHubRequestLimitPerWindow",
                      failures);
        ValidateRange(options.DockerHubReservedManualRequestsPerWindow,
                      0,
                      100000,
                      $"{DockerUpdateGuardOptions.SectionName}:Scanning:DockerHubReservedManualRequestsPerWindow",
                      failures);

        if (options.DockerHubReservedManualRequestsPerWindow >= options.DockerHubRequestLimitPerWindow)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:Scanning:DockerHubReservedManualRequestsPerWindow' must be smaller than DockerHubRequestLimitPerWindow");
        }

        ValidateRange(options.DockerHubAccountDiscoveryIntervalMinutes,
                      1,
                      10080,
                      $"{DockerUpdateGuardOptions.SectionName}:Scanning:DockerHubAccountDiscoveryIntervalMinutes",
                      failures);
        ValidateRange(options.RuntimeImageUpdateScanIntervalMinutes,
                      1,
                      1440,
                      $"{DockerUpdateGuardOptions.SectionName}:Scanning:RuntimeImageUpdateScanIntervalMinutes",
                      failures);
        ValidateRange(options.ResourceStatisticsIntervalMinutes,
                      1,
                      1440,
                      $"{DockerUpdateGuardOptions.SectionName}:Scanning:ResourceStatisticsIntervalMinutes",
                      failures);
        ValidateRange(options.VulnerabilityRefreshIntervalMinutes,
                      1,
                      10080,
                      $"{DockerUpdateGuardOptions.SectionName}:Scanning:VulnerabilityRefreshIntervalMinutes",
                      failures);
        ValidateRange(options.CleanupIntervalMinutes,
                      1,
                      10080,
                      $"{DockerUpdateGuardOptions.SectionName}:Scanning:CleanupIntervalMinutes",
                      failures);
        ValidateRange(options.RetryCount,
                      0,
                      10,
                      $"{DockerUpdateGuardOptions.SectionName}:Scanning:RetryCount",
                      failures);
        ValidateRange(options.RetainScanRunsDays,
                      1,
                      3650,
                      $"{DockerUpdateGuardOptions.SectionName}:Scanning:RetainScanRunsDays",
                      failures);
    }

    /// <summary>
    /// Validate that a value falls within an inclusive range
    /// </summary>
    /// <param name="value">Value to validate</param>
    /// <param name="minimum">Inclusive minimum</param>
    /// <param name="maximum">Inclusive maximum</param>
    /// <param name="propertyPath">Fully qualified configuration path</param>
    /// <param name="failures">Failure list</param>
    private static void ValidateRange(int value, int minimum, int maximum, string propertyPath, ICollection<string> failures)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add($"'{propertyPath}' must be between {minimum} and {maximum}");
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

            ValidateDockerInstance(instance, failures);
        }
    }

    /// <summary>
    /// Validate a single Docker instance
    /// </summary>
    /// <param name="instance">Docker instance</param>
    /// <param name="failures">Failure list</param>
    private static void ValidateDockerInstance(DockerInstanceOptions instance, ICollection<string> failures)
    {
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
            ValidatePortainerOptions(instance.Name, instance.Portainer, failures);
        }
    }

    /// <summary>
    /// Validate the Portainer configuration of a Docker instance
    /// </summary>
    /// <param name="instanceName">Owning Docker instance name</param>
    /// <param name="portainer">Portainer options</param>
    /// <param name="failures">Failure list</param>
    private static void ValidatePortainerOptions(string instanceName, PortainerOptions portainer, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(portainer.BaseUrl))
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerInstances:{instanceName}:Portainer:BaseUrl' must be configured when Portainer is enabled");
        }
        else if (Uri.TryCreate(portainer.BaseUrl, UriKind.Absolute, out var portainerUri) == false
                 || (portainerUri.Scheme != Uri.UriSchemeHttp
                     && portainerUri.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerInstances:{instanceName}:Portainer:BaseUrl' must be an absolute http or https URI");
        }
        else if (portainerUri.Scheme == Uri.UriSchemeHttp && portainer.AllowInsecureHttp == false)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerInstances:{instanceName}:Portainer:BaseUrl' uses plaintext HTTP; API keys, passwords and tokens will be transmitted unencrypted — set '{DockerUpdateGuardOptions.SectionName}:DockerInstances:{instanceName}:Portainer:AllowInsecureHttp: true' to explicitly allow it");
        }

        var hasApiToken = string.IsNullOrWhiteSpace(portainer.ApiToken) == false;
        var hasUsernamePassword = string.IsNullOrWhiteSpace(portainer.Username) == false
                                  && string.IsNullOrWhiteSpace(portainer.Password) == false;

        if (hasApiToken == false && hasUsernamePassword == false)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerInstances:{instanceName}:Portainer' must have either ApiToken or Username+Password configured");
        }

        if (portainer.RequestTimeoutSeconds is <= 0 or > 300)
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerInstances:{instanceName}:Portainer:RequestTimeoutSeconds' must be between 1 and 300");
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

    #region IValidateOptions

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
        ValidateReleaseMetadataOptions(options.ReleaseMetadata, failures);
        ValidateVulnerabilityOptions(options.Vulnerabilities, failures);
        ValidateScanningOptions(options.Scanning, failures);
        ValidateDockerInstances(options.DockerInstances, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    #endregion // IValidateOptions
}