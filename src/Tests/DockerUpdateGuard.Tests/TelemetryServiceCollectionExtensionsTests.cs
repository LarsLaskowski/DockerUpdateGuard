using DockerUpdateGuard.Telemetry;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using OpenTelemetry.Logs;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for shared telemetry service registration behavior
/// </summary>
[TestClass]
public class TelemetryServiceCollectionExtensionsTests
{
    #region Fields

    private static readonly string[] _removedTelemetryLoggingOptionKeys =
    [
        "IncludeFormattedMessage",
        "IncludeScopes",
        "ParseStateValues",
    ];

    #endregion // Fields

    #region Methods

    /// <summary>
    /// Verify logging-only telemetry registration wires logger services without requiring host-specific setup
    /// </summary>
    [TestMethod]
    public void TelemetryServiceCollectionExtensionsAddDockerUpdateGuardTelemetryRegistersLoggingProviderForLoggingOnlyConfiguration()
    {
        var services = new ServiceCollection();

        services.Configure<OpenTelemetryLoggerOptions>(options =>
        {
            options.IncludeFormattedMessage = false;
            options.IncludeScopes = false;
            options.ParseStateValues = false;
        });
        services.AddDockerUpdateGuardTelemetry(options =>
        {
            options.ServiceName = "DockerUpdateGuard.Tests";
            options.Instance = "Production";
            options.EnableLogging = true;
            options.EnableMetrics = false;
            options.EnableTracing = false;
        });

        using var serviceProvider = services.BuildServiceProvider();

        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var loggerProviders = serviceProvider.GetServices<ILoggerProvider>().ToArray();
        var telemetryOptions = serviceProvider.GetRequiredService<IOptions<TelemetryOptions>>()
                                             .Value;
        var loggerOptions = serviceProvider.GetRequiredService<IOptions<OpenTelemetryLoggerOptions>>()
                                           .Value;
        var logger = loggerFactory.CreateLogger(DockerUpdateGuardTelemetry.LoggerCategoryName);

        logger.LogInformation("Telemetry logging pipeline test {TestValue}", 42);

        Assert.IsNotNull(loggerFactory, "The telemetry registration must register an ILoggerFactory instance");
        Assert.IsTrue(loggerProviders.Any(provider => provider.GetType().FullName?.Contains("OpenTelemetry", StringComparison.Ordinal) == true), "The telemetry registration must register an OpenTelemetry logger provider");
        Assert.IsTrue(telemetryOptions.EnableLogging, "The telemetry options must keep logging enabled for logging-only configuration");
        Assert.IsFalse(telemetryOptions.EnableMetrics, "The telemetry options must keep metrics disabled for logging-only configuration");
        Assert.IsFalse(telemetryOptions.EnableTracing, "The telemetry options must keep tracing disabled for logging-only configuration");
        Assert.AreEqual("Production",
                        telemetryOptions.Instance,
                        "The telemetry instance option must preserve the configured instance value");
        Assert.IsTrue(loggerOptions.IncludeFormattedMessage, "OpenTelemetry logging must always include the formatted message");
        Assert.IsTrue(loggerOptions.IncludeScopes, "OpenTelemetry logging must always include scopes");
        Assert.IsTrue(loggerOptions.ParseStateValues, "OpenTelemetry logging must always parse structured state values");
    }

    /// <summary>
    /// Verify configuration binding supports the telemetry instance option
    /// </summary>
    [TestMethod]
    public void TelemetryServiceCollectionExtensionsAddDockerUpdateGuardTelemetryBindsTelemetryConfiguration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                                                                             {
                                                                                 ["Telemetry:ServiceName"] = "DockerUpdateGuard.Configuration.Tests",
                                                                                 ["Telemetry:Instance"] = "Development",
                                                                                 ["Telemetry:EnableLogging"] = "true",
                                                                                 ["Telemetry:EnableMetrics"] = "false",
                                                                                 ["Telemetry:EnableTracing"] = "false",
                                                                             })
                                                      .Build();
        var services = new ServiceCollection();

        services.AddDockerUpdateGuardTelemetry(configuration);

        using var serviceProvider = services.BuildServiceProvider();

        var telemetryOptions = serviceProvider.GetRequiredService<IOptions<TelemetryOptions>>()
                                             .Value;

        Assert.AreEqual("DockerUpdateGuard.Configuration.Tests",
                        telemetryOptions.ServiceName,
                        "The telemetry service name must bind from configuration");
        Assert.AreEqual("Development",
                        telemetryOptions.Instance,
                        "The telemetry instance option must bind from configuration");
        Assert.IsTrue(telemetryOptions.EnableLogging, "The telemetry logging flag must bind from configuration");
        Assert.IsFalse(telemetryOptions.EnableMetrics, "The telemetry metrics flag must bind from configuration");
        Assert.IsFalse(telemetryOptions.EnableTracing, "The telemetry tracing flag must bind from configuration");
    }

    /// <summary>
    /// Verify the shipped and sample configurations use the current telemetry schema
    /// </summary>
    [TestMethod]
    public void TelemetryServiceCollectionExtensionsConfigurationSamplesUseCurrentTelemetrySchema()
    {
        var hostProjectPath = GetHostProjectPath();
        var baseConfiguration = new ConfigurationBuilder().SetBasePath(hostProjectPath)
                                                          .AddJsonFile("appsettings.json", optional: false)
                                                          .Build();
        var developmentConfiguration = TestDevelopmentConfiguration.Create();
        var baseTelemetryOptions = BindTelemetryOptions(baseConfiguration);
        var developmentTelemetryOptions = BindTelemetryOptions(developmentConfiguration);
        var baseTelemetryKeys = baseConfiguration.GetSection(TelemetryOptions.SectionName)
                                                 .GetChildren()
                                                 .Select(section => section.Key)
                                                 .ToArray();
        var developmentTelemetryKeys = developmentConfiguration.GetSection(TelemetryOptions.SectionName)
                                                               .GetChildren()
                                                               .Select(section => section.Key)
                                                               .ToArray();

        Assert.AreEqual("Production",
                        baseTelemetryOptions.Instance,
                        "The shipped appsettings file must define the telemetry instance");
        Assert.AreEqual("Development",
                        developmentTelemetryOptions.Instance,
                        "The development configuration sample must define the telemetry instance");
        Assert.IsFalse(string.IsNullOrWhiteSpace(developmentTelemetryOptions.OtlpEndpoint), "The development configuration sample must define the telemetry OTLP endpoint when using the complete development schema");
        Assert.IsTrue(baseTelemetryOptions.EnableLogging, "The shipped appsettings file must keep telemetry logging enabled");
        Assert.IsTrue(developmentTelemetryKeys.Contains(nameof(TelemetryOptions.EnableLogging),
                                                        StringComparer.Ordinal),
                      "The development telemetry sample must expose the logging enable flag");
        Assert.IsTrue(developmentTelemetryKeys.Contains(nameof(TelemetryOptions.OtlpEndpoint),
                                                        StringComparer.Ordinal),
                      "The development telemetry sample must expose the OTLP endpoint option");

        foreach (var removedOptionKey in _removedTelemetryLoggingOptionKeys)
        {
            Assert.IsFalse(baseTelemetryKeys.Contains(removedOptionKey, StringComparer.Ordinal),
                           $"The shipped telemetry configuration must not expose the removed '{removedOptionKey}' option");
            Assert.IsFalse(developmentTelemetryKeys.Contains(removedOptionKey, StringComparer.Ordinal),
                           $"The development telemetry sample must not expose the removed '{removedOptionKey}' option");
        }
    }

    /// <summary>
    /// Resolve the host project directory from the current test output
    /// </summary>
    /// <returns>Host project directory path</returns>
    private static string GetHostProjectPath()
    {
        var hostProjectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                                                            "..",
                                                            "..",
                                                            "..",
                                                            "..",
                                                            "..",
                                                            "DockerUpdateGuard"));

        Assert.IsTrue(Directory.Exists(hostProjectPath), $"The host project directory '{hostProjectPath}' must exist for configuration verification");

        return hostProjectPath;
    }

    /// <summary>
    /// Bind telemetry options from the supplied configuration
    /// </summary>
    /// <param name="configuration">Configuration root</param>
    /// <returns>Bound telemetry options</returns>
    private static TelemetryOptions BindTelemetryOptions(IConfiguration configuration)
    {
        var telemetryOptions = new TelemetryOptions();

        configuration.GetSection(TelemetryOptions.SectionName)
                     .Bind(telemetryOptions);

        return telemetryOptions;
    }

    #endregion // Methods
}