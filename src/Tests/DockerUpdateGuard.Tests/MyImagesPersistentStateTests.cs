using Bunit;

using DockerUpdateGuard.Components.Pages;
using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Persistent-state tests for <see cref="MyImages"/>
/// </summary>
[TestClass]
public class MyImagesPersistentStateTests
{
    #region Constants

    /// <summary>
    /// Persistent-state key used by the page
    /// </summary>
    private const string StateKey = "MyImages.List";

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Verify the page reuses the prerendered image list without reloading from the view service
    /// </summary>
    [TestMethod]
    public void MyImagesRestoresFromPersistentState()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
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

            var component = testContext.RenderComponent<MyImages>();

            Assert.Contains("restored-image", component.Markup, "The page must render the image list restored from persistent state");
            viewService.DidNotReceive().GetDiscoveryObservedImagesAsync(Arg.Any<CancellationToken>());
        }
    }

    /// <summary>
    /// Verify the page persists the loaded image list for the interactive render
    /// </summary>
    [TestMethod]
    public void MyImagesPersistsLoadedStateForPrerender()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetDiscoveryObservedImagesAsync(Arg.Any<CancellationToken>())
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

            testContext.RenderComponent<MyImages>();

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
    private static void RegisterServices(Bunit.TestContext testContext, IApplicationViewService viewService)
    {
        testContext.Services.AddSingleton(viewService);
        testContext.Services.AddSingleton<IOptions<DockerUpdateGuardOptions>>(Options.Create(new DockerUpdateGuardOptions()));
    }

    #endregion // Methods
}