using DockerUpdateGuard.Configuration;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for options validation and connection string resolution
/// </summary>
[TestClass]
public class DockerUpdateGuardOptionsValidatorTests
{
    #region Methods

    /// <summary>
    /// Verify inline connection strings take precedence over named entries
    /// </summary>
    [TestMethod]
    public void DockerUpdateGuardConnectionStringResolverInlineConnectionStringWins()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
                                                {
                                                    ["ConnectionStrings:DockerUpdateGuard"] = "Host=fallback;Database=dug",
                                                });
        var options = CreateValidOptions();

        options.ConnectionString = " Host=inline;Database=dug; ";

        var resolvedConnectionString = DockerUpdateGuardConnectionStringResolver.ResolveConnectionString(options, configuration);

        Assert.AreEqual("Host=inline;Database=dug;",
                        resolvedConnectionString,
                        "Inline connection strings must be trimmed and preferred over named connection string entries");
    }

    /// <summary>
    /// Verify valid options pass the custom validator
    /// </summary>
    [TestMethod]
    public void DockerUpdateGuardOptionsValidatorValidOptionsReturnSuccess()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
                                                {
                                                    ["ConnectionStrings:DockerUpdateGuard"] = "Host=database;Database=dug",
                                                });
        var validator = new DockerUpdateGuardOptionsValidator(configuration);

        var validationResult = validator.Validate(Options.DefaultName, CreateValidOptions());

        Assert.IsFalse(validationResult.Failed, "A fully configured option set must pass validation");
    }

    /// <summary>
    /// Verify invalid host options return the relevant failures
    /// </summary>
    [TestMethod]
    public void DockerUpdateGuardOptionsValidatorInvalidConfigurationReturnsRelevantFailures()
    {
        var configuration = CreateConfiguration([]);
        var validator = new DockerUpdateGuardOptionsValidator(configuration);
        var options = CreateValidOptions();

        options.DockerHub.RequestTimeoutSeconds = 0;
        options.Vulnerabilities.Enabled = true;
        options.Scanning.CleanupIntervalMinutes = 0;
        options.Scanning.DockerHubAccountDiscoveryIntervalMinutes = 0;
        options.DockerInstances = [
                                      new DockerInstanceOptions
                                      {
                                          Name = "Production",
                                          BaseUrl = "ftp://docker.example.test",
                                          RequestTimeoutSeconds = 0,
                                          Portainer = new PortainerOptions
                                                      {
                                                          Enabled = true,
                                                          BaseUrl = "not-a-valid-uri",
                                                      },
                                      },
                                      new DockerInstanceOptions
                                      {
                                          Name = "Production",
                                          BaseUrl = string.Empty,
                                      },
                                  ];

        var validationResult = validator.Validate(Options.DefaultName, options);
        var failures = validationResult.Failures?.ToArray() ?? [];

        Assert.IsTrue(validationResult.Failed, "Invalid host options must fail validation");
        Assert.Contains(message => message.Contains("ConnectionString", StringComparison.Ordinal),
                        failures,
                        "Missing connection string configuration must be reported");
        Assert.Contains(message => message.Contains("DockerHub:RequestTimeoutSeconds", StringComparison.Ordinal),
                        failures,
                        "An invalid Docker Hub timeout must be reported");
        Assert.Contains(message => message.Contains("Vulnerabilities:Provider", StringComparison.Ordinal),
                        failures,
                        "An enabled vulnerability refresh without a provider must be reported");
        Assert.Contains(message => message.Contains("Scanning:CleanupIntervalMinutes", StringComparison.Ordinal),
                        failures,
                        "An invalid cleanup interval must be reported");
        Assert.Contains(message => message.Contains("Scanning:DockerHubAccountDiscoveryIntervalMinutes", StringComparison.Ordinal),
                        failures,
                        "An invalid Docker Hub account discovery interval must be reported");
        Assert.Contains(message => message.Contains("duplicate instance name", StringComparison.OrdinalIgnoreCase),
                        failures,
                        "Duplicate Docker instance names must be reported");
        Assert.Contains(message => message.Contains("BaseUrl' must use http, https, tcp or npipe", StringComparison.Ordinal),
                        failures,
                        "Unsupported Docker endpoint schemes must be reported");
        Assert.Contains(message => message.Contains("Portainer:BaseUrl' must be an absolute http or https URI", StringComparison.Ordinal),
                        failures,
                        "Invalid Portainer endpoints must be reported");
    }

    /// <summary>
    /// Verify the development configuration sample exposes the complete host configuration schema
    /// </summary>
    [TestMethod]
    public void DockerUpdateGuardOptionsValidatorDevelopmentConfigurationSampleExposesAllHostOptions()
    {
        var configuration = TestDevelopmentConfiguration.Create();
        var applicationSection = configuration.GetSection(DockerUpdateGuardOptions.SectionName);
        var dockerInstancesSection = applicationSection.GetSection(nameof(DockerUpdateGuardOptions.DockerInstances));

        var namedConnectionString = configuration.GetConnectionString("DockerUpdateGuard");
        var inlineConnectionString = applicationSection[nameof(DockerUpdateGuardOptions.ConnectionString)];

        Assert.IsFalse(string.IsNullOrWhiteSpace(inlineConnectionString), "The development configuration sample must include the inline application connection string");
        Assert.AreEqual("DockerUpdateGuard",
                        applicationSection[nameof(DockerUpdateGuardOptions.ConnectionStringName)],
                        "The development configuration sample must include the configured connection string name");
        Assert.IsTrue(string.IsNullOrWhiteSpace(namedConnectionString) == false || string.IsNullOrWhiteSpace(inlineConnectionString) == false, "The development configuration sample must expose at least one configured database connection path");
        var dockerHubRegistry = applicationSection["DockerHub:Registry"];
        var supportedDockerHubRegistries = new[] { "docker.io", "https://hub.docker.com" };

        Assert.IsFalse(string.IsNullOrWhiteSpace(dockerHubRegistry), "The development configuration sample must include the Docker Hub registry");
        CollectionAssert.Contains(supportedDockerHubRegistries,
                                  dockerHubRegistry,
                                  "The development configuration sample must include a supported Docker Hub registry value");
        Assert.IsFalse(string.IsNullOrWhiteSpace(applicationSection["DockerHub:UserName"]), "The development configuration sample must include the Docker Hub user name value or placeholder");
        Assert.IsFalse(string.IsNullOrWhiteSpace(applicationSection["DockerHub:Pat"]), "The development configuration sample must include the Docker Hub PAT value or placeholder");
        Assert.AreEqual("30",
                        applicationSection["DockerHub:RequestTimeoutSeconds"],
                        "The development configuration sample must include the Docker Hub timeout");
        Assert.AreEqual("4",
                        applicationSection["DockerHub:MaxParallelRequests"],
                        "The development configuration sample must include the Docker Hub parallelism");
        Assert.AreEqual("True",
                        applicationSection["Vulnerabilities:Enabled"],
                        "The development configuration sample must include the vulnerability enable flag");
        Assert.AreEqual("Trivy",
                        applicationSection["Vulnerabilities:Provider"],
                        "The development configuration sample must include the vulnerability provider");
        Assert.AreEqual("30",
                        applicationSection["Vulnerabilities:RequestTimeoutSeconds"],
                        "The development configuration sample must include the vulnerability timeout");
        Assert.AreEqual("5",
                        applicationSection["Scanning:DiscoveryIntervalMinutes"],
                        "The development configuration sample must include the discovery interval");
        Assert.AreEqual("15",
                        applicationSection["Scanning:DockerHubAccountDiscoveryIntervalMinutes"],
                        "The development configuration sample must include the Docker Hub account discovery interval");
        Assert.AreEqual("30",
                        applicationSection["Scanning:OwnImageBaseScanIntervalMinutes"],
                        "The development configuration sample must include the own-image scan interval");
        Assert.AreEqual("10",
                        applicationSection["Scanning:RuntimeImageUpdateScanIntervalMinutes"],
                        "The development configuration sample must include the runtime scan interval");
        Assert.AreEqual("60",
                        applicationSection["Scanning:VulnerabilityRefreshIntervalMinutes"],
                        "The development configuration sample must include the vulnerability refresh interval");
        Assert.AreEqual("720",
                        applicationSection["Scanning:CleanupIntervalMinutes"],
                        "The development configuration sample must include the cleanup interval");
        Assert.AreEqual("1",
                        applicationSection["Scanning:RetryCount"],
                        "The development configuration sample must include the retry count");
        Assert.AreEqual("14",
                        applicationSection["Scanning:RetainScanRunsDays"],
                        "The development configuration sample must include the scan retention period");
        Assert.IsTrue(dockerInstancesSection.GetChildren()
                                            .Any(),
                      "The development configuration sample must include at least one Docker instance entry");
        Assert.AreEqual("Docker Desktop",
                        applicationSection["DockerInstances:0:Name"],
                        "The first Docker instance sample must expose the name");
        Assert.AreEqual("npipe://./pipe/docker_engine",
                        applicationSection["DockerInstances:0:BaseUrl"],
                        "The first Docker instance sample must expose the base URL");
        Assert.AreEqual("True",
                        applicationSection["DockerInstances:0:Enabled"],
                        "The first Docker instance sample must expose the enabled flag");
        Assert.AreEqual("False",
                        applicationSection["DockerInstances:0:UseTls"],
                        "The first Docker instance sample must expose the TLS flag");
        Assert.AreEqual("False",
                        applicationSection["DockerInstances:0:SkipCertificateValidation"],
                        "The first Docker instance sample must expose the certificate validation flag");
        Assert.AreEqual("15",
                        applicationSection["DockerInstances:0:RequestTimeoutSeconds"],
                        "The first Docker instance sample must expose the request timeout");
        Assert.AreEqual("False",
                        applicationSection["DockerInstances:0:Portainer:Enabled"],
                        "The first Docker instance sample must expose the Portainer enabled flag");
        Assert.AreEqual("https://portainer.local",
                        applicationSection["DockerInstances:0:Portainer:BaseUrl"],
                        "The first Docker instance sample must expose the Portainer base URL");
        Assert.AreEqual("admin",
                        applicationSection["DockerInstances:0:Portainer:Username"],
                        "The first Docker instance sample must expose the Portainer username");
        Assert.AreEqual("change-me",
                        applicationSection["DockerInstances:0:Portainer:Password"],
                        "The first Docker instance sample must expose the Portainer password placeholder");
        Assert.AreEqual("1",
                        applicationSection["DockerInstances:0:Portainer:EndpointId"],
                        "The first Docker instance sample must expose the Portainer endpoint identifier");
        Assert.AreEqual("15",
                        applicationSection["DockerInstances:0:Portainer:RequestTimeoutSeconds"],
                        "The first Docker instance sample must expose the Portainer timeout");
    }

    /// <summary>
    /// Create a valid options object for tests
    /// </summary>
    /// <returns>Configured options</returns>
    private static DockerUpdateGuardOptions CreateValidOptions()
    {
        var scanningOptions = new ScanningOptions();

        scanningOptions.DiscoveryIntervalMinutes = 15;
        scanningOptions.DockerHubAccountDiscoveryIntervalMinutes = 60;
        scanningOptions.OwnImageBaseScanIntervalMinutes = 60;
        scanningOptions.RuntimeImageUpdateScanIntervalMinutes = 30;
        scanningOptions.VulnerabilityRefreshIntervalMinutes = 180;
        scanningOptions.CleanupIntervalMinutes = 720;
        scanningOptions.RetryCount = 2;
        scanningOptions.RetainScanRunsDays = 30;
        var dockerHubOptions = new DockerHubOptions
                               {
                                   Registry = "docker.io",
                                   RequestTimeoutSeconds = 30,
                                   MaxParallelRequests = 4,
                               };
        var vulnerabilityOptions = new VulnerabilityOptions
                                   {
                                       Enabled = false,
                                       Provider = VulnerabilityProviderKind.None,
                                       RequestTimeoutSeconds = 30,
                                   };
        var options = new DockerUpdateGuardOptions
                      {
                          ConnectionStringName = "DockerUpdateGuard",
                          DockerHub = dockerHubOptions,
                          Vulnerabilities = vulnerabilityOptions,
                          Scanning = scanningOptions,
                          DockerInstances = [],
                      };

        return options;
    }

    /// <summary>
    /// Create an in-memory configuration root
    /// </summary>
    /// <param name="values">Configuration entries</param>
    /// <returns>Configuration root</returns>
    private static IConfiguration CreateConfiguration(IEnumerable<KeyValuePair<string, string?>> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values)
                                         .Build();
    }

    #endregion // Methods
}