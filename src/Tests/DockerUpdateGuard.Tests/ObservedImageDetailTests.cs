using Bunit;

using DockerUpdateGuard.Components.Pages;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;

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

            Assert.Contains("2 of 3 active findings have a fix available",
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

            Assert.Contains("1 of 1 active findings have a fix available",
                            component.Markup,
                            "The vulnerability assessment card must still show the fixable finding count");
            Assert.DoesNotContain("Updating may resolve",
                                  component.Markup,
                                  "The update findings hint chip must be hidden when there is no active update finding");
        }
    }

    #endregion // Methods
}