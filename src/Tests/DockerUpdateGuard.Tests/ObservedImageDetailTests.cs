using Bunit;

using DockerUpdateGuard.Components.Pages;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ObservedImageDetail"/>
/// </summary>
[TestClass]
public class ObservedImageDetailTests
{
    #region Methods

    /// <summary>
    /// Verify the vulnerability assessment card shows the fixable finding count and the update-finding hint chip
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ObservedImageDetailWithFixableFindingsShowsFixableSummaryAndUpdateHint()
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
                                                                  ActiveFindingCount = 3,
                                                                  FixableFindingCount = 2,
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

            var component = testContext.Render<ObservedImageDetail>(parameters => parameters.Add(page => page.ObservedImageId, observedImageId));

            Assert.Contains("2 of 3 findings can be fixed by updating this image",
                            component.Markup,
                            "The vulnerability assessment card must show the fixable finding count");
            Assert.Contains("Updating may resolve up to 2 of 3 active findings",
                            component.Markup,
                            "The update findings section must show the fixable update hint chip");
        }
    }

    /// <summary>
    /// Verify the update-finding hint chip is hidden when there are no active update findings
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ObservedImageDetailWithoutActiveUpdateFindingsHidesUpdateHint()
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
                                                                  ActiveFindingCount = 1,
                                                                  FixableFindingCount = 1,
                                                              },
                                    UpdateFindings = [],
                                });

            testContext.Services.AddSingleton(viewService);

            var component = testContext.Render<ObservedImageDetail>(parameters => parameters.Add(page => page.ObservedImageId, observedImageId));

            Assert.Contains("1 of 1 findings can be fixed by updating this image",
                            component.Markup,
                            "The vulnerability assessment card must still show the fixable finding count");
            Assert.DoesNotContain("Updating may resolve",
                                  component.Markup,
                                  "The update findings hint chip must be hidden when there is no active update finding");
        }
    }

    /// <summary>
    /// Verify the linked container observation time are shown in UTC
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task ObservedImageDetailWithLinkedContainerRendersTimestampsInUtcAsync()
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
                                    IsOwnImage = true,
                                    LinkedRuntimeContainers = [
                                                                  new LinkedRuntimeContainerViewData
                                                                  {
                                                                      DockerInstanceId = Guid.NewGuid(),
                                                                      ContainerId = "abc",
                                                                      ContainerName = "api",
                                                                      RecordedAtUtc = new DateTimeOffset(2026, 11, 23, 23, 27, 0, TimeSpan.FromHours(2)),
                                                                  },
                                                              ],
                                });

            testContext.Services.AddSingleton(viewService);

            var component = testContext.Render<ObservedImageDetail>(parameters => parameters.Add(page => page.ObservedImageId, observedImageId));
            var markup = component.Markup;

            Assert.Contains("2026-11-23 21:27 UTC", markup, "The linked container observation time must be shown in UTC");
        }
    }

    /// <summary>
    /// Verify an unknown identifier shows the not-found panel with a link back to the list instead of the skeleton
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task ObservedImageDetailUnknownIdShowsNotFoundPanel()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var id = Guid.NewGuid();
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetObservedImageDetailAsync(id, Arg.Any<CancellationToken>())
                       .Returns(Task.FromResult<ObservedImageDetailViewData?>(null));

            testContext.Services.AddSingleton(viewService);

            var page = testContext.Render<ObservedImageDetail>(parameters => parameters.Add(component => component.ObservedImageId, id));

            await DetailPageAssert.AssertNotFoundAsync(page, "This observed image does not exist.", "Back to Observed Images", "/observed-images").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verify a failed load shows the fixed reason instead of the exception text, shows no skeleton and logs the exception once
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task ObservedImageDetailLoadFailureShowsFixedErrorAndLogs()
    {
        var exception = new InvalidOperationException("secret text");
        var logger = new TestLogger<ObservedImageDetail>();
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var id = Guid.NewGuid();
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetObservedImageDetailAsync(id, Arg.Any<CancellationToken>())
                       .Returns(Task.FromException<ObservedImageDetailViewData?>(exception));

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton<ILogger<ObservedImageDetail>>(logger);

            var page = testContext.Render<ObservedImageDetail>(parameters => parameters.Add(component => component.ObservedImageId, id));

            await DetailPageAssert.AssertLoadErrorAsync(page, "Loading the observed image failed: unexpected error", "secret text").ConfigureAwait(false);
            DetailPageAssert.AssertLoadLoggedOnce(logger, "LoadObservedImageDetail", exception);
        }
    }

    /// <summary>
    /// Verify a route change to an unknown identifier hides the previous detail and a following pending load clears the not-found panel
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task ObservedImageDetailRouteChangeToUnknownIdHidesPreviousDetail()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var firstId = Guid.NewGuid();
            var unknownId = Guid.NewGuid();
            var thirdId = Guid.NewGuid();
            var pending = new TaskCompletionSource<ObservedImageDetailViewData?>();
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetObservedImageDetailAsync(firstId, Arg.Any<CancellationToken>())
                       .Returns(CreateObservedImageDetailViewData(firstId, "Company API"));
            viewService.GetObservedImageDetailAsync(unknownId, Arg.Any<CancellationToken>())
                       .Returns(Task.FromResult<ObservedImageDetailViewData?>(null));
            viewService.GetObservedImageDetailAsync(thirdId, Arg.Any<CancellationToken>())
                       .Returns(pending.Task);

            testContext.Services.AddSingleton(viewService);

            var page = testContext.Render<ObservedImageDetail>(parameters => parameters.Add(component => component.ObservedImageId, firstId));

            await DetailPageAssert.AssertLoadedAsync(page, "Company API", "This observed image does not exist.").ConfigureAwait(false);

            page.Render(parameters => parameters.Add(component => component.ObservedImageId, unknownId));

            await DetailPageAssert.AssertNotFoundAsync(page, "This observed image does not exist.", "Back to Observed Images", "/observed-images").ConfigureAwait(false);

            Assert.DoesNotContain("Company API", page.Markup, "The previous detail must not be shown for the unknown identifier");

            page.Render(parameters => parameters.Add(component => component.ObservedImageId, thirdId));

            DetailPageAssert.AssertLoading(page);

            pending.SetResult(CreateObservedImageDetailViewData(thirdId, "Company Web"));

            await DetailPageAssert.AssertLoadedAsync(page, "Company Web", "This observed image does not exist.").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Create a detail for the load tests
    /// </summary>
    /// <param name="id">Identifier</param>
    /// <param name="name">Name</param>
    /// <returns>Detail</returns>
    private static ObservedImageDetailViewData CreateObservedImageDetailViewData(Guid id, string name)
    {
        return new ObservedImageDetailViewData
               {
                   Id = id,
                   Name = name,
                   ImageReference = "docker.io/company/api:1.0.0",
               };
    }

    #endregion // Methods
}