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
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceDetailRestoresFromPersistentState()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
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

            var component = testContext.Render<DockerInstanceDetail>(parameters => parameters.Add(page => page.DockerInstanceId, instanceId));

            Assert.Contains("restored-instance", component.Markup, "The page must render the detail restored from persistent state");
            await viewService.DidNotReceive().GetDockerInstanceDetailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verify the page persists the loaded detail for the interactive render
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceDetailPersistsLoadedStateForPrerender()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
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

            testContext.Render<DockerInstanceDetail>(parameters => parameters.Add(page => page.DockerInstanceId, instanceId));

            persistentState.TriggerOnPersisting();

            Assert.IsTrue(persistentState.TryTake<DockerInstanceDetailViewData>(StateKey, out var persisted), "The page must persist its loaded detail");
            Assert.AreEqual("loaded-instance", persisted!.Name, "The persisted detail must match the loaded data");
        }
    }

    /// <summary>
    /// Verify a route change after the first render reused the prerendered record shows the skeleton and none of the restored record
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task DockerInstanceDetailRouteChangeAfterRestoreShowsSkeleton()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var firstId = Guid.NewGuid();
            var secondId = Guid.NewGuid();
            var pending = new TaskCompletionSource<DockerInstanceDetailViewData?>();
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetDockerInstanceDetailAsync(secondId, Arg.Any<CancellationToken>())
                       .Returns(pending.Task);

            testContext.Services.AddSingleton(viewService);

            var persistentState = testContext.GetPersistentComponentState();

            persistentState.Persist(StateKey, CreateRestoredDetail(firstId, "restored-instance"));

            var page = testContext.Render<DockerInstanceDetail>(parameters => parameters.Add(component => component.DockerInstanceId, firstId));

            Assert.Contains("restored-instance", page.Markup, "The first render must show the record restored from persistent state");

            page.Render(parameters => parameters.Add(component => component.DockerInstanceId, secondId));

            DetailPageAssert.AssertLoading(page);
            Assert.DoesNotContain("restored-instance", page.Markup, "The restored record must not be shown for the new route");
        }
    }

    /// <summary>
    /// Create a renderable Docker-instance detail
    /// </summary>
    /// <param name="id">Identifier</param>
    /// <param name="name">Name</param>
    /// <returns>Detail</returns>
    private static DockerInstanceDetailViewData CreateRestoredDetail(Guid id, string name)
    {
        return new DockerInstanceDetailViewData
               {
                   Id = id,
                   Name = name,
                   EndpointUri = "unix:///var/run/docker.sock",
               };
    }

    #endregion // Methods
}