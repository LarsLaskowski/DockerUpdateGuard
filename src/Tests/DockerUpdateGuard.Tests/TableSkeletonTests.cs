using Bunit;

using DockerUpdateGuard.Components.Shared;
using DockerUpdateGuard.Tests.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="TableSkeleton"/>
/// </summary>
[TestClass]
public class TableSkeletonTests
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
    /// Verify the table skeleton renders the requested number of animated row placeholders
    /// </summary>
    [TestMethod]
    public void TableSkeletonRendersRequestedRowCount()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var component = testContext.RenderComponent<TableSkeleton>(parameters => parameters.Add(skeleton => skeleton.Rows, 4));
            var markup = component.Markup;

            Assert.Contains("section-paper", markup, "The table skeleton must render inside a section paper");
            Assert.AreEqual(4, CountOccurrences(markup, "mud-skeleton-wave"), "The table skeleton must render one animated placeholder per requested row");
        }
    }

    /// <summary>
    /// Verify the table skeleton defaults to five row placeholders
    /// </summary>
    [TestMethod]
    public void TableSkeletonDefaultRendersFiveRows()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var component = testContext.RenderComponent<TableSkeleton>();

            Assert.AreEqual(5, CountOccurrences(component.Markup, "mud-skeleton-wave"), "The table skeleton must render five row placeholders by default");
        }
    }

    /// <summary>
    /// Verify a title placeholder bar is rendered when a title is provided
    /// </summary>
    [TestMethod]
    public void TableSkeletonWithTitleRendersTitlePlaceholder()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var component = testContext.RenderComponent<TableSkeleton>(parameters => parameters.Add(skeleton => skeleton.Title, "Overview"));

            Assert.Contains("mb-3", component.Markup, "A title placeholder bar must be rendered when a title is provided");
        }
    }

    /// <summary>
    /// Verify no title placeholder bar is rendered when no title is provided
    /// </summary>
    [TestMethod]
    public void TableSkeletonWithoutTitleOmitsTitlePlaceholder()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var component = testContext.RenderComponent<TableSkeleton>();

            Assert.DoesNotContain("mb-3", component.Markup, "No title placeholder bar must be rendered when no title is provided");
        }
    }

    #endregion // Methods
}