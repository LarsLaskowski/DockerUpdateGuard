using System.Reflection;
using System.Text;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.Vulnerabilities;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for the JWT expiry parsing of <see cref="DockerScoutVulnerabilityProvider"/>
/// </summary>
[TestClass]
public class DockerScoutTokenExpiryTests
{
    #region Fields

    /// <summary>
    /// Instant the substituted clock returns
    /// </summary>
    private static readonly DateTimeOffset _fixedNow = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    #endregion // Fields

    #region Methods

    /// <summary>
    /// Verify a token whose exp claim is not a JSON number falls back to the default lifetime of the injected clock
    /// </summary>
    /// <param name="payloadJson">Payload JSON</param>
    [TestMethod]
    [TestCategory("AC1")]
    [DataRow("""{"exp":"1700000000"}""")]
    [DataRow("""{"exp":true}""")]
    [DataRow("""{"exp":null}""")]
    [DataRow("""{"exp":{"a":1}}""")]
    public void DockerScoutTokenExpiryNonNumericExpClaimFallsBackToDefaultLifetime(string payloadJson)
    {
        var clock = CreateClock();

        var resolved = InvokeResolveTokenExpiryUtc(CreateProvider(new TestLogger<DockerScoutVulnerabilityProvider>(), clock), CreateJwt(payloadJson));

        Assert.AreEqual(_fixedNow.AddMinutes(5),
                        resolved,
                        "A non-numeric exp claim must resolve to the clock's now plus five minutes without an exception");
    }

    /// <summary>
    /// Verify a token whose payload root is not an object falls back to the default lifetime of the injected clock
    /// </summary>
    /// <param name="payloadJson">Payload JSON</param>
    [TestMethod]
    [TestCategory("AC1")]
    [DataRow("[]")]
    [DataRow("\"text\"")]
    [DataRow("null")]
    public void DockerScoutTokenExpiryNonObjectPayloadFallsBackToDefaultLifetime(string payloadJson)
    {
        var clock = CreateClock();

        var resolved = InvokeResolveTokenExpiryUtc(CreateProvider(new TestLogger<DockerScoutVulnerabilityProvider>(), clock), CreateJwt(payloadJson));

        Assert.AreEqual(_fixedNow.AddMinutes(5),
                        resolved,
                        "A payload whose root is not an object must resolve to the clock's now plus five minutes without an exception");
    }

    /// <summary>
    /// Verify a token whose exp claim is outside the convertible Unix-seconds range falls back to the default lifetime
    /// </summary>
    /// <param name="expiry">Exp claim as JSON number text</param>
    [TestMethod]
    [TestCategory("AC2")]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("253402300800")]
    [DataRow("9223372036854775807")]
    public void DockerScoutTokenExpiryOutOfRangeExpClaimFallsBackToDefaultLifetime(string expiry)
    {
        var clock = CreateClock();

        var resolved = InvokeResolveTokenExpiryUtc(CreateProvider(new TestLogger<DockerScoutVulnerabilityProvider>(), clock), CreateJwt($"{{\"exp\":{expiry}}}"));

        Assert.AreEqual(_fixedNow.AddMinutes(5),
                        resolved,
                        "An exp claim outside the convertible range must resolve to the clock's now plus five minutes");
    }

    /// <summary>
    /// Verify a token whose exp claim is inside the convertible range resolves to that instant minus the safety margin
    /// </summary>
    /// <param name="expirySeconds">Exp claim in Unix seconds</param>
    [TestMethod]
    [TestCategory("AC3")]
    [DataRow(1900000000L)]
    [DataRow(253402300799L)]
    public void DockerScoutTokenExpiryInRangeExpClaimResolvesExpiryWithSafetyMargin(long expirySeconds)
    {
        var clock = CreateClock();

        var resolved = InvokeResolveTokenExpiryUtc(CreateProvider(new TestLogger<DockerScoutVulnerabilityProvider>(), clock), CreateJwt($"{{\"exp\":{expirySeconds}}}"));

        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(expirySeconds).AddMinutes(-1),
                        resolved,
                        "An in-range exp claim must resolve to that instant minus the one-minute safety margin");
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
    /// Verify a token payload that is not JSON is logged with the fixed JSON message and none of the payload's text
    /// </summary>
    [TestMethod]
    [TestCategory("AC3")]
    public void DockerScoutTokenExpiryMalformedPayloadLogsFixedJsonMessage()
    {
        var payload = JsonParseFailureAssert.CreateForgedLineBody();

        JsonParseFailureAssert.CarriesForgedLine(payload);

        var logger = new TestLogger<DockerScoutVulnerabilityProvider>();

        _ = InvokeResolveTokenExpiryUtc(CreateProvider(logger), CreateJwt(payload));

        var unreadable = Assert.ContainsSingle(entry => entry.EventId.Id == 3418,
                                               logger.Entries,
                                               "A token payload that is not JSON must be logged once as unreadable expiry 3418");

        JsonParseFailureAssert.IsForgedLineRewritten(unreadable.Exception, "Unreadable expiry 3418");
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
    /// Create a clock fixed at <see cref="_fixedNow"/>
    /// </summary>
    /// <returns>Clock</returns>
    private static TimeProvider CreateClock()
    {
        var clock = Substitute.For<TimeProvider>();

        clock.GetUtcNow().Returns(_fixedNow);

        return clock;
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
    /// <param name="timeProvider">Clock; null uses the system clock</param>
    /// <returns>Provider</returns>
    private static DockerScoutVulnerabilityProvider CreateProvider(TestLogger<DockerScoutVulnerabilityProvider> logger, TimeProvider? timeProvider = null)
    {
        var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(new DockerUpdateGuardOptions());
        var httpClientFactory = Substitute.For<IHttpClientFactory>();

        return new DockerScoutVulnerabilityProvider(httpClientFactory, optionsMonitor, logger, new LoginRetryAfterRegistry(TimeProvider.System), new DockerHubRequestLimiter(optionsMonitor), Substitute.For<IServiceScopeFactory>(), timeProvider ?? TimeProvider.System);
    }

    #endregion // Methods
}