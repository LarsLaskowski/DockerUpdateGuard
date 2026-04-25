using Microsoft.Extensions.Configuration;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Provides deterministic development configuration samples for tests
/// </summary>
internal static class TestDevelopmentConfiguration
{
    #region Methods

    /// <summary>
    /// Create a configuration root that represents the complete development host schema
    /// </summary>
    /// <returns>Configuration root</returns>
    internal static IConfiguration Create()
    {
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                                                                {
                                                                    ["ConnectionStrings:DockerUpdateGuard"] = "Host=database;Port=5432;Database=dockerupdateguard;Username=dockerupdateguard;Password=change-me",
                                                                    ["DockerUpdateGuard:ConnectionString"] = "Host=database;Port=5432;Database=dockerupdateguard;Username=dockerupdateguard;Password=change-me",
                                                                    ["DockerUpdateGuard:ConnectionStringName"] = "DockerUpdateGuard",
                                                                    ["DockerUpdateGuard:DockerHub:Registry"] = "https://hub.docker.com",
                                                                    ["DockerUpdateGuard:DockerHub:UserName"] = "dockerupdateguard",
                                                                    ["DockerUpdateGuard:DockerHub:Pat"] = "change-me",
                                                                    ["DockerUpdateGuard:DockerHub:RequestTimeoutSeconds"] = "30",
                                                                    ["DockerUpdateGuard:DockerHub:MaxParallelRequests"] = "4",
                                                                    ["DockerUpdateGuard:Vulnerabilities:Enabled"] = "True",
                                                                    ["DockerUpdateGuard:Vulnerabilities:Provider"] = "Trivy",
                                                                    ["DockerUpdateGuard:Vulnerabilities:RequestTimeoutSeconds"] = "30",
                                                                    ["DockerUpdateGuard:Scanning:DiscoveryIntervalMinutes"] = "5",
                                                                    ["DockerUpdateGuard:Scanning:DockerHubAccountDiscoveryIntervalMinutes"] = "15",
                                                                    ["DockerUpdateGuard:Scanning:OwnImageBaseScanIntervalMinutes"] = "30",
                                                                    ["DockerUpdateGuard:Scanning:DockerHubRequestLimitWindowHours"] = "6",
                                                                    ["DockerUpdateGuard:Scanning:DockerHubRequestLimitPerWindow"] = "200",
                                                                    ["DockerUpdateGuard:Scanning:DockerHubReservedManualRequestsPerWindow"] = "40",
                                                                    ["DockerUpdateGuard:Scanning:RuntimeImageUpdateScanIntervalMinutes"] = "10",
                                                                    ["DockerUpdateGuard:Scanning:ResourceStatisticsIntervalMinutes"] = "5",
                                                                    ["DockerUpdateGuard:Scanning:VulnerabilityRefreshIntervalMinutes"] = "60",
                                                                    ["DockerUpdateGuard:Scanning:CleanupIntervalMinutes"] = "720",
                                                                    ["DockerUpdateGuard:Scanning:RetryCount"] = "1",
                                                                    ["DockerUpdateGuard:Scanning:RetainScanRunsDays"] = "14",
                                                                    ["DockerUpdateGuard:DockerInstances:0:Name"] = "Docker Desktop",
                                                                    ["DockerUpdateGuard:DockerInstances:0:BaseUrl"] = "npipe://./pipe/docker_engine",
                                                                    ["DockerUpdateGuard:DockerInstances:0:Enabled"] = "True",
                                                                    ["DockerUpdateGuard:DockerInstances:0:UseTls"] = "False",
                                                                    ["DockerUpdateGuard:DockerInstances:0:SkipCertificateValidation"] = "False",
                                                                    ["DockerUpdateGuard:DockerInstances:0:CertificatePath"] = null,
                                                                    ["DockerUpdateGuard:DockerInstances:0:RequestTimeoutSeconds"] = "15",
                                                                    ["DockerUpdateGuard:DockerInstances:0:Portainer:Enabled"] = "False",
                                                                    ["DockerUpdateGuard:DockerInstances:0:Portainer:BaseUrl"] = "https://portainer.local",
                                                                    ["DockerUpdateGuard:DockerInstances:0:Portainer:Username"] = "admin",
                                                                    ["DockerUpdateGuard:DockerInstances:0:Portainer:Password"] = "change-me",
                                                                    ["DockerUpdateGuard:DockerInstances:0:Portainer:EndpointId"] = "1",
                                                                    ["DockerUpdateGuard:DockerInstances:0:Portainer:RequestTimeoutSeconds"] = "15",
                                                                    ["Telemetry:ServiceName"] = "DockerUpdateGuard",
                                                                    ["Telemetry:Instance"] = "Development",
                                                                    ["Telemetry:OtlpEndpoint"] = "http://collector:4317",
                                                                    ["Telemetry:EnableLogging"] = "True",
                                                                    ["Telemetry:EnableMetrics"] = "True",
                                                                    ["Telemetry:EnableTracing"] = "True",
                                                                })
                                         .Build();
    }

    #endregion // Methods
}