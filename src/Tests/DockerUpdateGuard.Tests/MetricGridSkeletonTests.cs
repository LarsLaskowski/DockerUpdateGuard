using Bunit;

using DockerUpdateGuard.Components.Shared;
using DockerUpdateGuard.Tests.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="MetricGridSkeleton"/>
/// </summary>
[TestClass]
public class MetricGridSkeletonTests
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
    /// Verify the metric grid skeleton renders the requested number of card placeholders
    /// </summary>
    [TestMethod]
    public void MetricGridSkeletonRendersRequestedCardCount()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var component = testContext.RenderComponent<MetricGridSkeleton>(parameters => parameters.Add(skeleton => skeleton.Cards, 4));

            Assert.AreEqual(4, CountOccurrences(component.Markup, "dug-skeleton-card"), "The metric grid skeleton must render one card placeholder per requested card");
        }
    }

    /// <summary>
    /// Verify the metric grid skeleton defaults to six card placeholders
    /// </summary>
    [TestMethod]
    public void MetricGridSkeletonDefaultRendersSixCards()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var component = testContext.RenderComponent<MetricGridSkeleton>();

            Assert.AreEqual(6, CountOccurrences(component.Markup, "dug-skeleton-card"), "The metric grid skeleton must render six card placeholders by default");
        }
    }

    #endregion // Methods
}