using DockerUpdateGuard.Portainer.Data;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="PortainerAuthResponse"/>
/// </summary>
[TestClass]
public class PortainerAuthResponseTests
{
    #region Methods

    /// <summary>
    /// Verify the string representation omits the JWT to prevent token leakage
    /// </summary>
    [TestMethod]
    public void PortainerAuthResponseToStringOmitsJwt()
    {
        var response = new PortainerAuthResponse("header.payload.signature");

        var text = response.ToString();

        Assert.IsFalse(text.Contains("header.payload.signature", StringComparison.Ordinal),
                       "ToString must not expose the JWT");
    }

    #endregion // Methods
}