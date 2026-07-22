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
    #region Methods

    /// <summary>
    /// Verify the header skeleton renders a hero panel with three placeholder bars
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PageHeaderSkeletonRendersHeroPanelWithThreeBars()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var component = testContext.Render<PageHeaderSkeleton>();
            var markup = component.Markup;

            Assert.Contains("hero-panel", markup, "The header skeleton must render inside the hero panel");
            Assert.AreEqual(3, MarkupTestHelper.CountSkeletons(markup), "The header skeleton must render three placeholder bars");
        }
    }

    #endregion // Methods
}