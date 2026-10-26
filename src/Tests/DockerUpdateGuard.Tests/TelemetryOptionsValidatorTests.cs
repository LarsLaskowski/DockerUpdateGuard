using DockerUpdateGuard.Telemetry;

using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for telemetry options validation
/// </summary>
[TestClass]
public class TelemetryOptionsValidatorTests
{
    #region Methods

    /// <summary>
    /// Verify an OTLP endpoint with an at sign outside its user info fails with the fixed text only
    /// </summary>
    /// <param name="endpoint">Configured OTLP endpoint</param>
    [TestMethod]
    [DataRow("http://admin#secret@collector.example:4317")]
    [DataRow("http://collector.example:4317/@x")]
    public void TelemetryOptionsValidatorOtlpEndpointWithAtSignOutsideUserInfoReturnsFailure(string endpoint)
    {
        const string key = "Telemetry:OtlpEndpoint";

        var validator = new TelemetryOptionsValidator();
        var options = new TelemetryOptions
                      {
                          OtlpEndpoint = endpoint,
                      };

        var validationResult = validator.Validate(Options.DefaultName, options);
        var failures = validationResult.Failures?.ToArray() ?? [];
        var keyFailures = failures.Where(message => message.Contains($"'{key}'", StringComparison.Ordinal))
                                  .ToArray();

        Assert.IsTrue(validationResult.Failed, "An OTLP endpoint with an at sign outside its user info must fail validation");
        Assert.AreSequenceEqual(["'Telemetry:OtlpEndpoint' has an '@' that does not end its user info; percent-encode '@', '#', '?' and '/' in a user name or password"],
                                keyFailures,
                                "The at sign failure must be the only failure naming the OTLP endpoint key");
        Assert.DoesNotContain(message => message.Contains("secret", StringComparison.Ordinal),
                              failures,
                              "No failure may repeat any part of the configured value");
    }

    #endregion // Methods
}