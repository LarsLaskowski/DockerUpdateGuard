using Bunit;

using DockerUpdateGuard.Components.Pages;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Persistent-state tests for <see cref="DockerInstanceDetail"/>
/// </summary>
[TestClass]
public class DockerInstanceDetailPersistentStateTests
{
    #region Constants

    /// <summary>
    /// Persistent-state key used by the page
    /// </summary>
    private const string StateKey = "DockerInstanceDetail.State";

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Verify the page reuses the prerendered detail without reloading from the view service
    /// </summary>
    [TestMethod]
    public void DockerInstanceDetailRestoresFromPersistentState()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();
            var instanceId = Guid.NewGuid();

            testContext.Services.AddSingleton(viewService);

            var persistentState = testContext.GetPersistentComponentState();

            persistentState.Persist(StateKey,
                                    new DockerInstanceDetailViewData
                                    {
                                        Id = instanceId,
                                        Name = "restored-instance",
                                        EndpointUri = "unix:///var/run/docker.sock",
                                    });

            var component = testContext.RenderComponent<DockerInstanceDetail>(parameters => parameters.Add(page => page.DockerInstanceId, instanceId));

            Assert.Contains("restored-instance", component.Markup, "The page must render the detail restored from persistent state");
            viewService.DidNotReceive().GetDockerInstanceDetailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        }
    }

    /// <summary>
    /// Verify the page persists the loaded detail for the interactive render
    /// </summary>
    [TestMethod]
    public void DockerInstanceDetailPersistsLoadedStateForPrerender()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();
            var instanceId = Guid.NewGuid();

            viewService.GetDockerInstanceDetailAsync(instanceId, Arg.Any<CancellationToken>())
                       .Returns(Task.FromResult<DockerInstanceDetailViewData?>(new DockerInstanceDetailViewData
                                                                               {
                                                                                   Id = instanceId,
                                                                                   Name = "loaded-instance",
                                                                                   EndpointUri = "unix:///var/run/docker.sock",
                                                                               }));

            testContext.Services.AddSingleton(viewService);

            var persistentState = testContext.GetPersistentComponentState();

            testContext.RenderComponent<DockerInstanceDetail>(parameters => parameters.Add(page => page.DockerInstanceId, instanceId));

            persistentState.TriggerOnPersisting();

            Assert.IsTrue(persistentState.TryTake<DockerInstanceDetailViewData>(StateKey, out var persisted), "The page must persist its loaded detail");
            Assert.AreEqual("loaded-instance", persisted!.Name, "The persisted detail must match the loaded data");
        }
    }

    #endregion // Methods
}