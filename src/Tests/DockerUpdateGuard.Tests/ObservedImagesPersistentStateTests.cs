using Bunit;

using DockerUpdateGuard.Components.Pages;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Persistent-state tests for <see cref="ObservedImages"/>
/// </summary>
[TestClass]
public class ObservedImagesPersistentStateTests
{
    #region Constants

    /// <summary>
    /// Persistent-state key used by the page
    /// </summary>
    private const string StateKey = "ObservedImages.List";

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Verify the page reuses the prerendered image list without reloading from the view service
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ObservedImagesRestoresFromPersistentState()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();

            RegisterServices(testContext, viewService);

            var persistentState = testContext.GetPersistentComponentState();

            persistentState.Persist<IReadOnlyList<ObservedImageListItemData>>(StateKey,
                                                                              new List<ObservedImageListItemData>
                                                                              {
                                                                                  new()
                                                                                  {
                                                                                      Id = Guid.NewGuid(),
                                                                                      Name = "restored-image",
                                                                                      ImageReference = "docker.io/company/api:1.0.0",
                                                                                  }
                                                                              });

            var component = testContext.Render<ObservedImages>();

            Assert.Contains("restored-image", component.Markup, "The page must render the image list restored from persistent state");
            await viewService.DidNotReceive().GetManualObservedImagesAsync(Arg.Any<CancellationToken>()).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verify the page persists the loaded image list for the interactive render
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ObservedImagesPersistsLoadedStateForPrerender()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetManualObservedImagesAsync(Arg.Any<CancellationToken>())
                       .Returns(new List<ObservedImageListItemData>
                                {
                                    new()
                                    {
                                        Id = Guid.NewGuid(),
                                        Name = "loaded-image",
                                        ImageReference = "docker.io/company/api:1.0.0",
                                    }
                                });

            RegisterServices(testContext, viewService);

            var persistentState = testContext.GetPersistentComponentState();

            testContext.Render<ObservedImages>();

            persistentState.TriggerOnPersisting();

            Assert.IsTrue(persistentState.TryTake<IReadOnlyList<ObservedImageListItemData>>(StateKey, out var persisted), "The page must persist its loaded image list");
            Assert.AreEqual("loaded-image", persisted![0].Name, "The persisted image list must match the loaded data");
        }
    }

    /// <summary>
    /// Register the services required to render the page
    /// </summary>
    /// <param name="testContext">Test context</param>
    /// <param name="viewService">Application view service substitute</param>
    private static void RegisterServices(Bunit.BunitContext testContext, IApplicationViewService viewService)
    {
        testContext.Services.AddSingleton(viewService);
        testContext.Services.AddSingleton(Substitute.For<IImageRegistrationService>());
        testContext.Services.AddSingleton(Substitute.For<IImageScanOrchestrator>());
        testContext.Services.AddSingleton(new DashboardRefreshState());
    }

    #endregion // Methods
}