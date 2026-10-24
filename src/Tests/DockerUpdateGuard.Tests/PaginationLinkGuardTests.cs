using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="PaginationLinkGuard"/>
/// </summary>
[TestClass]
public class PaginationLinkGuardTests
{
    #region Constants

    /// <summary>
    /// URI of the request that returned the link
    /// </summary>
    private const string CurrentRequest = "https://reg.example.com/v2/acme/app/tags/list?n=100";

    /// <summary>
    /// Origin the listing must stay on
    /// </summary>
    private const string AllowedOrigin = "https://reg.example.com/";

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Verify an absolute link on the allowed origin is returned resolved
    /// </summary>
    /// <param name="link">Raw next-page link</param>
    /// <param name="expected">Expected resolved link</param>
    [TestMethod]
    [DataRow("https://reg.example.com/v2/x?n=100&last=b", "https://reg.example.com/v2/x?n=100&last=b")]
    [DataRow("https://REG.example.com:443/v2/x", "https://reg.example.com/v2/x")]
    [DataRow("https://user:pw@reg.example.com/v2/x", "https://user:pw@reg.example.com/v2/x")]
    public void PaginationLinkGuardResolveNextLinkSameOriginLinkReturnsLink(string link, string expected)
    {
        var resolved = PaginationLinkGuard.ResolveNextLink(new Uri(CurrentRequest), new Uri(AllowedOrigin), link);

        Assert.IsNotNull(resolved, "A link on the allowed origin must be followed");
        Assert.AreEqual(expected,
                        resolved.AbsoluteUri,
                        "The guard must return the resolved link the caller sends unchanged");
    }

    /// <summary>
    /// Verify a relative link is resolved against the current request
    /// </summary>
    /// <param name="link">Raw next-page link</param>
    /// <param name="expected">Expected resolved link</param>
    [TestMethod]
    [DataRow("/v2/acme/app/tags/list?n=100&last=b", "https://reg.example.com/v2/acme/app/tags/list?n=100&last=b")]
    [DataRow("list?n=100&last=b", "https://reg.example.com/v2/acme/app/tags/list?n=100&last=b")]
    public void PaginationLinkGuardResolveNextLinkRelativeLinkResolvesAgainstCurrentRequest(string link, string expected)
    {
        var resolved = PaginationLinkGuard.ResolveNextLink(new Uri(CurrentRequest), new Uri(AllowedOrigin), link);

        Assert.IsNotNull(resolved, "A root-relative or path-relative link must be followed on the current origin");
        Assert.AreEqual(expected,
                        resolved.AbsoluteUri,
                        "A relative link must resolve against the request that returned it");
    }

    /// <summary>
    /// Verify a root-relative link with a backslash second character stays on the allowed host
    /// </summary>
    [TestMethod]
    public void PaginationLinkGuardResolveNextLinkSlashBackslashLinkStaysOnAllowedHost()
    {
        const string link = @"/\attacker.example/x";

        var resolved = PaginationLinkGuard.ResolveNextLink(new Uri(CurrentRequest), new Uri(AllowedOrigin), link);

        Assert.Contains("\\", link, "The input must still contain its backslash, otherwise the test checks a plain path");
        Assert.IsNotNull(resolved, "A slash-backslash link is a path on the current host and must be followed");
        Assert.AreEqual("reg.example.com",
                        resolved.Host,
                        "A slash-backslash link must never move the request to the host named after the slashes");
    }

    /// <summary>
    /// Verify a link on another origin is rejected
    /// </summary>
    /// <param name="link">Raw next-page link</param>
    [TestMethod]
    [DataRow("https://attacker.example/x")]
    [DataRow("https://reg.example.com:8443/v2/x")]
    [DataRow("http://reg.example.com/v2/x")]
    [DataRow("//attacker.example/x")]
    [DataRow("file:///etc/passwd")]
    [DataRow("javascript:alert(1)")]
    [DataRow("ftp://reg.example.com/x")]
    [DataRow("https://attacker.example#@reg.example.com/")]
    [DataRow("https://reg.example.com./v2/x")]
    public void PaginationLinkGuardResolveNextLinkForeignOriginReturnsNull(string link)
    {
        var resolved = PaginationLinkGuard.ResolveNextLink(new Uri(CurrentRequest), new Uri(AllowedOrigin), link);

        Assert.IsNull(resolved, "A link on another host, port, scheme or a downgrade must never be followed");
    }

    /// <summary>
    /// Verify the backslash forms of a foreign link are rejected
    /// </summary>
    /// <param name="link">Raw next-page link</param>
    [TestMethod]
    [DataRow(@"\\attacker.example\x")]
    [DataRow(@"\/attacker.example/x")]
    public void PaginationLinkGuardResolveNextLinkBackslashForeignLinkReturnsNull(string link)
    {
        var resolved = PaginationLinkGuard.ResolveNextLink(new Uri(CurrentRequest), new Uri(AllowedOrigin), link);

        Assert.Contains("\\", link, "The input must still contain its backslash, otherwise the test checks a plain relative path");
        Assert.IsNull(resolved, "A backslash form that names another host must never be followed");
    }

    /// <summary>
    /// Verify an empty, whitespace or unparseable link is rejected
    /// </summary>
    /// <param name="link">Raw next-page link</param>
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("http:/v2/x")]
    [DataRow("https://reg.example.com%2eattacker.example/x")]
    public void PaginationLinkGuardResolveNextLinkUnparseableLinkReturnsNull(string? link)
    {
        var resolved = PaginationLinkGuard.ResolveNextLink(new Uri(CurrentRequest), new Uri(AllowedOrigin), link);

        Assert.IsNull(resolved, "An empty or unparseable link must neither be followed nor throw");
    }

    /// <summary>
    /// Verify the origin description carries scheme, host and port only
    /// </summary>
    [TestMethod]
    public void PaginationLinkGuardDescribeOriginLinkWithCredentialsReturnsSchemeHostAndPortOnly()
    {
        var origin = PaginationLinkGuard.DescribeOrigin(new Uri(CurrentRequest),
                                                        "https://user:secret@attacker.example:8443/collect?token=leak");

        Assert.AreEqual("https://attacker.example:8443",
                        origin,
                        "The origin text must drop user info, path and query of the rejected link");
    }

    /// <summary>
    /// Verify an unparseable link is described by the placeholder
    /// </summary>
    [TestMethod]
    public void PaginationLinkGuardDescribeOriginUnparseableLinkReturnsPlaceholder()
    {
        var origin = PaginationLinkGuard.DescribeOrigin(new Uri(CurrentRequest), "http:/v2/x");

        Assert.AreEqual("unparseable link",
                        origin,
                        "A link that cannot be parsed must be described by the fixed placeholder");
    }

    /// <summary>
    /// Verify the rejection message has the documented text
    /// </summary>
    [TestMethod]
    public void PaginationLinkGuardCreateRejectionMessageReturnsExactText()
    {
        var message = PaginationLinkGuard.CreateRejectionMessage("Docker Hub tag listing for 'acme/api'",
                                                                 "https://attacker.example");

        Assert.AreEqual("Docker Hub tag listing for 'acme/api' stopped: the next-page link pointed to a different origin (https://attacker.example)",
                        message,
                        "The rejection message must use the documented wording");
    }

    #endregion // Methods
}