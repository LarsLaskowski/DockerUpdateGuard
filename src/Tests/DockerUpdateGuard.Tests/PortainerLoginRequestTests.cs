using DockerUpdateGuard.Portainer.Data;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="PortainerLoginRequest"/>
/// </summary>
[TestClass]
public class PortainerLoginRequestTests
{
    #region Methods

    /// <summary>
    /// Verify the string representation omits the password to prevent credential leakage
    /// </summary>
    [TestMethod]
    public void PortainerLoginRequestToStringOmitsPassword()
    {
        var request = new PortainerLoginRequest("admin", "s3cr3t-password");

        var text = request.ToString();

        Assert.IsFalse(text.Contains("s3cr3t-password", StringComparison.Ordinal),
                       "ToString must not expose the password");
        Assert.IsTrue(text.Contains("admin", StringComparison.Ordinal),
                      "ToString should still expose the non-sensitive username");
    }

    #endregion // Methods
}