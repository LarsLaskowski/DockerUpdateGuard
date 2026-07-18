using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Docker;
using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Portainer;
using DockerUpdateGuard.Telemetry;
using DockerUpdateGuard.UI;
using DockerUpdateGuard.Vulnerabilities;
using DockerUpdateGuard.Vulnerabilities.Interfaces;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DockerUpdateGuard;

/// <summary>
/// Host project service registration helpers
/// </summary>
public static class ServiceCollectionExtensions
{
    #region Const fields

    /// <summary>
    /// User agent sent with all outbound registry and metadata requests
    /// </summary>
    private const string OutboundUserAgent = "DockerUpdateGuard/1.0";

    #endregion // Const fields

    #region Methods

    /// <summary>
    /// Register the first host iteration services
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    /// <returns>Returns the service collection</returns>
    public static IServiceCollection AddDockerUpdateGuardHost(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var optionsSection = configuration.GetSection(DockerUpdateGuardOptions.SectionName);
        var applicationOptions = new DockerUpdateGuardOptions();

        optionsSection.Bind(applicationOptions);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DockerUpdateGuardOptions>, DockerUpdateGuardOptionsValidator>());
        services.AddOptions<DockerUpdateGuardOptions>()
                .Bind(optionsSection)
                .ValidateOnStart();

        var connectionString = DockerUpdateGuardConnectionStringResolver.ResolveConnectionString(applicationOptions, configuration);

        services.AddDockerUpdateGuardData(options =>
                                          {
                                              options.UseNpgsql(connectionString,
                                                                npgsqlOptions =>
                                                                {
                                                                    npgsqlOptions.MigrationsAssembly(typeof(DockerUpdateGuard.Data.DockerUpdateGuardDbContext).Assembly.GetName().Name);
                                                                    npgsqlOptions.EnableRetryOnFailure(applicationOptions.Database.MaxConnectionRetryCount,
                                                                                                       TimeSpan.FromSeconds(applicationOptions.Database.MaxConnectionRetryDelaySeconds),
                                                                                                       errorCodesToAdd: null);
                                                                });
                                          });
        services.AddDockerUpdateGuardTelemetry(configuration);
        services.AddTransient<TransientHttpRetryHandler>();
        services.AddHttpClient<DockerHubClient>(client =>
                                                {
                                                    client.BaseAddress = DockerHubClient.GetBaseUri(applicationOptions.DockerHub);
                                                    client.Timeout = TimeSpan.FromSeconds(applicationOptions.DockerHub.RequestTimeoutSeconds);
                                                    client.DefaultRequestHeaders.UserAgent.ParseAdd(OutboundUserAgent);
                                                })
                .AddHttpMessageHandler<TransientHttpRetryHandler>();
        services.AddHttpClient<OciRegistryClient>(client =>
                                                  {
                                                      client.Timeout = TimeSpan.FromSeconds(applicationOptions.DockerHub.RequestTimeoutSeconds);
                                                      client.DefaultRequestHeaders.UserAgent.ParseAdd(OutboundUserAgent);
                                                  })
                .AddHttpMessageHandler<TransientHttpRetryHandler>();
        services.AddHttpClient<DotNetReleaseMetadataService>(client =>
                                                             {
                                                                 client.BaseAddress = new Uri(applicationOptions.ReleaseMetadata.DotNetBaseUrl);
                                                                 client.Timeout = TimeSpan.FromSeconds(applicationOptions.DockerHub.RequestTimeoutSeconds);
                                                                 client.DefaultRequestHeaders.UserAgent.ParseAdd(OutboundUserAgent);
                                                             })
                .AddHttpMessageHandler<TransientHttpRetryHandler>();
        services.AddHttpClient<NginxReleaseMetadataService>(client =>
                                                            {
                                                                client.BaseAddress = new Uri(applicationOptions.ReleaseMetadata.NginxBaseUrl);
                                                                client.Timeout = TimeSpan.FromSeconds(applicationOptions.DockerHub.RequestTimeoutSeconds);
                                                                client.DefaultRequestHeaders.UserAgent.ParseAdd(OutboundUserAgent);
                                                            })
                .AddHttpMessageHandler<TransientHttpRetryHandler>();
        services.AddHttpClient(PortainerClient.HttpClientName)
                .AddHttpMessageHandler<TransientHttpRetryHandler>();
        services.AddSingleton<ApplicationTelemetry>();
        services.AddSingleton<IDockerInstanceClient, DockerInstanceClient>();
        services.AddSingleton<IPortainerClient, PortainerClient>();

        if (applicationOptions.Vulnerabilities.Enabled)
        {
            switch (applicationOptions.Vulnerabilities.Provider)
            {
                case VulnerabilityProviderKind.DockerScout:
                    {
                        services.AddSingleton<IVulnerabilityProvider, DockerScoutVulnerabilityProvider>();
                    }
                    break;

                case VulnerabilityProviderKind.Trivy:
                    {
                        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
                        services.AddSingleton<IVulnerabilityProvider, TrivyVulnerabilityProvider>();
                    }
                    break;

                default:
                    {
                        services.AddSingleton<IVulnerabilityProvider, DefaultVulnerabilityProvider>();
                    }
                    break;
            }
        }
        else
        {
            services.AddSingleton<IVulnerabilityProvider, DefaultVulnerabilityProvider>();
        }

        services.AddScoped<IDockerHubClient>(serviceProvider => serviceProvider.GetRequiredService<DockerHubClient>());
        services.AddScoped<IRegistryMetadataClient>(serviceProvider => serviceProvider.GetRequiredService<DockerHubClient>());
        services.AddScoped<IRegistryMetadataClient>(serviceProvider => serviceProvider.GetRequiredService<OciRegistryClient>());
        services.AddScoped<IRegistryMetadataService, RegistryMetadataService>();
        services.AddScoped<IImageReferenceParser, ImageReferenceParser>();
        services.AddScoped<IUpdateDetectionService, UpdateDetectionService>();
        services.AddScoped<IDotNetReleaseMetadataService>(serviceProvider => serviceProvider.GetRequiredService<DotNetReleaseMetadataService>());
        services.AddScoped<INginxReleaseMetadataService>(serviceProvider => serviceProvider.GetRequiredService<NginxReleaseMetadataService>());
        services.AddScoped<IDerivedBaseRuntimeDetector, DerivedBaseRuntimeDetector>();
        services.AddScoped<IBaseImageResolver, RegistryBaseImageResolver>();
        services.AddScoped<IImageRegistrationService, ImageRegistrationService>();
        services.AddScoped<IInstanceDiscoveryService, InstanceDiscoveryService>();
        services.AddScoped<IDockerHubAccountImageDiscoveryService, DockerHubAccountImageDiscoveryService>();
        services.AddScoped<IImageScanOrchestrator, ImageScanOrchestrator>();
        services.AddScoped<IRuntimeContainerScanOrchestrator, RuntimeContainerScanOrchestrator>();
        services.AddScoped<IResourceStatisticsCollector, ResourceStatisticsCollector>();
        services.AddScoped<IVulnerabilityEnrichmentService, VulnerabilityEnrichmentService>();
        services.AddSingleton<DashboardRefreshState>();
        services.AddScoped<IApplicationViewService, ApplicationViewService>();
        services.AddScoped<IRuntimeContainerTagSelectionService, RuntimeContainerTagSelectionService>();
        services.AddHostedService<DockerInstanceDiscoveryBackgroundService>();
        services.AddHostedService<DockerHubAccountImageDiscoveryBackgroundService>();
        services.AddHostedService<OwnImageBaseRefreshBackgroundService>();
        services.AddHostedService<RuntimeContainerRefreshBackgroundService>();
        services.AddHostedService<ResourceStatisticsRefreshBackgroundService>();
        services.AddHostedService<VulnerabilityRefreshBackgroundService>();
        services.AddHostedService<ScanCleanupBackgroundService>();

        return services;
    }

    #endregion // Methods
}