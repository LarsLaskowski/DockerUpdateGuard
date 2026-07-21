using Bunit;

using DockerUpdateGuard.Components.Pages;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Persistent-state tests for <see cref="RuntimeContainerDetail"/>
/// </summary>
[TestClass]
public class RuntimeContainerDetailPersistentStateTests
{
    #region Constants

    /// <summary>
    /// Persistent-state key used by the page
    /// </summary>
    private const string StateKey = "RuntimeContainerDetail.State";

    /// <summary>
    /// Container identifier used by the tests
    /// </summary>
    private const string ContainerId = "container-a";

    #endregion // Constants

    #region Static methods

    /// <summary>
    /// Build a renderable runtime-container detail with the given name
    /// </summary>
    /// <param name="dockerInstanceId">Docker instance identifier</param>
    /// <param name="containerName">Container name</param>
    /// <returns>Runtime container detail view data</returns>
    private static RuntimeContainerDetailViewData CreateDetail(Guid dockerInstanceId, string containerName)
    {
        return new RuntimeContainerDetailViewData
               {
                   DockerInstanceId = dockerInstanceId,
                   ContainerId = ContainerId,
                   ContainerName = containerName,
                   DockerInstanceName = "Production",
                   ImageReference = "ghcr.io/acme/api:1.0.0",
                   CurrentTag = "1.0.0",
                   RuntimeStatus = "Running",
                   VulnerabilityAssessment = new VulnerabilityAssessmentViewData
                                             {
                                                 Status = "No findings",
                                             },
               };
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Verify the page reuses the prerendered detail without reloading from the view service
    /// </summary>
    [TestMethod]
    public void RuntimeContainerDetailRestoresFromPersistentState()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();
            var dockerInstanceId = Guid.NewGuid();

            RegisterServices(testContext, viewService);

            var persistentState = testContext.GetPersistentComponentState();

            persistentState.Persist(StateKey, CreateDetail(dockerInstanceId, "restored-container"));

            var component = testContext.RenderComponent<RuntimeContainerDetail>(parameters => parameters.Add(page => page.DockerInstanceId, dockerInstanceId)
                                                                                                        .Add(page => page.ContainerId, ContainerId));

            Assert.Contains("restored-container", component.Markup, "The page must render the detail restored from persistent state");
            viewService.DidNotReceive().GetRuntimeContainerDetailAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
    }

    /// <summary>
    /// Verify the page persists the loaded detail for the interactive render
    /// </summary>
    [TestMethod]
    public void RuntimeContainerDetailPersistsLoadedStateForPrerender()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();
            var dockerInstanceId = Guid.NewGuid();

            viewService.GetRuntimeContainerDetailAsync(dockerInstanceId, ContainerId, Arg.Any<CancellationToken>())
                       .Returns(CreateDetail(dockerInstanceId, "loaded-container"));

            RegisterServices(testContext, viewService);

            var persistentState = testContext.GetPersistentComponentState();
            var component = testContext.RenderComponent<RuntimeContainerDetail>(parameters => parameters.Add(page => page.DockerInstanceId, dockerInstanceId)
                                                                                                        .Add(page => page.ContainerId, ContainerId));

            persistentState.TriggerOnPersisting();

            Assert.IsTrue(persistentState.TryTake<RuntimeContainerDetailViewData>(StateKey, out var persisted), "The page must persist its loaded detail");
            Assert.AreEqual("loaded-container", persisted!.ContainerName, "The persisted detail must match the loaded data");
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
        testContext.Services.AddSingleton(Substitute.For<IRuntimeContainerTagSelectionService>());
    }

    #endregion // Methods
}