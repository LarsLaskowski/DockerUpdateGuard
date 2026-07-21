using Bunit;

using DockerUpdateGuard.Components.Pages;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Persistent-state tests for <see cref="RuntimeContainers"/>
/// </summary>
[TestClass]
public class RuntimeContainersPersistentStateTests
{
    #region Constants

    /// <summary>
    /// Persistent-state key used by the page
    /// </summary>
    private const string StateKey = "RuntimeContainers.List";

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Verify the page reuses the prerendered container list without reloading from the view service
    /// </summary>
    [TestMethod]
    public void RuntimeContainersRestoresFromPersistentState()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();

            RegisterServices(testContext, viewService);

            var persistentState = testContext.GetPersistentComponentState();

            persistentState.Persist<IReadOnlyList<RuntimeContainerListItemData>>(StateKey,
                                                                                 new List<RuntimeContainerListItemData>
                                                                                 {
                                                                                     new()
                                                                                     {
                                                                                         DockerInstanceId = Guid.NewGuid(),
                                                                                         ContainerId = "container-a",
                                                                                         ContainerName = "restored-container",
                                                                                         DockerInstanceName = "Production",
                                                                                         UpdateState = "Up to date",
                                                                                     }
                                                                                 });

            var component = testContext.RenderComponent<RuntimeContainers>();

            Assert.Contains("restored-container", component.Markup, "The page must render the container list restored from persistent state");
            viewService.DidNotReceive().GetRuntimeContainersAsync(Arg.Any<CancellationToken>());
        }
    }

    /// <summary>
    /// Verify the page persists the loaded container list for the interactive render
    /// </summary>
    [TestMethod]
    public void RuntimeContainersPersistsLoadedStateForPrerender()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetRuntimeContainersAsync(Arg.Any<CancellationToken>())
                       .Returns(new List<RuntimeContainerListItemData>
                                {
                                    new()
                                    {
                                        DockerInstanceId = Guid.NewGuid(),
                                        ContainerId = "container-a",
                                        ContainerName = "loaded-container",
                                        DockerInstanceName = "Production",
                                        UpdateState = "Up to date",
                                    }
                                });

            RegisterServices(testContext, viewService);

            var persistentState = testContext.GetPersistentComponentState();
            var component = testContext.RenderComponent<RuntimeContainers>();

            persistentState.TriggerOnPersisting();

            Assert.IsTrue(persistentState.TryTake<IReadOnlyList<RuntimeContainerListItemData>>(StateKey, out var persisted), "The page must persist its loaded container list");
            Assert.AreEqual("loaded-container", persisted![0].ContainerName, "The persisted container list must match the loaded data");
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
        testContext.Services.AddSingleton(Substitute.For<IRuntimeContainerScanOrchestrator>());
        testContext.Services.AddSingleton(new DashboardRefreshState());
    }

    #endregion // Methods
}