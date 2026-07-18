using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Telemetry;

/// <summary>
/// Validates telemetry configuration
/// </summary>
public class TelemetryOptionsValidator : IValidateOptions<TelemetryOptions>
{
    #region Methods

    /// <summary>
    /// Validate that the OTLP endpoint is an absolute HTTP URI
    /// </summary>
    /// <param name="otlpEndpoint">OTLP endpoint value</param>
    /// <param name="endpoint">Parsed endpoint</param>
    /// <returns><c>true</c> if parsing succeeded</returns>
    public static bool TryCreateEndpoint(string? otlpEndpoint, out Uri? endpoint)
    {
        endpoint = null;

        if (string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            return false;
        }

        if (Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var parsedEndpoint) == false)
        {
            return false;
        }

        var usesSupportedScheme = parsedEndpoint.Scheme == Uri.UriSchemeHttp
                                  || parsedEndpoint.Scheme == Uri.UriSchemeHttps;

        if (usesSupportedScheme == false)
        {
            return false;
        }

        endpoint = parsedEndpoint;

        return true;
    }

    #endregion // Methods

    #region IValidateOptions

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, TelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        var telemetryEnabled = options.EnableLogging || options.EnableMetrics || options.EnableTracing;

        if (telemetryEnabled == false)
        {
            return ValidateOptionsResult.Success;
        }

        if (string.IsNullOrWhiteSpace(options.ServiceName))
        {
            failures.Add($"'{TelemetryOptions.SectionName}:{nameof(TelemetryOptions.ServiceName)}' must be configured when telemetry is enabled");
        }

        if (string.IsNullOrWhiteSpace(options.OtlpEndpoint) == false)
        {
            var hasValidEndpoint = TryCreateEndpoint(options.OtlpEndpoint, out _);

            if (hasValidEndpoint == false)
            {
                failures.Add($"'{TelemetryOptions.SectionName}:{nameof(TelemetryOptions.OtlpEndpoint)}' must be an absolute http or https URI");
            }
        }

        return failures.Count == 0
                   ? ValidateOptionsResult.Success
                   : ValidateOptionsResult.Fail(failures);
    }

    #endregion // IValidateOptions
}