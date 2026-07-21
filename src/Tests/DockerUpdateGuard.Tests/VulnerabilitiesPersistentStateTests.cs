using Bunit;

using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using VulnerabilitiesPage = DockerUpdateGuard.Components.Pages.Vulnerabilities;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Persistent-state tests for <see cref="VulnerabilitiesPage"/>
/// </summary>
[TestClass]
public class VulnerabilitiesPersistentStateTests
{
    #region Constants

    /// <summary>
    /// Persistent-state key used by the page
    /// </summary>
    private const string StateKey = "Vulnerabilities.Overview";

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Verify the page reuses the prerendered overview without reloading from the view service
    /// </summary>
    [TestMethod]
    public void VulnerabilitiesRestoresFromPersistentState()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();

            testContext.Services.AddSingleton(viewService);

            var persistentState = testContext.GetPersistentComponentState();

            persistentState.Persist<IReadOnlyList<VulnerabilityOverviewItemData>>(StateKey,
                                                                                  new List<VulnerabilityOverviewItemData>
                                                                                  {
                                                                                      new()
                                                                                      {
                                                                                          AdvisoryId = "CVE-RESTORE-1",
                                                                                          Title = "Restored advisory",
                                                                                          Severity = "Critical",
                                                                                      }
                                                                                  });

            var component = testContext.RenderComponent<VulnerabilitiesPage>();

            Assert.Contains("CVE-RESTORE-1", component.Markup, "The page must render the overview restored from persistent state");
            viewService.DidNotReceive().GetVulnerabilityOverviewAsync(Arg.Any<CancellationToken>());
        }
    }

    /// <summary>
    /// Verify the page persists the loaded overview for the interactive render
    /// </summary>
    [TestMethod]
    public void VulnerabilitiesPersistsLoadedStateForPrerender()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetVulnerabilityOverviewAsync(Arg.Any<CancellationToken>())
                       .Returns(new List<VulnerabilityOverviewItemData>
                                {
                                    new()
                                    {
                                        AdvisoryId = "CVE-LOADED-1",
                                        Title = "Loaded advisory",
                                        Severity = "High",
                                    }
                                });

            testContext.Services.AddSingleton(viewService);

            var persistentState = testContext.GetPersistentComponentState();

            testContext.RenderComponent<VulnerabilitiesPage>();

            persistentState.TriggerOnPersisting();

            Assert.IsTrue(persistentState.TryTake<IReadOnlyList<VulnerabilityOverviewItemData>>(StateKey, out var persisted), "The page must persist its loaded overview");
            Assert.AreEqual("CVE-LOADED-1", persisted![0].AdvisoryId, "The persisted overview must match the loaded data");
        }
    }

    #endregion // Methods
}