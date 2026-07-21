using Bunit;

using DockerUpdateGuard.Components.Shared;
using DockerUpdateGuard.Tests.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="PageHeaderSkeleton"/>
/// </summary>
[TestClass]
public class PageHeaderSkeletonTests
{
    #region Static methods

    /// <summary>
    /// Count non-overlapping occurrences of a token within a text
    /// </summary>
    /// <param name="text">Text to search</param>
    /// <param name="token">Token to count</param>
    /// <returns>Number of occurrences</returns>
    private static int CountOccurrences(string text, string token)
    {
        return text.Split(token).Length - 1;
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Verify the header skeleton renders a hero panel with three animated placeholder bars
    /// </summary>
    [TestMethod]
    public void PageHeaderSkeletonRendersHeroPanelWithThreeBars()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var component = testContext.RenderComponent<PageHeaderSkeleton>();
            var markup = component.Markup;

            Assert.Contains("hero-panel", markup, "The header skeleton must render inside the hero panel");
            Assert.AreEqual(3, CountOccurrences(markup, "mud-skeleton-wave"), "The header skeleton must render three animated placeholder bars");
        }
    }

    #endregion // Methods
}