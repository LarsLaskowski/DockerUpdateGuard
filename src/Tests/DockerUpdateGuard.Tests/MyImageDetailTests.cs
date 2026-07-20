using System.Reflection;

using Bunit;

using DockerUpdateGuard.Components.Pages;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="MyImageDetail"/>
/// </summary>
[TestClass]
public class MyImageDetailTests
{
    #region Fields

    /// <summary>
    /// Non-public scan-status chip color resolver
    /// </summary>
    private static readonly MethodInfo _getScanStatusColorMethod = typeof(MyImageDetail).GetMethod("GetScanStatusColor", BindingFlags.NonPublic | BindingFlags.Static)
                                                                       ?? throw new InvalidOperationException("MyImageDetail must expose the non-public static GetScanStatusColor method");

    /// <summary>
    /// Non-public vulnerability-assessment chip color resolver
    /// </summary>
    private static readonly MethodInfo _getVulnerabilityAssessmentColorMethod = typeof(MyImageDetail).GetMethod("GetVulnerabilityAssessmentColor", BindingFlags.NonPublic | BindingFlags.Static)
                                                                                    ?? throw new InvalidOperationException("MyImageDetail must expose the non-public static GetVulnerabilityAssessmentColor method");

    #endregion // Fields

    #region Methods

    /// <summary>
    /// Verify scan status chip colors map to expected MudBlazor color values
    /// </summary>
    /// <param name="status">Scan status string</param>
    /// <param name="expectedColor">Expected chip color</param>
    [TestMethod]
    [DataRow("Succeeded", Color.Success)]
    [DataRow("SUCCEEDED", Color.Success)]
    [DataRow("Completed", Color.Success)]
    [DataRow("COMPLETED", Color.Success)]
    [DataRow("Partial", Color.Warning)]
    [DataRow("PARTIAL", Color.Warning)]
    [DataRow("Failed", Color.Error)]
    [DataRow("FAILED", Color.Error)]
    [DataRow("Running", Color.Info)]
    [DataRow("RUNNING", Color.Info)]
    [DataRow("Pending", Color.Warning)]
    [DataRow("PENDING", Color.Warning)]
    [DataRow("Unknown", Color.Default)]
    [DataRow("anything-else", Color.Default)]
    public void MyImageDetailGetScanStatusColorKnownStatusReturnsExpectedColor(string status, Color expectedColor)
    {
        var color = (Color)_getScanStatusColorMethod.Invoke(null, [status])!;

        Assert.AreEqual(expectedColor,
                        color,
                        $"Scan status '{status}' must map to Color.{expectedColor}");
    }

    /// <summary>
    /// Verify vulnerability assessment chip colors map to expected MudBlazor color values
    /// </summary>
    /// <param name="status">Vulnerability assessment status string</param>
    /// <param name="expectedColor">Expected chip color</param>
    [TestMethod]
    [DataRow("Findings Detected", Color.Warning)]
    [DataRow("FINDINGS DETECTED", Color.Warning)]
    [DataRow("No Findings", Color.Success)]
    [DataRow("NO FINDINGS", Color.Success)]
    [DataRow("Failed", Color.Error)]
    [DataRow("FAILED", Color.Error)]
    [DataRow("Not Configured", Color.Default)]
    [DataRow("NOT CONFIGURED", Color.Default)]
    [DataRow("Unsupported", Color.Default)]
    [DataRow("UNSUPPORTED", Color.Default)]
    [DataRow("anything-else", Color.Info)]
    public void MyImageDetailGetVulnerabilityAssessmentColorKnownStatusReturnsExpectedColor(string status, Color expectedColor)
    {
        var color = (Color)_getVulnerabilityAssessmentColorMethod.Invoke(null, [status, null])!;

        Assert.AreEqual(expectedColor,
                        color,
                        $"Vulnerability assessment status '{status}' must map to Color.{expectedColor}");
    }

    /// <summary>
    /// Verify the vulnerability assessment card shows the fixable finding count and the update-finding hint chip
    /// </summary>
    [TestMethod]
    public void MyImageDetailWithFixableFindingsShowsFixableSummaryAndUpdateHint()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var observedImageId = Guid.NewGuid();
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetObservedImageDetailAsync(observedImageId, Arg.Any<CancellationToken>())
                       .Returns(new ObservedImageDetailViewData
                                {
                                    Id = observedImageId,
                                    Name = "Company API",
                                    ImageReference = "docker.io/company/api:1.0.0",
                                    VulnerabilityAssessment = new VulnerabilityAssessmentViewData
                                                              {
                                                                  Status = "Findings detected",
                                                                  ActiveFindingCount = 2,
                                                                  FixableFindingCount = 1,
                                                              },
                                    UpdateFindings = [
                                                         new UpdateFindingViewData
                                                         {
                                                             Type = "Tag recommendation",
                                                             Summary = "Alternative tags are available",
                                                             IsActive = true,
                                                         }
                                                     ],
                                });

            testContext.Services.AddSingleton(viewService);

            var component = testContext.RenderComponent<MyImageDetail>(parameters => parameters.Add(page => page.ObservedImageId, observedImageId));

            Assert.Contains("1 of 2 active findings have a fix available",
                            component.Markup,
                            "The vulnerability assessment card must show the fixable finding count");
            Assert.Contains("Updating may resolve up to 1 of 2 active findings",
                            component.Markup,
                            "The update findings section must show the fixable update hint chip");
        }
    }

    /// <summary>
    /// Verify the update-finding hint chip is hidden when no active finding has a fix available
    /// </summary>
    [TestMethod]
    public void MyImageDetailWithoutFixableFindingsHidesUpdateHint()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var observedImageId = Guid.NewGuid();
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetObservedImageDetailAsync(observedImageId, Arg.Any<CancellationToken>())
                       .Returns(new ObservedImageDetailViewData
                                {
                                    Id = observedImageId,
                                    Name = "Company API",
                                    ImageReference = "docker.io/company/api:1.0.0",
                                    VulnerabilityAssessment = new VulnerabilityAssessmentViewData
                                                              {
                                                                  Status = "No findings",
                                                                  ActiveFindingCount = 0,
                                                                  FixableFindingCount = 0,
                                                              },
                                    UpdateFindings = [
                                                         new UpdateFindingViewData
                                                         {
                                                             Type = "Tag recommendation",
                                                             Summary = "Alternative tags are available",
                                                             IsActive = true,
                                                         }
                                                     ],
                                });

            testContext.Services.AddSingleton(viewService);

            var component = testContext.RenderComponent<MyImageDetail>(parameters => parameters.Add(page => page.ObservedImageId, observedImageId));

            Assert.DoesNotContain("active findings have a fix available",
                                  component.Markup,
                                  "The fixable summary line must be hidden when there are no active findings");
            Assert.DoesNotContain("Updating may resolve",
                                  component.Markup,
                                  "The update findings hint chip must be hidden when no active finding has a fix available");
        }
    }

    #endregion // Methods
}