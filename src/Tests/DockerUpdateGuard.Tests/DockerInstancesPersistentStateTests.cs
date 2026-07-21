using Bunit;

using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using DockerInstancesPage = DockerUpdateGuard.Components.Pages.DockerInstances;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Persistent-state tests for <see cref="DockerInstancesPage"/>
/// </summary>
[TestClass]
public class DockerInstancesPersistentStateTests
{
    #region Constants

    /// <summary>
    /// Persistent-state key for the instance list
    /// </summary>
    private const string ListStateKey = "DockerInstances.List";

    /// <summary>
    /// Persistent-state key for the single-instance detail
    /// </summary>
    private const string SingleDetailStateKey = "DockerInstances.SingleDetail";

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Verify the page reuses the prerendered instance list and single-instance detail without reloading from the view service
    /// </summary>
    [TestMethod]
    public void DockerInstancesRestoresFromPersistentState()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();
            var instanceId = Guid.NewGuid();

            testContext.Services.AddSingleton(viewService);

            var persistentState = testContext.GetPersistentComponentState();

            persistentState.Persist<IReadOnlyList<DockerInstanceListItemData>>(ListStateKey,
                                                                               new List<DockerInstanceListItemData>
                                                                               {
                                                                                   new()
                                                                                   {
                                                                                       Id = instanceId,
                                                                                       Name = "restored-instance",
                                                                                       EndpointUri = "unix:///var/run/docker.sock",
                                                                                   }
                                                                               });
            persistentState.Persist(SingleDetailStateKey,
                                    new DockerInstanceDetailViewData
                                    {
                                        Id = instanceId,
                                        Name = "restored-instance",
                                        EndpointUri = "unix:///var/run/docker.sock",
                                    });

            var component = testContext.RenderComponent<DockerInstancesPage>();

            Assert.Contains("restored-instance", component.Markup, "The page must render the instances restored from persistent state");
            viewService.DidNotReceive().GetDockerInstancesAsync(Arg.Any<CancellationToken>());
        }
    }

    /// <summary>
    /// Verify the page persists the loaded instance list and single-instance detail for the interactive render
    /// </summary>
    [TestMethod]
    public void DockerInstancesPersistsLoadedStateForPrerender()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();
            var instanceId = Guid.NewGuid();

            viewService.GetDockerInstancesAsync(Arg.Any<CancellationToken>())
                       .Returns(Task.FromResult<IReadOnlyList<DockerInstanceListItemData>>([
                                                                                               new DockerInstanceListItemData
                                                                                               {
                                                                                                   Id = instanceId,
                                                                                                   Name = "loaded-instance",
                                                                                                   EndpointUri = "unix:///var/run/docker.sock",
                                                                                               },
                                                                                           ]));
            viewService.GetDockerInstanceDetailAsync(instanceId, Arg.Any<CancellationToken>())
                       .Returns(Task.FromResult<DockerInstanceDetailViewData?>(new DockerInstanceDetailViewData
                                                                               {
                                                                                   Id = instanceId,
                                                                                   Name = "loaded-instance",
                                                                                   EndpointUri = "unix:///var/run/docker.sock",
                                                                               }));

            testContext.Services.AddSingleton(viewService);

            var persistentState = testContext.GetPersistentComponentState();

            testContext.RenderComponent<DockerInstancesPage>();

            persistentState.TriggerOnPersisting();

            Assert.IsTrue(persistentState.TryTake<IReadOnlyList<DockerInstanceListItemData>>(ListStateKey, out var persistedList), "The page must persist its loaded instance list");
            Assert.AreEqual("loaded-instance", persistedList![0].Name, "The persisted instance list must match the loaded data");
            Assert.IsTrue(persistentState.TryTake<DockerInstanceDetailViewData>(SingleDetailStateKey, out var persistedDetail), "The page must persist the single-instance detail");
            Assert.AreEqual("loaded-instance", persistedDetail!.Name, "The persisted single-instance detail must match the loaded data");
        }
    }

    #endregion // Methods
}