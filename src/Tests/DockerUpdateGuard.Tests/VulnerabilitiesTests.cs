using Bunit;

using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using VulnerabilitiesPage = DockerUpdateGuard.Components.Pages.Vulnerabilities;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="VulnerabilitiesPage"/>
/// </summary>
[TestClass]
public class VulnerabilitiesTests
{
    #region Methods

    /// <summary>
    /// Verify the page renders one row per advisory with its severity, package, and reference link
    /// </summary>
    [TestMethod]
    public void VulnerabilitiesActiveAdvisoriesRendersOverviewRow()
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
                                        AdvisoryId = "CVE-2026-7001",
                                        Title = "Sample advisory",
                                        Severity = "Critical",
                                        CvssScore = 9.1m,
                                        ReferenceUrl = "https://nvd.nist.gov/vuln/detail/CVE-2026-7001",
                                        AffectedPackages = ["openssl"],
                                        FixedVersions = ["3.0.1"],
                                        AffectedImages = [
                                                             new VulnerabilityOverviewAffectedImageData
                                                             {
                                                                 ImageVersionId = Guid.NewGuid(),
                                                                 ImageReference = "docker.io/company/api:1.0.0",
                                                                 ObservedImageId = Guid.NewGuid(),
                                                                 IsOwnImage = true,
                                                             },
                                                         ],
                                        AffectedContainerCount = 3,
                                    }
                                });

            testContext.Services.AddSingleton(viewService);

            var component = testContext.RenderComponent<VulnerabilitiesPage>();

            Assert.Contains("CVE-2026-7001", component.Markup, "The advisory identifier must be rendered");
            Assert.Contains("openssl", component.Markup, "The affected package must be rendered");
            Assert.Contains("Critical", component.Markup, "The severity chip must be rendered");
        }
    }

    /// <summary>
    /// Verify the empty state is shown when there are no active vulnerability findings
    /// </summary>
    [TestMethod]
    public void VulnerabilitiesNoActiveFindingsShowsEmptyState()
    {
        var testContext = BlazorTestContextFactory.Create();

        using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetVulnerabilityOverviewAsync(Arg.Any<CancellationToken>())
                       .Returns(new List<VulnerabilityOverviewItemData>());

            testContext.Services.AddSingleton(viewService);

            var component = testContext.RenderComponent<VulnerabilitiesPage>();

            Assert.Contains("No active vulnerability findings have been recorded yet.",
                            component.Markup,
                            "The empty state must be shown when no active findings exist");
        }
    }

    /// <summary>
    /// Verify the free-text filter hides advisories that do not match the search text
    /// </summary>
    [TestMethod]
    public void VulnerabilitiesFreeTextFilterHidesNonMatchingAdvisories()
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
                                        AdvisoryId = "CVE-2026-7002",
                                        Title = "Critical advisory",
                                        Severity = "Critical",
                                    },
                                    new()
                                    {
                                        AdvisoryId = "CVE-2026-7003",
                                        Title = "Low advisory",
                                        Severity = "Low",
                                    }
                                });

            testContext.Services.AddSingleton(viewService);

            var component = testContext.RenderComponent<VulnerabilitiesPage>();

            component.Find("input[placeholder='Filter by advisory, package, or title']").Input("7002");

            Assert.Contains("CVE-2026-7002", component.Markup, "A matching advisory must remain visible after filtering by text");
            Assert.DoesNotContain("CVE-2026-7003", component.Markup, "A non-matching advisory must be hidden after filtering by text");
        }
    }

    /// <summary>
    /// Verify a critical advisory row carries the severity-rail CSS class
    /// </summary>
    [TestMethod]
    public void VulnerabilitiesCriticalAdvisoryRendersSeverityRailClass()
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
                                        AdvisoryId = "CVE-2026-7004",
                                        Title = "Critical rail advisory",
                                        Severity = "Critical",
                                    }
                                });

            testContext.Services.AddSingleton(viewService);

            var component = testContext.RenderComponent<VulnerabilitiesPage>();

            Assert.Contains("dug-rail-critical", component.Markup, "A critical advisory row must carry the severity-rail CSS class");
        }
    }

    #endregion // Methods
}