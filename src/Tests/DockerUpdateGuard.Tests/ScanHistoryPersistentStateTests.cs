using Bunit;

using DockerUpdateGuard.Components.Pages;
using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Persistent-state tests for <see cref="ScanHistory"/>
/// </summary>
[TestClass]
public class ScanHistoryPersistentStateTests
{
    #region Constants

    /// <summary>
    /// Persistent-state key used by the page
    /// </summary>
    private const string StateKey = "ScanHistory.List";

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Verify the page reuses prerendered scan history without reloading from the view service
    /// </summary>
    [TestMethod]
    public void ScanHistoryRestoresFromPersistentState()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();

            RegisterServices(testContext, viewService);

            var persistentState = testContext.GetPersistentComponentState();

            persistentState.Persist<IReadOnlyList<ScanHistoryItemData>>(StateKey,
                                                                        new List<ScanHistoryItemData>
                                                                        {
                                                                            new()
                                                                            {
                                                                                Type = "Discovery",
                                                                                Status = "Completed",
                                                                                TriggerSource = "Manual",
                                                                                SubjectName = "restored-subject",
                                                                                StartedAtUtc = DateTimeOffset.UtcNow,
                                                                            }
                                                                        });

            var component = testContext.RenderComponent<ScanHistory>();

            Assert.Contains("restored-subject", component.Markup, "The page must render the scan history restored from persistent state");
            viewService.DidNotReceive().GetScanHistoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        }
    }

    /// <summary>
    /// Verify the page persists the loaded scan history for the interactive render
    /// </summary>
    [TestMethod]
    public void ScanHistoryPersistsLoadedStateForPrerender()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetScanHistoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                       .Returns(new List<ScanHistoryItemData>
                                {
                                    new()
                                    {
                                        Type = "Discovery",
                                        Status = "Completed",
                                        TriggerSource = "Manual",
                                        SubjectName = "loaded-subject",
                                        StartedAtUtc = DateTimeOffset.UtcNow,
                                    }
                                });

            RegisterServices(testContext, viewService);

            var persistentState = testContext.GetPersistentComponentState();

            testContext.RenderComponent<ScanHistory>();

            persistentState.TriggerOnPersisting();

            Assert.IsTrue(persistentState.TryTake<IReadOnlyList<ScanHistoryItemData>>(StateKey, out var persisted), "The page must persist its loaded scan history");
            Assert.AreEqual("loaded-subject", persisted![0].SubjectName, "The persisted scan history must match the loaded data");
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
        testContext.Services.AddSingleton(new DashboardRefreshState());
        testContext.Services.AddSingleton<IOptionsMonitor<DockerUpdateGuardOptions>>(new TestOptionsMonitor<DockerUpdateGuardOptions>(new DockerUpdateGuardOptions()));
    }

    #endregion // Methods
}