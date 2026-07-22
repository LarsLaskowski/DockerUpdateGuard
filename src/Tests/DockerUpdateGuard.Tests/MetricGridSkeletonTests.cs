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
    #region Methods

    /// <summary>
    /// Verify the metric grid skeleton renders the requested number of card placeholders
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task MetricGridSkeletonRendersRequestedCardCount()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var component = testContext.Render<MetricGridSkeleton>(parameters => parameters.Add(skeleton => skeleton.Cards, 4));

            Assert.AreEqual(4, MarkupTestHelper.CountOccurrences(component.Markup, "dug-skeleton-card"), "The metric grid skeleton must render one card placeholder per requested card");
        }
    }

    /// <summary>
    /// Verify the metric grid skeleton defaults to six card placeholders
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task MetricGridSkeletonDefaultRendersSixCards()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var component = testContext.Render<MetricGridSkeleton>();

            Assert.AreEqual(6, MarkupTestHelper.CountOccurrences(component.Markup, "dug-skeleton-card"), "The metric grid skeleton must render six card placeholders by default");
        }
    }

    #endregion // Methods
}