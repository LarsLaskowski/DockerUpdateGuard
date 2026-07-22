using System.Reflection;
using System.Text;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Vulnerabilities;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for the JWT expiry parsing of <see cref="DockerScoutVulnerabilityProvider"/>
/// </summary>
[TestClass]
public class DockerScoutTokenExpiryTests
{
    #region Methods

    /// <summary>
    /// Verify a token with an exp claim resolves an expiry reduced by the safety margin
    /// </summary>
    [TestMethod]
    public void DockerScoutTokenExpiryValidExpClaimResolvesExpiryWithSafetyMargin()
    {
        var expiryUtc = DateTimeOffset.UtcNow.AddHours(1);
        var token = CreateJwt($"{{\"exp\":{expiryUtc.ToUnixTimeSeconds()}}}");

        var resolved = InvokeResolveTokenExpiryUtc(CreateProvider(new TestLogger<DockerScoutVulnerabilityProvider>()), token);

        Assert.IsLessThan(expiryUtc,
                          resolved,
                          "The resolved expiry must be reduced by the safety margin");
        Assert.IsGreaterThan(expiryUtc.AddMinutes(-2),
                             resolved,
                             "The resolved expiry must stay close to the token expiry");
    }

    /// <summary>
    /// Verify a token without an exp claim falls back to the default lifetime
    /// </summary>
    [TestMethod]
    public void DockerScoutTokenExpiryMissingExpClaimFallsBackToDefaultLifetime()
    {
        var token = CreateJwt("""{"sub":"user"}""");
        var nowUtc = DateTimeOffset.UtcNow;

        var resolved = InvokeResolveTokenExpiryUtc(CreateProvider(new TestLogger<DockerScoutVulnerabilityProvider>()), token);

        Assert.IsGreaterThan(nowUtc,
                             resolved,
                             "A token without an exp claim must fall back to a future expiry");
        Assert.IsLessThan(nowUtc.AddMinutes(10),
                          resolved,
                          "The fallback lifetime must be the short default lifetime");
    }

    /// <summary>
    /// Verify a token that is not a valid JWT falls back to the default lifetime
    /// </summary>
    [TestMethod]
    public void DockerScoutTokenExpirySinglePartTokenFallsBackToDefaultLifetime()
    {
        var nowUtc = DateTimeOffset.UtcNow;

        var resolved = InvokeResolveTokenExpiryUtc(CreateProvider(new TestLogger<DockerScoutVulnerabilityProvider>()), "not-a-jwt");

        Assert.IsGreaterThan(nowUtc,
                             resolved,
                             "A malformed token must fall back to a future expiry");
        Assert.IsLessThan(nowUtc.AddMinutes(10),
                          resolved,
                          "The fallback lifetime must be the short default lifetime");
    }

    /// <summary>
    /// Verify a token with an unparseable payload logs the failure and falls back to the default lifetime
    /// </summary>
    [TestMethod]
    public void DockerScoutTokenExpiryUnparseablePayloadLogsAndFallsBack()
    {
        var logger = new TestLogger<DockerScoutVulnerabilityProvider>();
        var token = "header.!!!invalid!!!.signature";
        var nowUtc = DateTimeOffset.UtcNow;

        var resolved = InvokeResolveTokenExpiryUtc(CreateProvider(logger), token);

        Assert.IsGreaterThan(nowUtc,
                             resolved,
                             "An unparseable payload must fall back to a future expiry");
        Assert.Contains(entry => entry.EventId.Id == 3418,
                        logger.Entries,
                        "An unparseable token payload must be logged");
    }

    /// <summary>
    /// Verify disposing the provider does not throw
    /// </summary>
    [TestMethod]
    public void DockerScoutVulnerabilityProviderDisposeReleasesResources()
    {
        var provider = CreateProvider(new TestLogger<DockerScoutVulnerabilityProvider>());

        Exception? exception = null;

        try
        {
            provider.Dispose();
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        Assert.IsNull(exception, "Disposing the provider must release its resources without throwing");
    }

    /// <summary>
    /// Build a JWT string from a payload JSON document
    /// </summary>
    /// <param name="payloadJson">Payload JSON</param>
    /// <returns>JWT string</returns>
    private static string CreateJwt(string payloadJson)
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson))
                             .TrimEnd('=')
                             .Replace('+', '-')
                             .Replace('/', '_');

        return $"header.{payload}.signature";
    }

    /// <summary>
    /// Invoke the private ResolveTokenExpiryUtc method via reflection
    /// </summary>
    /// <param name="provider">Provider instance</param>
    /// <param name="jwtToken">JWT token</param>
    /// <returns>Resolved expiry</returns>
    private static DateTimeOffset InvokeResolveTokenExpiryUtc(DockerScoutVulnerabilityProvider provider, string jwtToken)
    {
        var method = typeof(DockerScoutVulnerabilityProvider).GetMethod("ResolveTokenExpiryUtc", BindingFlags.NonPublic | BindingFlags.Instance)
                         ?? throw new InvalidOperationException("DockerScoutVulnerabilityProvider must expose the private ResolveTokenExpiryUtc method");

        return (DateTimeOffset)method.Invoke(provider, [jwtToken])!;
    }

    /// <summary>
    /// Create a Docker Scout provider instance for the tests
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <returns>Provider</returns>
    private static DockerScoutVulnerabilityProvider CreateProvider(TestLogger<DockerScoutVulnerabilityProvider> logger)
    {
        var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(new DockerUpdateGuardOptions());
        var httpClientFactory = Substitute.For<IHttpClientFactory>();

        return new DockerScoutVulnerabilityProvider(httpClientFactory, optionsMonitor, logger);
    }

    #endregion // Methods
}