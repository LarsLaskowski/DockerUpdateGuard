using Bunit;

using DockerUpdateGuard.Components.Pages;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Persistent-state tests for <see cref="SharedBaseImages"/>
/// </summary>
[TestClass]
public class SharedBaseImagesPersistentStateTests
{
    #region Constants

    /// <summary>
    /// Persistent-state key used by the page
    /// </summary>
    private const string StateKey = "BaseImages.List";

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Verify the page reuses the prerendered base-image list without reloading from the view service
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task SharedBaseImagesRestoresFromPersistentState()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();

            testContext.Services.AddSingleton(viewService);

            var persistentState = testContext.GetPersistentComponentState();

            persistentState.Persist<IReadOnlyList<SharedBaseImageListItemData>>(StateKey,
                                                                                new List<SharedBaseImageListItemData>
                                                                                {
                                                                                    new()
                                                                                    {
                                                                                        BaseImageVersionId = Guid.NewGuid(),
                                                                                        ImageReference = "docker.io/library/debian:12",
                                                                                    }
                                                                                });

            var component = testContext.Render<SharedBaseImages>();

            Assert.Contains("Base image overview", component.Markup, "The page must render the base-image overview restored from persistent state");
            await viewService.DidNotReceive().GetBaseImagesAsync(Arg.Any<CancellationToken>()).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verify the page persists the loaded base-image list for the interactive render
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task SharedBaseImagesPersistsLoadedStateForPrerender()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();
            var baseImageVersionId = Guid.NewGuid();

            viewService.GetBaseImagesAsync(Arg.Any<CancellationToken>())
                       .Returns(new List<SharedBaseImageListItemData>
                                {
                                    new()
                                    {
                                        BaseImageVersionId = baseImageVersionId,
                                        ImageReference = "docker.io/library/debian:12",
                                    }
                                });

            testContext.Services.AddSingleton(viewService);

            var persistentState = testContext.GetPersistentComponentState();

            testContext.Render<SharedBaseImages>();

            persistentState.TriggerOnPersisting();

            Assert.IsTrue(persistentState.TryTake<IReadOnlyList<SharedBaseImageListItemData>>(StateKey, out var persisted), "The page must persist its loaded base-image list");
            Assert.AreEqual(baseImageVersionId, persisted![0].BaseImageVersionId, "The persisted base-image list must match the loaded data");
        }
    }

    #endregion // Methods
}