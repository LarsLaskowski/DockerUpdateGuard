using System.Reflection;

using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Vulnerabilities;
using DockerUpdateGuard.Vulnerabilities.Interfaces;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for startup-adjacent service registration behavior
/// </summary>
[TestClass]
public class ServiceCollectionExtensionsTests
{
    #region Methods

    /// <summary>
    /// Verify host registration wires core services, hosted services and Docker Hub client settings
    /// </summary>
    [TestMethod]
    public void ServiceCollectionExtensionsAddDockerUpdateGuardHostRegistersExpectedServices()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                                                                             {
                                                                                 ["DockerUpdateGuard:ConnectionString"] = "Host=database;Database=dug;Username=test;Password=test",
                                                                                 ["DockerUpdateGuard:DockerHub:Registry"] = "https://registry.example.test/",
                                                                                 ["DockerUpdateGuard:DockerHub:RequestTimeoutSeconds"] = "42",
                                                                                 ["Telemetry:EnableLogging"] = "true",
                                                                                 ["Telemetry:EnableMetrics"] = "false",
                                                                                 ["Telemetry:EnableTracing"] = "false",
                                                                             })
                                                      .Build();
        var services = new ServiceCollection();

        services.AddDockerUpdateGuardHost(configuration);

        using var serviceProvider = services.BuildServiceProvider();

        var hostedServiceTypes = serviceProvider.GetServices<IHostedService>()
                                                .Select(service => service.GetType())
                                                .ToArray();
        var dockerHubClient = serviceProvider.GetRequiredService<IDockerHubClient>();
        var baseImageResolver = serviceProvider.GetRequiredService<IBaseImageResolver>();
        var httpClientField = typeof(DockerHubClient).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic);
        var httpClient = httpClientField?.GetValue(dockerHubClient) as HttpClient;
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var loggerProviders = serviceProvider.GetServices<ILoggerProvider>().ToArray();

        Assert.IsNotNull(serviceProvider.GetService<TransientHttpRetryHandler>(), "The transient HTTP retry handler must be registered for outbound HTTP clients");
        Assert.IsNotNull(serviceProvider.GetService<IImageScanOrchestrator>(), "The observed image scan orchestrator must be registered");
        Assert.IsNotNull(serviceProvider.GetService<IRuntimeContainerScanOrchestrator>(), "The runtime container scan orchestrator must be registered");
        Assert.IsNotNull(serviceProvider.GetService<IVulnerabilityEnrichmentService>(), "The vulnerability enrichment service must be registered");
        Assert.IsInstanceOfType<RegistryBaseImageResolver>(baseImageResolver, "Base-image resolution must use the registry-aware resolver");
        Assert.IsTrue(hostedServiceTypes.Contains(typeof(DockerInstanceDiscoveryBackgroundService)), "The Docker instance discovery background service must be registered");
        Assert.IsTrue(hostedServiceTypes.Contains(typeof(DockerHubAccountImageDiscoveryBackgroundService)), "The Docker Hub account discovery background service must be registered");
        Assert.IsTrue(hostedServiceTypes.Contains(typeof(OwnImageBaseRefreshBackgroundService)), "The observed image refresh background service must be registered");
        Assert.IsTrue(hostedServiceTypes.Contains(typeof(RuntimeContainerRefreshBackgroundService)), "The runtime refresh background service must be registered");
        Assert.IsTrue(hostedServiceTypes.Contains(typeof(VulnerabilityRefreshBackgroundService)), "The vulnerability refresh background service must be registered");
        Assert.IsTrue(hostedServiceTypes.Contains(typeof(ScanCleanupBackgroundService)), "The cleanup background service must be registered");
        Assert.IsNotNull(httpClient, "The typed Docker Hub client must keep the configured HttpClient instance");
        Assert.AreEqual(new Uri("https://registry.example.test/"),
                        httpClient.BaseAddress,
                        "The Docker Hub client must use the configured registry URI as base address");
        Assert.AreEqual(TimeSpan.FromSeconds(42),
                        httpClient.Timeout,
                        "The Docker Hub client must use the configured request timeout");
        Assert.IsNotNull(loggerFactory, "The host registration must register an ILoggerFactory instance through telemetry");
        Assert.Contains(provider => provider.GetType().FullName?.Contains("OpenTelemetry", StringComparison.Ordinal) == true, loggerProviders, "The host registration must register an OpenTelemetry logger provider when telemetry logging is enabled");
        Assert.IsNotNull(serviceProvider.GetService<IDockerHubAccountImageDiscoveryService>(), "The Docker Hub account discovery service must be registered");
        Assert.IsNotNull(serviceProvider.GetService<IVulnerabilityProviderResolver>(), "The vulnerability provider resolver must be registered");
        Assert.IsNotNull(serviceProvider.GetService<DefaultVulnerabilityProvider>(), "The default vulnerability provider must be registered unconditionally");
        Assert.IsNotNull(serviceProvider.GetService<DockerScoutVulnerabilityProvider>(), "The Docker Scout vulnerability provider must be registered unconditionally");
        Assert.IsNotNull(serviceProvider.GetService<TrivyVulnerabilityProvider>(), "The Trivy vulnerability provider must be registered unconditionally");
        Assert.IsNull(serviceProvider.GetService<IVulnerabilityProvider>(), "No conditional IVulnerabilityProvider registration must remain; consumers must depend on the resolver");
    }

    #endregion // Methods
}