using System.Reflection;

using Bunit;

using DockerUpdateGuard.Components.Pages;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    /// Verify the vulnerability assessment card shows the fixable finding count and the update-finding hint chip
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task MyImageDetailWithFixableFindingsShowsFixableSummaryAndUpdateHint()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
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

            var component = testContext.Render<MyImageDetail>(parameters => parameters.Add(page => page.ObservedImageId, observedImageId));

            Assert.Contains("1 of 2 findings can be fixed by updating this image",
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
    /// <returns>Task</returns>
    [TestMethod]
    public async Task MyImageDetailWithoutFixableFindingsHidesUpdateHint()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
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

            var component = testContext.Render<MyImageDetail>(parameters => parameters.Add(page => page.ObservedImageId, observedImageId));

            Assert.DoesNotContain("summary-remediation",
                                  component.Markup,
                                  "The remediation line must be hidden when there are no active findings");
            Assert.DoesNotContain("Updating may resolve",
                                  component.Markup,
                                  "The update findings hint chip must be hidden when no active finding has a fix available");
        }
    }

    /// <summary>
    /// Verify the linked container observation time and the scan history times are shown in UTC
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task MyImageDetailWithLinkedContainerRendersTimestampsInUtcAsync()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var observedImageId = Guid.NewGuid();
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetObservedImageDetailAsync(observedImageId, Arg.Any<CancellationToken>())
                       .Returns(new ObservedImageDetailViewData
                                {
                                    Id = observedImageId,
                                    Name = "Company API",
                                    ImageReference = "docker.io/company/api:1.0.0",
                                    LinkedRuntimeContainers = [
                                                                  new LinkedRuntimeContainerViewData
                                                                  {
                                                                      DockerInstanceId = Guid.NewGuid(),
                                                                      ContainerId = "abc",
                                                                      ContainerName = "api",
                                                                      RecordedAtUtc = new DateTimeOffset(2026, 11, 23, 23, 27, 0, TimeSpan.FromHours(2)),
                                                                  },
                                                              ],
                                    ScanHistory = [
                                                      new ScanHistoryItemData
                                                      {
                                                          Type = "Registry",
                                                          Status = "Succeeded",
                                                          StartedAtUtc = new DateTimeOffset(2026, 11, 23, 10, 5, 0, TimeSpan.FromHours(2)),
                                                          CompletedAtUtc = new DateTimeOffset(2026, 11, 23, 10, 9, 0, TimeSpan.FromHours(2)),
                                                      },
                                                      new ScanHistoryItemData
                                                      {
                                                          Type = "Registry",
                                                          Status = "Running",
                                                          StartedAtUtc = new DateTimeOffset(2026, 11, 22, 1, 30, 0, TimeSpan.Zero),
                                                      },
                                                  ],
                                });

            testContext.Services.AddSingleton(viewService);

            var component = testContext.Render<MyImageDetail>(parameters => parameters.Add(page => page.ObservedImageId, observedImageId));
            var markup = component.Markup;

            Assert.Contains("2026-11-23 21:27 UTC", markup, "The linked container observation time must be shown in UTC");
            Assert.Contains("2026-11-23 08:05 UTC", markup, "The scan start must be shown in UTC");
            Assert.Contains("2026-11-23 08:09 UTC", markup, "The scan completion must be shown in UTC");
            Assert.Contains("2026-11-22 01:30 UTC", markup, "A UTC scan start must keep its wall-clock time");
            Assert.Contains("In progress", markup, "A scan without completion must show the in-progress marker");
        }
    }

    /// <summary>
    /// Verify an unknown identifier shows the not-found panel with a link back to the list instead of the skeleton
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task MyImageDetailUnknownIdShowsNotFoundPanel()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var id = Guid.NewGuid();
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetObservedImageDetailAsync(id, Arg.Any<CancellationToken>())
                       .Returns(Task.FromResult<ObservedImageDetailViewData?>(null));

            testContext.Services.AddSingleton(viewService);

            var page = testContext.Render<MyImageDetail>(parameters => parameters.Add(component => component.ObservedImageId, id));

            await DetailPageAssert.AssertNotFoundAsync(page, "This image does not exist.", "Back to My Images", "/my-images").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verify a failed load shows the fixed reason instead of the exception text, shows no skeleton and logs the exception once
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task MyImageDetailLoadFailureShowsFixedErrorAndLogs()
    {
        var exception = new InvalidOperationException("secret text");
        var logger = new TestLogger<MyImageDetail>();
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var id = Guid.NewGuid();
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetObservedImageDetailAsync(id, Arg.Any<CancellationToken>())
                       .Returns(Task.FromException<ObservedImageDetailViewData?>(exception));

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton<ILogger<MyImageDetail>>(logger);

            var page = testContext.Render<MyImageDetail>(parameters => parameters.Add(component => component.ObservedImageId, id));

            await DetailPageAssert.AssertLoadErrorAsync(page, "Loading the image failed: unexpected error", "secret text").ConfigureAwait(false);
            DetailPageAssert.AssertLoadLoggedOnce(logger, "LoadMyImageDetail", exception);
        }
    }

    /// <summary>
    /// Verify a pending load shows the skeleton only, and the loaded detail replaces it
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task MyImageDetailPendingLoadShowsSkeletonOnly()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var id = Guid.NewGuid();
            var pending = new TaskCompletionSource<ObservedImageDetailViewData?>();
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetObservedImageDetailAsync(id, Arg.Any<CancellationToken>())
                       .Returns(pending.Task);

            testContext.Services.AddSingleton(viewService);

            var page = testContext.Render<MyImageDetail>(parameters => parameters.Add(component => component.ObservedImageId, id));

            DetailPageAssert.AssertLoading(page);

            pending.SetResult(new ObservedImageDetailViewData
                              {
                                  Id = id,
                                  Name = "Company API",
                                  ImageReference = "docker.io/company/api:1.0.0"
                              });

            await DetailPageAssert.AssertLoadedAsync(page, "Company API", "This image does not exist.").ConfigureAwait(false);
        }
    }

    #endregion // Methods
}