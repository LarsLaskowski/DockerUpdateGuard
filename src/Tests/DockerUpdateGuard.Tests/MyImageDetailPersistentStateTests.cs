using Bunit;

using DockerUpdateGuard.Components.Pages;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Persistent-state tests for <see cref="MyImageDetail"/>
/// </summary>
[TestClass]
public class MyImageDetailPersistentStateTests
{
    #region Constants

    /// <summary>
    /// Persistent-state key used by the page
    /// </summary>
    private const string StateKey = "MyImageDetail.State";

    #endregion // Constants

    #region Static methods

    /// <summary>
    /// Build a renderable observed-image detail with the given name
    /// </summary>
    /// <param name="observedImageId">Observed image identifier</param>
    /// <param name="name">Observed image name</param>
    /// <returns>Observed image detail view data</returns>
    private static ObservedImageDetailViewData CreateDetail(Guid observedImageId, string name)
    {
        return new ObservedImageDetailViewData
               {
                   Id = observedImageId,
                   Name = name,
                   ImageReference = "docker.io/company/api:1.0.0",
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
    public void MyImageDetailRestoresFromPersistentState()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();
            var observedImageId = Guid.NewGuid();

            testContext.Services.AddSingleton(viewService);

            var persistentState = testContext.GetPersistentComponentState();

            persistentState.Persist(StateKey, CreateDetail(observedImageId, "restored-image"));

            var component = testContext.RenderComponent<MyImageDetail>(parameters => parameters.Add(page => page.ObservedImageId, observedImageId));

            Assert.Contains("restored-image", component.Markup, "The page must render the detail restored from persistent state");
            viewService.DidNotReceive().GetObservedImageDetailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        }
    }

    /// <summary>
    /// Verify the page persists the loaded detail for the interactive render
    /// </summary>
    [TestMethod]
    public void MyImageDetailPersistsLoadedStateForPrerender()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();
            var observedImageId = Guid.NewGuid();

            viewService.GetObservedImageDetailAsync(observedImageId, Arg.Any<CancellationToken>())
                       .Returns(CreateDetail(observedImageId, "loaded-image"));

            testContext.Services.AddSingleton(viewService);

            var persistentState = testContext.GetPersistentComponentState();

            testContext.RenderComponent<MyImageDetail>(parameters => parameters.Add(page => page.ObservedImageId, observedImageId));

            persistentState.TriggerOnPersisting();

            Assert.IsTrue(persistentState.TryTake<ObservedImageDetailViewData>(StateKey, out var persisted), "The page must persist its loaded detail");
            Assert.AreEqual("loaded-image", persisted!.Name, "The persisted detail must match the loaded data");
        }
    }

    #endregion // Methods
}