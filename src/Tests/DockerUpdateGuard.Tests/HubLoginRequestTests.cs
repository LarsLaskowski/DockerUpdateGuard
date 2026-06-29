using DockerUpdateGuard.Vulnerabilities.Data;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="HubLoginRequest"/>
/// </summary>
[TestClass]
public class HubLoginRequestTests
{
    #region Methods

    /// <summary>
    /// Verify the string representation omits the password to prevent credential leakage
    /// </summary>
    [TestMethod]
    public void HubLoginRequestToStringOmitsPassword()
    {
        var request = new HubLoginRequest { Username = "admin", Password = "s3cr3t-pat" };

        var text = request.ToString();

        Assert.IsFalse(text.Contains("s3cr3t-pat", StringComparison.Ordinal),
                       "ToString must not expose the password");
        Assert.IsTrue(text.Contains("admin", StringComparison.Ordinal),
                      "ToString should still expose the non-sensitive username");
    }

    #endregion // Methods
}