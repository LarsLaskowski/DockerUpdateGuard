using DockerUpdateGuard.Vulnerabilities.Data;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="HubLoginResponse"/>
/// </summary>
[TestClass]
public class HubLoginResponseTests
{
    #region Methods

    /// <summary>
    /// Verify the string representation omits the token to prevent credential leakage
    /// </summary>
    [TestMethod]
    public void HubLoginResponseToStringOmitsToken()
    {
        var response = new HubLoginResponse
        {
            Token = "header.payload.signature",
        };

        var text = response.ToString();

        Assert.IsFalse(text.Contains("header.payload.signature", StringComparison.Ordinal),
                       "ToString must not expose the token");
    }

    #endregion // Methods
}
