using System.Reflection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace DockerUpdateGuard.Telemetry;

/// <summary>
/// Service collection extensions for DockerUpdateGuard telemetry
/// </summary>
public static class TelemetryServiceCollectionExtensions
{
    #region Methods

    /// <summary>
    /// Register DockerUpdateGuard telemetry by binding the configured telemetry section
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    /// <returns>The updated service collection</returns>
    public static IServiceCollection AddDockerUpdateGuardTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var telemetrySection = configuration.GetSection(TelemetryOptions.SectionName);
        var telemetryOptions = new TelemetryOptions();

        telemetrySection.Bind(telemetryOptions);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TelemetryOptions>, TelemetryOptionsValidator>());
        services.AddOptions<TelemetryOptions>()
                .Bind(telemetrySection)
                .ValidateOnStart();

        return AddDockerUpdateGuardTelemetryCore(services, telemetryOptions);
    }

    /// <summary>
    /// Register DockerUpdateGuard telemetry by applying an options delegate
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureOptions">Telemetry configuration delegate</param>
    /// <returns>The updated service collection</returns>
    public static IServiceCollection AddDockerUpdateGuardTelemetry(this IServiceCollection services, Action<TelemetryOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        var telemetryOptions = new TelemetryOptions();

        configureOptions(telemetryOptions);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TelemetryOptions>, TelemetryOptionsValidator>());
        services.AddOptions<TelemetryOptions>()
                .Configure(configureOptions)
                .ValidateOnStart();

        return AddDockerUpdateGuardTelemetryCore(services, telemetryOptions);
    }

    /// <summary>
    /// Register shared OpenTelemetry services and instrumentations
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="telemetryOptions">Telemetry options snapshot</param>
    /// <returns>The updated service collection</returns>
    private static IServiceCollection AddDockerUpdateGuardTelemetryCore(IServiceCollection services, TelemetryOptions telemetryOptions)
    {
        ValidateOptions(telemetryOptions);

        var telemetryEnabled = telemetryOptions.EnableLogging
                               || telemetryOptions.EnableMetrics
                               || telemetryOptions.EnableTracing;

        if (telemetryEnabled == false)
        {
            return services;
        }

        var openTelemetryBuilder = services.AddOpenTelemetry();

        openTelemetryBuilder.ConfigureResource(resourceBuilder => ConfigureResource(resourceBuilder, telemetryOptions));

        if (telemetryOptions.EnableLogging)
        {
            services.AddLogging(loggingBuilder =>
                                {
                                    loggingBuilder.AddOpenTelemetry(loggingOptions =>
                                                                    {
                                                                        loggingOptions.IncludeFormattedMessage = true;
                                                                        loggingOptions.IncludeScopes = true;
                                                                        loggingOptions.ParseStateValues = true;
                                                                        loggingOptions.SetResourceBuilder(CreateResourceBuilder(telemetryOptions));

                                                                        ConfigureLoggingExporter(loggingOptions, telemetryOptions);
                                                                    });
                                });
        }

        if (telemetryOptions.EnableTracing)
        {
            openTelemetryBuilder.WithTracing(tracingBuilder =>
                                             {
                                                 tracingBuilder.AddSource(DockerUpdateGuardTelemetry.ActivitySourceName)
                                                               .AddAspNetCoreInstrumentation()
                                                               .AddHttpClientInstrumentation();

                                                 ConfigureTracingExporter(tracingBuilder, telemetryOptions);
                                             });
        }

        if (telemetryOptions.EnableMetrics)
        {
            openTelemetryBuilder.WithMetrics(metricsBuilder =>
                                             {
                                                 metricsBuilder.AddMeter(DockerUpdateGuardTelemetry.MeterName)
                                                               .AddAspNetCoreInstrumentation()
                                                               .AddHttpClientInstrumentation()
                                                               .AddRuntimeInstrumentation();

                                                 ConfigureMetricsExporter(metricsBuilder, telemetryOptions);
                                             });
        }

        return services;
    }

    /// <summary>
    /// Register the OTLP logging exporter when an endpoint is configured
    /// </summary>
    /// <param name="loggingOptions">Logger provider options</param>
    /// <param name="telemetryOptions">Telemetry options snapshot</param>
    private static void ConfigureLoggingExporter(OpenTelemetryLoggerOptions loggingOptions, TelemetryOptions telemetryOptions)
    {
        var hasEndpoint = TelemetryOptionsValidator.TryCreateEndpoint(telemetryOptions.OtlpEndpoint, out var endpoint);

        if (hasEndpoint == false)
        {
            return;
        }

        if (endpoint is null)
        {
            return;
        }

        loggingOptions.AddOtlpExporter(exporterOptions =>
                                       {
                                           exporterOptions.Endpoint = endpoint;
                                       });
    }

    /// <summary>
    /// Register the OTLP metrics exporter when an endpoint is configured
    /// </summary>
    /// <param name="metricsBuilder">Meter provider builder</param>
    /// <param name="telemetryOptions">Telemetry options snapshot</param>
    private static void ConfigureMetricsExporter(MeterProviderBuilder metricsBuilder, TelemetryOptions telemetryOptions)
    {
        var hasEndpoint = TelemetryOptionsValidator.TryCreateEndpoint(telemetryOptions.OtlpEndpoint, out var endpoint);

        if (hasEndpoint == false)
        {
            return;
        }

        if (endpoint is null)
        {
            return;
        }

        metricsBuilder.AddOtlpExporter(exporterOptions =>
                                       {
                                           exporterOptions.Endpoint = endpoint;
                                       });
    }

    /// <summary>
    /// Register the OTLP trace exporter when an endpoint is configured
    /// </summary>
    /// <param name="tracingBuilder">Tracer provider builder</param>
    /// <param name="telemetryOptions">Telemetry options snapshot</param>
    private static void ConfigureTracingExporter(TracerProviderBuilder tracingBuilder, TelemetryOptions telemetryOptions)
    {
        var hasEndpoint = TelemetryOptionsValidator.TryCreateEndpoint(telemetryOptions.OtlpEndpoint, out var endpoint);

        if (hasEndpoint == false)
        {
            return;
        }

        if (endpoint is null)
        {
            return;
        }

        tracingBuilder.AddOtlpExporter(exporterOptions =>
                                       {
                                           exporterOptions.Endpoint = endpoint;
                                       });
    }

    /// <summary>
    /// Configure the shared resource attributes for DockerUpdateGuard telemetry
    /// </summary>
    /// <param name="resourceBuilder">Resource builder</param>
    /// <param name="telemetryOptions">Telemetry options snapshot</param>
    private static void ConfigureResource(ResourceBuilder resourceBuilder, TelemetryOptions telemetryOptions)
    {
        resourceBuilder.AddService(serviceName: telemetryOptions.ServiceName, serviceVersion: GetServiceVersion());

        if (string.IsNullOrWhiteSpace(telemetryOptions.Instance) == false)
        {
            resourceBuilder.AddAttributes([
                                              new KeyValuePair<string, object>(TelemetryResourceAttributeNames.DeploymentEnvironmentName, telemetryOptions.Instance),
                                          ]);
        }
    }

    /// <summary>
    /// Create a resource builder for logging telemetry
    /// </summary>
    /// <param name="telemetryOptions">Telemetry options snapshot</param>
    /// <returns>Configured resource builder</returns>
    private static ResourceBuilder CreateResourceBuilder(TelemetryOptions telemetryOptions)
    {
        var resourceBuilder = ResourceBuilder.CreateDefault();

        ConfigureResource(resourceBuilder, telemetryOptions);

        return resourceBuilder;
    }

    /// <summary>
    /// Resolve the service version for OpenTelemetry resources
    /// </summary>
    /// <returns>Service version value</returns>
    private static string GetServiceVersion()
    {
        var entryAssembly = Assembly.GetEntryAssembly() ?? typeof(TelemetryServiceCollectionExtensions).Assembly;
        var informationalVersion = entryAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informationalVersion) == false)
        {
            return informationalVersion;
        }

        var assemblyVersion = entryAssembly.GetName().Version?.ToString();

        if (string.IsNullOrWhiteSpace(assemblyVersion) == false)
        {
            return assemblyVersion;
        }

        return "unknown";
    }

    /// <summary>
    /// Validate the current telemetry options and fail fast for invalid values
    /// </summary>
    /// <param name="telemetryOptions">Telemetry options snapshot</param>
    private static void ValidateOptions(TelemetryOptions telemetryOptions)
    {
        var validator = new TelemetryOptionsValidator();
        var validationResult = validator.Validate(Options.DefaultName, telemetryOptions);

        if (validationResult.Failed)
        {
            throw new OptionsValidationException(nameof(TelemetryOptions),
                                                 typeof(TelemetryOptions),
                                                 validationResult.Failures);
        }
    }

    #endregion // Methods
}