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
    #region Methods

    /// <summary>
    /// Verify the table skeleton renders the requested number of row placeholders inside a section paper
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
            Assert.AreEqual(4, MarkupTestHelper.CountSkeletons(markup), "The table skeleton must render one placeholder per requested row");
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

            Assert.AreEqual(5, MarkupTestHelper.CountSkeletons(component.Markup), "The table skeleton must render five row placeholders by default");
        }
    }

    /// <summary>
    /// Verify a title adds one extra placeholder bar above the rows
    /// </summary>
    [TestMethod]
    public void TableSkeletonWithTitleRendersOneExtraBar()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var withoutTitle = testContext.RenderComponent<TableSkeleton>(parameters => parameters.Add(skeleton => skeleton.Rows, 3));
            var withTitle = testContext.RenderComponent<TableSkeleton>(parameters => parameters.Add(skeleton => skeleton.Rows, 3)
                                                                                               .Add(skeleton => skeleton.Title, "Overview"));

            Assert.AreEqual(3, MarkupTestHelper.CountSkeletons(withoutTitle.Markup), "Without a title only the row placeholders must render");
            Assert.AreEqual(4, MarkupTestHelper.CountSkeletons(withTitle.Markup), "A title must add exactly one placeholder bar above the rows");
        }
    }

    /// <summary>
    /// Verify the embedded variant renders the row placeholders without the surrounding section paper
    /// </summary>
    [TestMethod]
    public void TableSkeletonEmbeddedOmitsSectionPaper()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var component = testContext.RenderComponent<TableSkeleton>(parameters => parameters.Add(skeleton => skeleton.Embedded, true));
            var markup = component.Markup;

            Assert.DoesNotContain("section-paper", markup, "The embedded table skeleton must not render the surrounding section paper");
            Assert.AreEqual(5, MarkupTestHelper.CountSkeletons(markup), "The embedded table skeleton must still render its row placeholders");
        }
    }

    #endregion // Methods
}