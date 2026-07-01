using System.Reflection;

using DockerUpdateGuard.Components.Layout;
using DockerUpdateGuard.Configuration;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="NavMenu"/>
/// </summary>
[TestClass]
public class NavMenuTests
{
    #region Fields

    /// <summary>
    /// Non-public Docker Hub configuration visibility resolver
    /// </summary>
    private static readonly MethodInfo _isDockerHubAccountConfiguredMethod = typeof(NavMenu).GetMethod("IsDockerHubAccountConfigured",
                                                                                                       BindingFlags.NonPublic | BindingFlags.Static,
                                                                                                       [typeof(DockerHubOptions)])
                                                                                 ?? throw new InvalidOperationException("NavMenu must expose the non-public static IsDockerHubAccountConfigured(DockerHubOptions) overload for testability");

    #endregion // Fields

    #region Methods

    /// <summary>
    /// Verify the My Images link is shown when both credentials are present
    /// </summary>
    /// <param name="userName">Docker Hub user name</param>
    /// <param name="pat">Personal access token</param>
    [TestMethod]
    [DataRow("alice", "my-pat")]
    [DataRow("bob", "another-token")]
    [DataRow("  user  ", "  token  ")]
    public void NavMenuIsDockerHubAccountConfiguredBothCredentialsSetReturnsTrue(string userName, string pat)
    {
        var options = new DockerHubOptions
                      {
                          UserName = userName,
                          Pat = pat,
                      };

        var result = (bool)_isDockerHubAccountConfiguredMethod.Invoke(null, [options])!;

        Assert.IsTrue(result, $"My Images navigation must be visible when both UserName ('{userName}') and Pat are set");
    }

    /// <summary>
    /// Verify the My Images link is hidden when UserName is absent
    /// </summary>
    /// <param name="userName">Missing or blank user name</param>
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void NavMenuIsDockerHubAccountConfiguredMissingUserNameReturnsFalse(string? userName)
    {
        var options = new DockerHubOptions
                      {
                          UserName = userName,
                          Pat = "valid-token",
                      };

        var result = (bool)_isDockerHubAccountConfiguredMethod.Invoke(null, [options])!;

        Assert.IsFalse(result, "My Images navigation must be hidden when UserName is absent or blank");
    }

    /// <summary>
    /// Verify the My Images link is hidden when Pat is absent
    /// </summary>
    /// <param name="pat">Missing or blank personal access token</param>
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void NavMenuIsDockerHubAccountConfiguredMissingPatReturnsFalse(string? pat)
    {
        var options = new DockerHubOptions
                      {
                          UserName = "alice",
                          Pat = pat,
                      };

        var result = (bool)_isDockerHubAccountConfiguredMethod.Invoke(null, [options])!;

        Assert.IsFalse(result, "My Images navigation must be hidden when Pat is absent or blank");
    }

    /// <summary>
    /// Verify the My Images link is hidden when neither credential is configured
    /// </summary>
    [TestMethod]
    public void NavMenuIsDockerHubAccountConfiguredBothCredentialsMissingReturnsFalse()
    {
        var options = new DockerHubOptions();

        var result = (bool)_isDockerHubAccountConfiguredMethod.Invoke(null, [options])!;

        Assert.IsFalse(result, "My Images navigation must be hidden when neither UserName nor Pat is configured");
    }

    #endregion // Methods
}