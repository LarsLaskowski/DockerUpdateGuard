using System.Reflection;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Portainer;
using DockerUpdateGuard.Portainer.Interfaces;
using DockerUpdateGuard.Vulnerabilities;
using DockerUpdateGuard.Vulnerabilities.Interfaces;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NSubstitute;

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

    /// <summary>
    /// Verify the login Retry-After registry is one singleton shared by the Docker Hub clients, the Portainer client and the Docker Scout provider
    /// </summary>
    [TestMethod]
    [TestCategory("AC7")]
    public void ServiceCollectionExtensionsLoginRetryAfterRegistryIsSharedSingleton()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                                                                             {
                                                                                 ["DockerUpdateGuard:ConnectionString"] = "Host=database;Database=dug;Username=test;Password=test",
                                                                                 ["Telemetry:EnableLogging"] = "true",
                                                                                 ["Telemetry:EnableMetrics"] = "false",
                                                                                 ["Telemetry:EnableTracing"] = "false",
                                                                             })
                                                      .Build();
        var services = new ServiceCollection();

        services.AddDockerUpdateGuardHost(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        using var firstScope = serviceProvider.CreateScope();
        using var secondScope = serviceProvider.CreateScope();

        var registry = serviceProvider.GetRequiredService<LoginRetryAfterRegistry>();
        var firstClient = firstScope.ServiceProvider.GetRequiredService<IDockerHubClient>();
        var secondClient = secondScope.ServiceProvider.GetRequiredService<IDockerHubClient>();
        var portainerClient = serviceProvider.GetRequiredService<IPortainerClient>();
        var scoutProvider = serviceProvider.GetRequiredService<DockerScoutVulnerabilityProvider>();

        Assert.AreNotSame(firstClient, secondClient, "The Docker Hub clients of two scopes must be separate instances");
        Assert.AreSame(registry, GetRegistry(firstClient), "The Docker Hub client of the first scope must use the registered registry");
        Assert.AreSame(registry, GetRegistry(secondClient), "The Docker Hub client of the second scope must use the registered registry");
        Assert.AreSame(registry, GetRegistry(portainerClient), "The Portainer client must use the registered registry");
        Assert.AreSame(registry, GetRegistry(scoutProvider), "The Docker Scout provider must use the registered registry");
    }

    /// <summary>
    /// Verify the Docker Hub access token cache is one singleton shared by the Docker Hub clients of all scopes
    /// </summary>
    [TestMethod]
    [TestCategory("AC6")]
    public void ServiceCollectionExtensionsDockerHubAccessTokenCacheIsSharedSingleton()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                                                                             {
                                                                                 ["DockerUpdateGuard:ConnectionString"] = "Host=database;Database=dug;Username=test;Password=test",
                                                                                 ["Telemetry:EnableLogging"] = "true",
                                                                                 ["Telemetry:EnableMetrics"] = "false",
                                                                                 ["Telemetry:EnableTracing"] = "false",
                                                                             })
                                                      .Build();
        var services = new ServiceCollection();

        services.AddDockerUpdateGuardHost(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        using var firstScope = serviceProvider.CreateScope();
        using var secondScope = serviceProvider.CreateScope();

        var accessTokenCache = serviceProvider.GetRequiredService<DockerHubAccessTokenCache>();
        var firstScopeCache = firstScope.ServiceProvider.GetRequiredService<DockerHubAccessTokenCache>();
        var firstClient = firstScope.ServiceProvider.GetRequiredService<IDockerHubClient>();
        var secondClient = secondScope.ServiceProvider.GetRequiredService<IDockerHubClient>();

        Assert.AreSame(accessTokenCache, firstScopeCache, "The access token cache resolved in a scope must be the root singleton");
        Assert.AreNotSame(firstClient, secondClient, "The Docker Hub clients of two scopes must be separate instances");
        Assert.AreSame(accessTokenCache, GetAccessTokenCache(firstClient), "The Docker Hub client of the first scope must use the registered access token cache");
        Assert.AreSame(accessTokenCache, GetAccessTokenCache(secondClient), "The Docker Hub client of the second scope must use the registered access token cache");
    }

    /// <summary>
    /// Verify the Docker Hub request limiter is one singleton shared by the Docker Hub clients of all scopes and the client holds no semaphore of its own
    /// </summary>
    [TestMethod]
    [TestCategory("AC7")]
    public void ServiceCollectionExtensionsDockerHubRequestLimiterIsSharedSingleton()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                                                                             {
                                                                                 ["DockerUpdateGuard:ConnectionString"] = "Host=database;Database=dug;Username=test;Password=test",
                                                                                 ["Telemetry:EnableLogging"] = "true",
                                                                                 ["Telemetry:EnableMetrics"] = "false",
                                                                                 ["Telemetry:EnableTracing"] = "false",
                                                                             })
                                                      .Build();
        var services = new ServiceCollection();

        services.AddDockerUpdateGuardHost(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        using var firstScope = serviceProvider.CreateScope();
        using var secondScope = serviceProvider.CreateScope();

        var requestLimiter = serviceProvider.GetRequiredService<DockerHubRequestLimiter>();
        var firstScopeLimiter = firstScope.ServiceProvider.GetRequiredService<DockerHubRequestLimiter>();
        var secondScopeLimiter = secondScope.ServiceProvider.GetRequiredService<DockerHubRequestLimiter>();
        var firstClient = firstScope.ServiceProvider.GetRequiredService<IDockerHubClient>();
        var secondClient = secondScope.ServiceProvider.GetRequiredService<IDockerHubClient>();
        var clientFields = typeof(DockerHubClient).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.AreSame(requestLimiter, firstScopeLimiter, "The limiter resolved in the first scope must be the root singleton");
        Assert.AreSame(requestLimiter, secondScopeLimiter, "The limiter resolved in the second scope must be the root singleton");
        Assert.AreSame(requestLimiter, GetRequestLimiter(firstClient), "The Docker Hub client of the first scope must use the registered limiter");
        Assert.AreSame(requestLimiter, GetRequestLimiter(secondClient), "The Docker Hub client of the second scope must use the registered limiter");
        Assert.DoesNotContain(field => field.FieldType == typeof(SemaphoreSlim), clientFields, "The Docker Hub client must hold no semaphore of its own");
        Assert.IsFalse(typeof(IDisposable).IsAssignableFrom(typeof(DockerHubClient)), "The Docker Hub client owns nothing to dispose");
    }

    /// <summary>
    /// Verify the Docker Scout provider uses the registered Docker Hub request limiter singleton
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void ServiceCollectionExtensionsDockerScoutProviderSharesDockerHubRequestLimiter()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                                                                             {
                                                                                 ["DockerUpdateGuard:ConnectionString"] = "Host=database;Database=dug;Username=test;Password=test",
                                                                                 ["Telemetry:EnableLogging"] = "true",
                                                                                 ["Telemetry:EnableMetrics"] = "false",
                                                                                 ["Telemetry:EnableTracing"] = "false",
                                                                             })
                                                      .Build();
        var services = new ServiceCollection();

        services.AddDockerUpdateGuardHost(configuration);

        using var serviceProvider = services.BuildServiceProvider();

        var requestLimiter = serviceProvider.GetRequiredService<DockerHubRequestLimiter>();
        var scoutProvider = serviceProvider.GetRequiredService<DockerScoutVulnerabilityProvider>();

        Assert.IsNotNull(GetRequestLimiter(scoutProvider), "The Docker Scout provider must hold a request limiter");
        Assert.AreSame(requestLimiter, GetRequestLimiter(scoutProvider), "The Docker Scout provider must use the registered limiter");
    }

    /// <summary>
    /// Verify the named Docker Scout client has the transient retry handler in its handler chain
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void ServiceCollectionExtensionsDockerScoutClientUsesRetryHandler()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                                                                             {
                                                                                 ["DockerUpdateGuard:ConnectionString"] = "Host=database;Database=dug;Username=test;Password=test",
                                                                                 ["Telemetry:EnableLogging"] = "true",
                                                                                 ["Telemetry:EnableMetrics"] = "false",
                                                                                 ["Telemetry:EnableTracing"] = "false",
                                                                             })
                                                      .Build();
        var services = new ServiceCollection();

        services.AddDockerUpdateGuardHost(configuration);

        using var serviceProvider = services.BuildServiceProvider();

        var clientOptions = serviceProvider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
                                           .Get(DockerScoutVulnerabilityProvider.HttpClientName);
        var builder = serviceProvider.GetRequiredService<HttpMessageHandlerBuilder>();

        foreach (var action in clientOptions.HttpMessageHandlerBuilderActions)
        {
            action(builder);
        }

        Assert.Contains(handler => handler is TransientHttpRetryHandler,
                        builder.AdditionalHandlers,
                        "The Docker Scout client must send through the transient HTTP retry handler");
    }

    /// <summary>
    /// Verify the Docker Hub and OCI clients send through the request deadline handler with the configured timeout, outside the retry handler
    /// </summary>
    /// <param name="clientName">Name of the typed client</param>
    [TestMethod]
    [TestCategory("AC6")]
    [DataRow(nameof(DockerHubClient))]
    [DataRow(nameof(OciRegistryClient))]
    public void ServiceCollectionExtensionsRegistryClientsSendThroughRequestDeadlineHandler(string clientName)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                                                                             {
                                                                                 ["DockerUpdateGuard:ConnectionString"] = "Host=database;Database=dug;Username=test;Password=test",
                                                                                 ["DockerUpdateGuard:DockerHub:RequestTimeoutSeconds"] = "42",
                                                                                 ["Telemetry:EnableLogging"] = "true",
                                                                                 ["Telemetry:EnableMetrics"] = "false",
                                                                                 ["Telemetry:EnableTracing"] = "false",
                                                                             })
                                                      .Build();
        var services = new ServiceCollection();

        services.AddDockerUpdateGuardHost(configuration);

        using var serviceProvider = services.BuildServiceProvider();

        var clientOptions = serviceProvider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
                                           .Get(clientName);
        var builder = serviceProvider.GetRequiredService<HttpMessageHandlerBuilder>();

        foreach (var action in clientOptions.HttpMessageHandlerBuilderActions)
        {
            action(builder);
        }

        var deadlineIndex = builder.AdditionalHandlers.ToList().FindIndex(handler => handler is RequestDeadlineHandler);
        var retryIndex = builder.AdditionalHandlers.ToList().FindIndex(handler => handler is TransientHttpRetryHandler);

        Assert.IsGreaterThanOrEqualTo(0, deadlineIndex, $"The {clientName} client must send through the request deadline handler");
        Assert.IsGreaterThanOrEqualTo(0, retryIndex, $"The {clientName} client must keep the transient HTTP retry handler");
        Assert.IsLessThan(retryIndex, deadlineIndex, "The request deadline handler must sit before (outside) the retry handler");
        Assert.AreEqual(TimeSpan.FromSeconds(42),
                        ((RequestDeadlineHandler)builder.AdditionalHandlers[deadlineIndex]).RequestTimeout,
                        "The deadline must be the configured DockerHub request timeout");
    }

    /// <summary>
    /// Verify the container action service is one singleton shared by all scopes, so its in-progress guard spans every circuit
    /// </summary>
    [TestMethod]
    [TestCategory("AC7")]
    public void ServiceCollectionExtensionsAddDockerUpdateGuardHostSharesContainerActionServiceAcrossScopes()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                                                                             {
                                                                                 ["DockerUpdateGuard:ConnectionString"] = "Host=database;Database=dug;Username=test;Password=test",
                                                                                 ["Telemetry:EnableLogging"] = "true",
                                                                                 ["Telemetry:EnableMetrics"] = "false",
                                                                                 ["Telemetry:EnableTracing"] = "false",
                                                                             })
                                                      .Build();
        var services = new ServiceCollection();

        services.AddDockerUpdateGuardHost(configuration);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(Substitute.For<IHostApplicationLifetime>());

        using var serviceProvider = services.BuildServiceProvider();
        using var firstScope = serviceProvider.CreateScope();
        using var secondScope = serviceProvider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<IContainerActionService>();
        var second = secondScope.ServiceProvider.GetRequiredService<IContainerActionService>();

        Assert.IsInstanceOfType<ContainerActionService>(first, "The container action service must be the Portainer-backed implementation");
        Assert.AreSame(first, second, "Two scopes (two circuits) must share one container action service");
    }

    /// <summary>
    /// Verify the resolved options carry the Docker instance names trimmed of surrounding whitespace
    /// </summary>
    [TestMethod]
    [TestCategory("AC4")]
    public void ServiceCollectionExtensionsAddDockerUpdateGuardHostTrimsDockerInstanceNames()
    {
        var configuration = CreateInstanceConfiguration(" Production ", "\tStaging Host ");
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddDockerUpdateGuardHost(configuration);

        using var serviceProvider = services.BuildServiceProvider();

        var names = serviceProvider.GetRequiredService<IOptionsMonitor<DockerUpdateGuardOptions>>()
                                   .CurrentValue
                                   .DockerInstances
                                   .Select(instance => instance.Name)
                                   .ToArray();

        Assert.AreSequenceEqual(["Production", "Staging Host"], names, "The resolved options must carry trimmed names with inner spaces kept");
    }

    /// <summary>
    /// Verify the resolved options carry trimmed names after a reload of the configuration
    /// </summary>
    [TestMethod]
    [TestCategory("AC4")]
    public void ServiceCollectionExtensionsAddDockerUpdateGuardHostTrimsDockerInstanceNamesAfterReload()
    {
        var configuration = CreateInstanceConfiguration(" Production ");
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddDockerUpdateGuardHost(configuration);

        using var serviceProvider = services.BuildServiceProvider();

        var monitor = serviceProvider.GetRequiredService<IOptionsMonitor<DockerUpdateGuardOptions>>();

        _ = monitor.CurrentValue;
        configuration["DockerUpdateGuard:DockerInstances:0:Name"] = " Staging ";
        configuration.Reload();

        Assert.AreEqual("Staging", monitor.CurrentValue.DockerInstances[0].Name, "A reload of the configuration must produce trimmed names again");
    }

    /// <summary>
    /// Verify a whitespace-only name still fails validation with the message for a missing name
    /// </summary>
    [TestMethod]
    [TestCategory("AC5")]
    public void ServiceCollectionExtensionsAddDockerUpdateGuardHostRejectsWhitespaceOnlyInstanceName()
    {
        var configuration = CreateInstanceConfiguration("   ");
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddDockerUpdateGuardHost(configuration);

        using var serviceProvider = services.BuildServiceProvider();

        var monitor = serviceProvider.GetRequiredService<IOptionsMonitor<DockerUpdateGuardOptions>>();
        var exception = Assert.Throws<OptionsValidationException>(() => _ = monitor.CurrentValue, "A whitespace-only name must fail validation");

        Assert.Contains("'Name' must be configured for every entry of 'DockerUpdateGuard:DockerInstances'",
                        exception.Failures.ToArray(),
                        "The failure must name the missing Name");
    }

    /// <summary>
    /// Verify names differing only in surrounding whitespace are reported as duplicates of the trimmed name
    /// </summary>
    [TestMethod]
    [TestCategory("AC5")]
    public void ServiceCollectionExtensionsAddDockerUpdateGuardHostRejectsInstanceNamesDifferingOnlyInWhitespace()
    {
        var configuration = CreateInstanceConfiguration("Production", "Production ");
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddDockerUpdateGuardHost(configuration);

        using var serviceProvider = services.BuildServiceProvider();

        var monitor = serviceProvider.GetRequiredService<IOptionsMonitor<DockerUpdateGuardOptions>>();
        var exception = Assert.Throws<OptionsValidationException>(() => _ = monitor.CurrentValue, "Names differing only in whitespace must fail validation");

        Assert.Contains("'DockerUpdateGuard:DockerInstances' contains duplicate instance name 'Production'",
                        exception.Failures.ToArray(),
                        "The failure must name the trimmed duplicate");
    }

    /// <summary>
    /// Create a configuration with enabled Docker instances of the given names
    /// </summary>
    /// <param name="names">Configured names</param>
    /// <returns>Configuration root</returns>
    private static ConfigurationRoot CreateInstanceConfiguration(params string[] names)
    {
        var values = new Dictionary<string, string?>
                     {
                         ["DockerUpdateGuard:ConnectionString"] = "Host=database;Database=dug;Username=test;Password=test",
                         ["Telemetry:EnableLogging"] = "false",
                         ["Telemetry:EnableMetrics"] = "false",
                         ["Telemetry:EnableTracing"] = "false",
                     };

        for (var index = 0; index < names.Length; index++)
        {
            values[$"DockerUpdateGuard:DockerInstances:{index}:Name"] = names[index];
            values[$"DockerUpdateGuard:DockerInstances:{index}:BaseUrl"] = "https://docker.example.test";
            values[$"DockerUpdateGuard:DockerInstances:{index}:Enabled"] = "true";
        }

        return (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>
    /// Read the Docker Hub request limiter a service holds, found by field type
    /// </summary>
    /// <param name="service">Service instance</param>
    /// <returns>Request limiter or null when the service holds none</returns>
    private static DockerHubRequestLimiter? GetRequestLimiter(object service)
    {
        var field = service.GetType()
                           .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                           .FirstOrDefault(candidate => candidate.FieldType == typeof(DockerHubRequestLimiter));

        return field?.GetValue(service) as DockerHubRequestLimiter;
    }

    /// <summary>
    /// Read the Docker Hub access token cache a service holds, found by field type
    /// </summary>
    /// <param name="service">Service instance</param>
    /// <returns>Access token cache or null when the service holds none</returns>
    private static DockerHubAccessTokenCache? GetAccessTokenCache(object service)
    {
        var field = service.GetType()
                           .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                           .FirstOrDefault(candidate => candidate.FieldType == typeof(DockerHubAccessTokenCache));

        return field?.GetValue(service) as DockerHubAccessTokenCache;
    }

    /// <summary>
    /// Read the login Retry-After registry a service holds, found by field type
    /// </summary>
    /// <param name="service">Service instance</param>
    /// <returns>Registry or null when the service holds none</returns>
    private static LoginRetryAfterRegistry? GetRegistry(object service)
    {
        var field = service.GetType()
                           .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                           .FirstOrDefault(candidate => candidate.FieldType == typeof(LoginRetryAfterRegistry));

        return field?.GetValue(service) as LoginRetryAfterRegistry;
    }

    #endregion // Methods
}