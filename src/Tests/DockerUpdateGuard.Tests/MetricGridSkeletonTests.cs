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
    [TestMethod]
    public void MetricGridSkeletonRendersRequestedCardCount()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var component = testContext.RenderComponent<MetricGridSkeleton>(parameters => parameters.Add(skeleton => skeleton.Cards, 4));

            Assert.AreEqual(4, MarkupTestHelper.CountOccurrences(component.Markup, "dug-skeleton-card"), "The metric grid skeleton must render one card placeholder per requested card");
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

            Assert.AreEqual(6, MarkupTestHelper.CountOccurrences(component.Markup, "dug-skeleton-card"), "The metric grid skeleton must render six card placeholders by default");
        }
    }

    #endregion // Methods
}