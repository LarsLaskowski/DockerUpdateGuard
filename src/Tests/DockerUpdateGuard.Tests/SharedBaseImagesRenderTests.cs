using Bunit;

using DockerUpdateGuard.Components.Pages;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Rendering tests for <see cref="SharedBaseImages"/>
/// </summary>
[TestClass]
public class SharedBaseImagesRenderTests
{
    #region Methods

    /// <summary>
    /// Verify the page renders the shared base images heading and description
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC6")]
    public async Task SharedBaseImagesRendersSharedBaseImagesHeadingAsync()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var component = RenderPage(testContext,
                                       [
                                           new()
                                           {
                                               BaseImageVersionId = Guid.NewGuid(),
                                               ImageReference = "docker.io/library/debian:12",
                                           }
                                       ]);

            await component.WaitForAssertionAsync(() => Assert.Contains("Base image overview", component.Markup, "The page must render the overview once loaded"));

            Assert.AreEqual("Shared Base Images",
                            component.Find("h3").TextContent.Trim(),
                            "The page heading must read Shared Base Images");
            Assert.AreEqual("Shared base images: bases used by two or more enabled images or containers, with the images built on them, their running containers, and vulnerability posture.",
                            component.Find(".page-description").TextContent.Trim(),
                            "The page description must state the sharing rule without calling the containers running");
        }
    }

    /// <summary>
    /// Verify the page renders the empty state without entries
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC6")]
    public async Task SharedBaseImagesWithoutEntriesRendersEmptyStateAsync()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var component = RenderPage(testContext, []);

            await component.WaitForAssertionAsync(() => Assert.Contains("No base image is used by two or more enabled images or containers yet.",
                                                                        component.Markup,
                                                                        "The empty state must state the sharing rule"));
        }
    }

    /// <summary>
    /// Render the page for the entries the view service returns
    /// </summary>
    /// <param name="testContext">bUnit test context</param>
    /// <param name="items">Entries returned by the view service</param>
    /// <returns>Rendered page</returns>
    private static IRenderedComponent<SharedBaseImages> RenderPage(BunitContext testContext, IReadOnlyList<SharedBaseImageListItemData> items)
    {
        var viewService = Substitute.For<IApplicationViewService>();

        viewService.GetBaseImagesAsync(Arg.Any<CancellationToken>())
                   .Returns(items);

        testContext.Services.AddSingleton(viewService);

        return testContext.Render<SharedBaseImages>();
    }

    #endregion // Methods
}