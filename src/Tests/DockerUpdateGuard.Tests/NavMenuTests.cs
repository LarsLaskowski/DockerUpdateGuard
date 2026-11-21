using System.Reflection;

using Bunit;

using DockerUpdateGuard.Components.Layout;
using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NSubstitute;

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

    /// <summary>
    /// Verify the Vulnerabilities navigation entry is always rendered
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task NavMenuRendersVulnerabilitiesNavigationEntry()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.HasBaseImagesAsync(Arg.Any<CancellationToken>())
                       .Returns(false);

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            testContext.Services.AddSingleton<IOptions<DockerUpdateGuardOptions>>(Options.Create(new DockerUpdateGuardOptions()));

            var component = testContext.Render<NavMenu>();
            var vulnerabilitiesLink = component.FindAll("a").SingleOrDefault(link => link.TextContent.Trim() == "Vulnerabilities");

            Assert.IsNotNull(vulnerabilitiesLink, "The Vulnerabilities navigation entry must always be rendered");
            Assert.IsTrue(vulnerabilitiesLink.GetAttribute("href")?.Contains("vulnerabilities", StringComparison.OrdinalIgnoreCase),
                          "The Vulnerabilities navigation entry must link to the vulnerabilities page");
        }
    }

    /// <summary>
    /// Verify the Shared Base Images navigation entry is rendered when shared base images exist
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC7")]
    public async Task NavMenuWithSharedBaseImagesRendersSharedBaseImagesEntryAsync()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var component = RenderNavMenu(testContext, true);

            await component.WaitForAssertionAsync(() => Assert.IsNotNull(FindSharedBaseImagesLink(component), "The Shared Base Images entry must be rendered when shared base images exist"));

            Assert.AreEqual("base-images",
                            FindSharedBaseImagesLink(component)!.GetAttribute("href"),
                            "The Shared Base Images entry must link to the base images page");
        }
    }

    /// <summary>
    /// Verify the Shared Base Images navigation entry is hidden without shared base images
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC7")]
    public async Task NavMenuWithoutSharedBaseImagesHidesSharedBaseImagesEntryAsync()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var component = RenderNavMenu(testContext, false);

            await component.WaitForAssertionAsync(() => Assert.IsNotNull(component.Find("a"), "The navigation menu must have finished rendering"));

            Assert.IsNull(FindSharedBaseImagesLink(component), "The Shared Base Images entry must not be rendered without shared base images");
        }
    }

    /// <summary>
    /// Render the navigation menu for a base images flag
    /// </summary>
    /// <param name="testContext">bUnit test context</param>
    /// <param name="hasBaseImages">Whether the view service reports shared base images</param>
    /// <returns>Rendered component</returns>
    private static IRenderedComponent<NavMenu> RenderNavMenu(BunitContext testContext, bool hasBaseImages)
    {
        var viewService = Substitute.For<IApplicationViewService>();

        viewService.HasBaseImagesAsync(Arg.Any<CancellationToken>())
                   .Returns(hasBaseImages);

        testContext.Services.AddSingleton(viewService);
        testContext.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        testContext.Services.AddSingleton<IOptions<DockerUpdateGuardOptions>>(Options.Create(new DockerUpdateGuardOptions()));

        return testContext.Render<NavMenu>();
    }

    /// <summary>
    /// Find the Shared Base Images navigation link
    /// </summary>
    /// <param name="component">Rendered navigation menu</param>
    /// <returns>Link element, or null when it is not rendered</returns>
    private static AngleSharp.Dom.IElement? FindSharedBaseImagesLink(IRenderedComponent<NavMenu> component)
    {
        return component.FindAll("a").SingleOrDefault(link => link.TextContent.Trim() == "Shared Base Images");
    }

    #endregion // Methods
}