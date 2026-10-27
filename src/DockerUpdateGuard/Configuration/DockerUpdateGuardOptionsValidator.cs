using System.Diagnostics.CodeAnalysis;

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

    /// <summary>
    /// Client certificate loader
    /// </summary>
    private readonly ClientCertificateLoader _clientCertificateLoader;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="configuration">Configuration root</param>
    public DockerUpdateGuardOptionsValidator(IConfiguration configuration)
        : this(configuration, new ClientCertificateLoader())
    {
    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="configuration">Configuration root</param>
    /// <param name="clientCertificateLoader">Client certificate loader</param>
    public DockerUpdateGuardOptionsValidator(IConfiguration configuration, ClientCertificateLoader clientCertificateLoader)
    {
        _configuration = configuration;
        _clientCertificateLoader = clientCertificateLoader;
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
    private static void ValidateDockerHubOptions(DockerHubOptions options, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.Registry))
        {
            failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerHub:Registry' must be configured");
        }
        else if (Uri.TryCreate(options.Registry, UriKind.Absolute, out var registryUri)
                 && HasAtSignOutsideUserInfo(options.Registry, registryUri))
        {
            failures.Add(CreateAtSignFailure($"'{DockerUpdateGuardOptions.SectionName}:DockerHub:Registry'"));
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
    private static void ValidateReleaseMetadataOptions(ReleaseMetadataOptions options, List<string> failures)
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
    private static void ValidateAbsoluteHttpUri(string value, string propertyPath, List<string> failures)
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
        else if (HasAtSignOutsideUserInfo(value, uri))
        {
            failures.Add(CreateAtSignFailure($"'{propertyPath}'"));
        }
    }

    /// <summary>
    /// Validate vulnerability options
    /// </summary>
    /// <param name="options">Vulnerability options</param>
    /// <param name="failures">Failure list</param>
    private static void ValidateVulnerabilityOptions(VulnerabilityOptions options, List<string> failures)
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

        ValidateRange(options.MaxParallelScans,
                      1,
                      8,
                      $"{DockerUpdateGuardOptions.SectionName}:Vulnerabilities:MaxParallelScans",
                      failures);
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
    private static void ValidateScanningOptions(ScanningOptions options, List<string> failures)
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
        ValidateRange(options.MajorUpgradeMinimumAgeDays,
                      0,
                      3650,
                      $"{DockerUpdateGuardOptions.SectionName}:Scanning:MajorUpgradeMinimumAgeDays",
                      failures);
        ValidateRange(options.MajorUpgradeMinimumReleaseCount,
                      0,
                      100,
                      $"{DockerUpdateGuardOptions.SectionName}:Scanning:MajorUpgradeMinimumReleaseCount",
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
    private static void ValidateRange(int value, int minimum, int maximum, string propertyPath, List<string> failures)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add($"'{propertyPath}' must be between {minimum} and {maximum}");
        }
    }

    /// <summary>
    /// Validate a single Docker instance
    /// </summary>
    /// <param name="instance">Docker instance</param>
    /// <param name="failures">Failure list</param>
    private static void ValidateDockerInstance(DockerInstanceOptions instance, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(instance.BaseUrl))
        {
            failures.Add($"{DescribeInstanceKey(instance.Name, "BaseUrl")} must be configured");
        }
        else if (TryValidateDockerUri(instance.BaseUrl, out var dockerUri) == false)
        {
            failures.Add($"{DescribeInstanceKey(instance.Name, "BaseUrl")} must use http, https, tcp, unix or npipe");
        }
        else if (HasAtSignOutsideUserInfo(instance.BaseUrl, dockerUri))
        {
            failures.Add(CreateAtSignFailure(DescribeInstanceKey(instance.Name, "BaseUrl")));
        }

        if (instance.RequestTimeoutSeconds is <= 0 or > 300)
        {
            failures.Add($"{DescribeInstanceKey(instance.Name, "RequestTimeoutSeconds")} must be between 1 and 300");
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
    private static void ValidatePortainerOptions(string instanceName, PortainerOptions portainer, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(portainer.BaseUrl))
        {
            failures.Add($"{DescribeInstanceKey(instanceName, "Portainer:BaseUrl")} must be configured when Portainer is enabled");
        }
        else if (Uri.TryCreate(portainer.BaseUrl, UriKind.Absolute, out var portainerUri) == false
                 || (portainerUri.Scheme != Uri.UriSchemeHttp
                     && portainerUri.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add($"{DescribeInstanceKey(instanceName, "Portainer:BaseUrl")} must be an absolute http or https URI");
        }
        else if (HasAtSignOutsideUserInfo(portainer.BaseUrl, portainerUri))
        {
            failures.Add(CreateAtSignFailure(DescribeInstanceKey(instanceName, "Portainer:BaseUrl")));
        }
        else if (portainerUri.Scheme == Uri.UriSchemeHttp && portainer.AllowInsecureHttp == false)
        {
            failures.Add($"{DescribeInstanceKey(instanceName, "Portainer:BaseUrl")} uses plaintext HTTP; API keys, passwords and tokens will be transmitted unencrypted — set {DescribeInstanceKey(instanceName, "Portainer:AllowInsecureHttp")} to true to explicitly allow it");
        }

        var hasApiToken = string.IsNullOrWhiteSpace(portainer.ApiToken) == false;
        var hasUsernamePassword = string.IsNullOrWhiteSpace(portainer.Username) == false
                                  && string.IsNullOrWhiteSpace(portainer.Password) == false;

        if (hasApiToken == false && hasUsernamePassword == false)
        {
            failures.Add($"{DescribeInstanceKey(instanceName, "Portainer")} must have either ApiToken or Username+Password configured");
        }

        if (portainer.RequestTimeoutSeconds is <= 0 or > 300)
        {
            failures.Add($"{DescribeInstanceKey(instanceName, "Portainer:RequestTimeoutSeconds")} must be between 1 and 300");
        }
    }

    /// <summary>
    /// Validate a Docker endpoint URI candidate
    /// </summary>
    /// <param name="value">Endpoint value</param>
    /// <param name="uri">Parsed endpoint</param>
    /// <returns>True when valid</returns>
    private static bool TryValidateDockerUri(string value, [NotNullWhen(true)] out Uri? uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out uri) == false)
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttp
               || uri.Scheme == Uri.UriSchemeHttps
               || uri.Scheme == "tcp"
               || uri.Scheme == "unix"
               || uri.Scheme == "npipe";
    }

    /// <summary>
    /// Check whether an already parsed absolute URI value contains an '@' that is not the separator of a non-empty user info
    /// </summary>
    /// <param name="value">Configured value</param>
    /// <param name="uri">Parsed value</param>
    /// <returns>True when the value has an '@' outside its user info</returns>
    private static bool HasAtSignOutsideUserInfo(string value, Uri uri)
    {
        return value.Contains('@')
               && (value.IndexOf('@') != value.LastIndexOf('@')
                   || string.IsNullOrEmpty(uri.UserInfo));
    }

    /// <summary>
    /// Create the failure text for a value with an '@' outside its user info
    /// </summary>
    /// <param name="subject">Quoted subject the failure names</param>
    /// <returns>Failure text</returns>
    private static string CreateAtSignFailure(string subject)
    {
        return $"{subject} has an '@' that does not end its user info; percent-encode '@', '#', '?' and '/' in a user name or password";
    }

    /// <summary>
    /// Describe a key inside a Docker instance relative to that instance
    /// </summary>
    /// <param name="instanceName">Docker instance name as configured</param>
    /// <param name="relativeKey">Key relative to the Docker instance entry</param>
    /// <returns>Quoted key with the instance it belongs to</returns>
    private static string DescribeInstanceKey(string instanceName, string relativeKey)
    {
        return $"'{relativeKey}' of Docker instance '{instanceName}'";
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Validate Docker instances
    /// </summary>
    /// <param name="instances">Configured instances</param>
    /// <param name="failures">Failure list</param>
    private void ValidateDockerInstances(IEnumerable<DockerInstanceOptions> instances, List<string> failures)
    {
        var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var instance in instances)
        {
            if (string.IsNullOrWhiteSpace(instance.Name))
            {
                failures.Add($"'Name' must be configured for every entry of '{DockerUpdateGuardOptions.SectionName}:DockerInstances'");

                continue;
            }

            if (knownNames.Add(instance.Name.Trim()) == false)
            {
                failures.Add($"'{DockerUpdateGuardOptions.SectionName}:DockerInstances' contains duplicate instance name '{instance.Name}'");
            }

            ValidateDockerInstance(instance, failures);
            ValidateCertificatePath(instance, failures);
        }
    }

    /// <summary>
    /// Validate the client certificate path of a Docker instance
    /// </summary>
    /// <param name="instance">Docker instance</param>
    /// <param name="failures">Failure list</param>
    private void ValidateCertificatePath(DockerInstanceOptions instance, List<string> failures)
    {
        if (instance.Enabled == false
            || string.IsNullOrWhiteSpace(instance.CertificatePath))
        {
            return;
        }

        var status = _clientCertificateLoader.TryLoad(instance.CertificatePath, out var certificate);

        certificate?.Dispose();

        if (status is ClientCertificateLoadStatus.Loaded or ClientCertificateLoadStatus.NotSet)
        {
            return;
        }

        failures.Add($"{DescribeInstanceKey(instance.Name, "CertificatePath")} names an unusable client certificate '{instance.CertificatePath}': {ClientCertificateLoader.DescribeFailure(status)}");
    }

    #endregion // Methods

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