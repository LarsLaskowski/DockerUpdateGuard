using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Enums;
using DockerUpdateGuard.Tests.Data;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="UpdateDetectionService"/>
/// </summary>
[TestClass]
public class UpdateDetectionServiceTests
{
    #region Methods

    /// <summary>
    /// Verify digest changes on the current tag produce an update
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceDigestChangeReturnsCurrentTagUpdate()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "docker.io",
                                              Repository = "library/nginx",
                                              Tag = "latest",
                                              Digest = "sha256:old",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "latest",
                                                  Digest = "sha256:new",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpdateAvailable,
                        evaluation.Status,
                        "A changed digest on the current tag must be treated as an available update");
        Assert.AreEqual("latest",
                        evaluation.RecommendedTag,
                        "Digest-only updates must keep the current tag as the recommendation");
        Assert.AreEqual("sha256:new",
                        evaluation.RecommendedDigest,
                        "Digest-only updates must recommend the latest registry digest");
        Assert.AreEqual("Update available",
                        evaluation.Summary,
                        "Digest-only updates must use a concise user-facing summary");
        Assert.AreEqual("A newer image is available for tag 'latest'",
                        evaluation.Details,
                        "Digest-only updates must not expose raw digests in the details");
        Assert.HasCount(1,
                        evaluation.Candidates,
                        "Digest-only updates must include the latest current-tag candidate");
    }

    /// <summary>
    /// Verify digest changes keep the current alias tag while exposing the matching version tag
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceDigestChangeReturnsAliasAndResolvedVersionCandidates()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "docker.io",
                                              Repository = "networlddev/f1-telemetry",
                                              Tag = "latest",
                                              Digest = "sha256:old",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "latest",
                                                  Digest = "sha256:new",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "2.4.1",
                                                  Digest = "sha256:new",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpdateAvailable,
                        evaluation.Status,
                        "A digest change on latest must still be reported as an available update");
        Assert.AreEqual("latest",
                        evaluation.RecommendedTag,
                        "The current alias tag must remain the recommendation for digest-only updates");
        Assert.AreSequenceEqual(["latest", "2.4.1"],
                                evaluation.Candidates.Select(candidate => candidate.Tag)
                                                     .ToArray(),
                                "Digest-only updates must keep the current alias tag and expose the matching semantic version tag");
    }

    /// <summary>
    /// Verify semantic version candidates choose the highest successor
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceSemverSuccessorReturnsHighestCandidate()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "docker.io",
                                              Repository = "library/nginx",
                                              Tag = "1.25.0",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "1.26.0",
                                                  Digest = "sha256:1260",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "1.27.1",
                                                  Digest = "sha256:1271",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "1.27.0",
                                                  Digest = "sha256:1270",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 05, 31, 12, 00, 00, TimeSpan.Zero),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpdateAvailable,
                        evaluation.Status,
                        "A newer semantic version must be reported as an available update");
        Assert.AreEqual("1.27.1",
                        evaluation.RecommendedTag,
                        "The highest semantic version successor must be recommended");
        Assert.AreEqual("sha256:1271",
                        evaluation.RecommendedDigest,
                        "The recommended digest must come from the selected semantic successor");
        Assert.AreEqual("1.27.1",
                        evaluation.Candidates[0].Tag,
                        "Candidates must be ordered by the newest semantic version first");
    }

    /// <summary>
    /// Verify a pre-release current tag prefers the general-availability release over a later-published pre-release
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServicePreReleaseCurrentTagPrefersReleaseOverLaterPreRelease()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "docker.io",
                                              Repository = "library/nginx",
                                              Tag = "1.2.3-rc1",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "1.2.3-rc5",
                                                  Digest = "sha256:rc5",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 10, 12, 00, 00, TimeSpan.Zero),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "1.2.3",
                                                  Digest = "sha256:ga",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpdateAvailable,
                        evaluation.Status,
                        "A pre-release current tag must report the general-availability release as an available update");
        Assert.AreEqual("1.2.3",
                        evaluation.RecommendedTag,
                        "The general-availability release must outrank a later-published pre-release of the same version");
        Assert.AreEqual("sha256:ga",
                        evaluation.RecommendedDigest,
                        "The recommended digest must come from the general-availability release");
        Assert.AreEqual("1.2.3",
                        evaluation.Candidates[0].Tag,
                        "Candidates must be ordered by version-tag precedence, not publication date");
    }

    /// <summary>
    /// Verify year-based CU tags only advance within the current year line
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceYearCuTagOnlyUsesSameYearSuccessors()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "mcr.microsoft.com",
                                              Repository = "mssql/server",
                                              Tag = "2019-CU32-GDR1-ubuntu-20.04",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "2019-CU33-GDR1-ubuntu-20.04",
                                                  Digest = "sha256:2019-cu33",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 03, 12, 00, 00, TimeSpan.Zero),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "2022-CU14-ubuntu-22.04",
                                                  Digest = "sha256:2022-cu14",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 04, 12, 00, 00, TimeSpan.Zero),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpdateAvailable,
                        evaluation.Status,
                        "Year-based CU tags must still produce an update when a newer tag exists in the same year line");
        Assert.AreEqual("2019-CU33-GDR1-ubuntu-20.04",
                        evaluation.RecommendedTag,
                        "Year-based CU tags must ignore higher tags from a different year line");
        Assert.AreSequenceEqual(["2019-CU33-GDR1-ubuntu-20.04"],
                                evaluation.Candidates.Select(candidate => candidate.Tag)
                                                     .ToArray(),
                                "Only candidates from the same year line must be considered for year-based CU tags");
    }

    /// <summary>
    /// Verify generic year-prefixed tags are treated as part of the same year line
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceYearPrefixedTagUsesSameYearSuccessors()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "mcr.microsoft.com",
                                              Repository = "example/app",
                                              Tag = "2019-release9",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "2019-release10",
                                                  Digest = "sha256:2019-10",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 03, 12, 00, 00, TimeSpan.Zero),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "2022-release1",
                                                  Digest = "sha256:2022-1",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 04, 12, 00, 00, TimeSpan.Zero),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpdateAvailable,
                        evaluation.Status,
                        "Year-prefixed tags must produce an update when a newer tag exists in the same year line");
        Assert.AreEqual("2019-release10",
                        evaluation.RecommendedTag,
                        "Year-prefixed tags must ignore tags from newer year lines");
    }

    /// <summary>
    /// Verify MCR channel tags only advance within the same variant family
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceMcrChannelTagUsesSameVariantFamilySuccessors()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "mcr.microsoft.com",
                                              Repository = "dotnet/runtime",
                                              Tag = "10.0-alpine",
                                              Digest = "sha256:current",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "10.0-alpine",
                                                  Digest = "sha256:current",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "10.0.7-alpine3.23",
                                                  Digest = "sha256:current",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "10.0.8-alpine3.24",
                                                  Digest = "sha256:update",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 03, 12, 00, 00, TimeSpan.Zero),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "10.0.8-jammy",
                                                  Digest = "sha256:other",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 04, 12, 00, 00, TimeSpan.Zero),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpdateAvailable,
                        evaluation.Status,
                        "MCR channel tags must report an available update when a newer exact tag exists in the same variant family");
        Assert.AreEqual("10.0.8-alpine3.24",
                        evaluation.RecommendedTag,
                        "MCR channel tags must recommend the newer exact tag from the same variant family");
        Assert.AreSequenceEqual(["10.0.8-alpine3.24", "10.0.7-alpine3.23"],
                                evaluation.Candidates.Select(candidate => candidate.Tag)
                                                     .ToArray(),
                                "MCR channel tag candidates must stay in the same variant family and include the resolved current exact tag");
    }

    /// <summary>
    /// Verify non-semantic current tags produce manual review candidates
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceNonSemverTagReturnsNeedsReview()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "docker.io",
                                              Repository = "company/app",
                                              Tag = "stable",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "stable",
                                                  Digest = "sha256:stable",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 05, 30, 12, 00, 00, TimeSpan.Zero),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "canary",
                                                  Digest = "sha256:canary",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "preview",
                                                  Digest = "sha256:preview",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.NeedsReview,
                        evaluation.Status,
                        "Non-semantic tags must produce a review result when alternative tags exist");
        Assert.IsNull(evaluation.RecommendedTag, "Non-semantic tags must not auto-select a recommendation");
        Assert.AreSequenceEqual(["preview", "canary"],
                                evaluation.Candidates.Select(candidate => candidate.Tag)
                                                     .ToArray(),
                                "Manual review candidates must exclude the current tag and keep the newest alternatives");
    }

    /// <summary>
    /// Verify current latest digests stay up to date even when higher semantic tags exist
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceLatestAliasWithCurrentLatestDigestReturnsUpToDate()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "docker.io",
                                              Repository = "company/app",
                                              Tag = "latest",
                                              Digest = "sha256:241",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "latest",
                                                  Digest = "sha256:241",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "2.4.1",
                                                  Digest = "sha256:241",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "2.5.0",
                                                  Digest = "sha256:250",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 03, 12, 00, 00, TimeSpan.Zero),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpToDate,
                        evaluation.Status,
                        "A current latest digest must stay up to date even when higher semantic tags exist");
        Assert.AreEqual("The running image already matches the current 'latest' tag",
                        evaluation.Summary,
                        "The summary must explain that the running latest digest is already current");
        Assert.HasCount(0,
                        evaluation.Candidates,
                        "No candidate list should be produced when the running latest digest already matches the registry latest digest");
    }

    /// <summary>
    /// Verify stale calendar tags do not matter when the running image already matches the current latest digest
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceLatestAliasIgnoresOlderPublishedCalendarTagAsync()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "docker.io",
                                              Repository = "linuxserver/heimdall",
                                              Tag = "latest",
                                              Digest = "sha256:241",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "latest",
                                                  Digest = "sha256:241",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "2.4.1",
                                                  Digest = "sha256:241",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "2021.11.28",
                                                  Digest = "sha256:old-calendar",
                                                  PublishedAtUtc = new DateTimeOffset(2021, 11, 28, 12, 00, 00, TimeSpan.Zero),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "2.5.0",
                                                  Digest = "sha256:250",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 03, 12, 00, 00, TimeSpan.Zero),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpToDate,
                        evaluation.Status,
                        "A current latest digest must stay up to date regardless of stale calendar tags");
        Assert.AreEqual("The running image already matches the current 'latest' tag",
                        evaluation.Summary,
                        "The summary must explain that the running latest digest is already current");
    }

    /// <summary>
    /// Verify latest aliases with a matching semantic digest stay up to date when no newer version exists
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceLatestAliasWithMatchingSemanticDigestAndNoSuccessorReturnsUpToDate()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "docker.io",
                                              Repository = "company/app",
                                              Tag = "latest",
                                              Digest = "sha256:241",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "latest",
                                                  Digest = "sha256:241",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "2.4.1",
                                                  Digest = "sha256:241",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpToDate,
                        evaluation.Status,
                        "A latest alias must stay up to date when its digest resolves to the newest semantic version");
        Assert.AreEqual("The running image already matches the current 'latest' tag",
                        evaluation.Summary,
                        "The summary must explain that the running latest digest is already current");
    }

    /// <summary>
    /// Verify latest aliases without semantic matches stay up to date when the registry digest is unchanged
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceLatestAliasWithoutSemanticMatchAndUnchangedDigestReturnsUpToDate()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "docker.io",
                                              Repository = "company/app",
                                              Tag = "latest",
                                              Digest = "sha256:stable",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "latest",
                                                  Digest = "sha256:stable",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "preview",
                                                  Digest = "sha256:preview",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 03, 12, 00, 00, TimeSpan.Zero),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpToDate,
                        evaluation.Status,
                        "A latest alias must not fall back to manual review when the registry latest digest matches the running digest");
        Assert.AreEqual("The running image already matches the current 'latest' tag",
                        evaluation.Summary,
                        "The summary must explain that the running latest digest is already current");
    }

    /// <summary>
    /// Verify semantic version candidates are capped at fifty entries
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceSemverCandidatesAreLimitedToFiftyEntries()
    {
        var service = CreateService();
        var availableTags = Enumerable.Range(1, 60)
                                      .Select(index => new DockerHubTagData
                                                       {
                                                           Tag = $"1.0.{index}",
                                                           Digest = $"sha256:{index}",
                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero).AddMinutes(index),
                                                       })
                                      .ToList();

        availableTags.Add(new DockerHubTagData
                          {
                              Tag = "1.0.0",
                              Digest = "sha256:current",
                              PublishedAtUtc = new DateTimeOffset(2025, 05, 31, 12, 00, 00, TimeSpan.Zero),
                          });

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "docker.io",
                                              Repository = "library/nginx",
                                              Tag = "1.0.0",
                                              Digest = "sha256:current",
                                          },
                                          availableTags);

        Assert.AreEqual(UpdateEvaluationStatus.UpdateAvailable,
                        evaluation.Status,
                        "A larger successor set must still produce an update result");
        Assert.HasCount(50,
                        evaluation.Candidates,
                        "Semantic successor candidates must be capped at fifty entries");
        Assert.AreEqual("1.0.60",
                        evaluation.Candidates[0].Tag,
                        "The highest semantic successor must remain first in the capped candidate set");
    }

    /// <summary>
    /// Verify year-based tags stay up to date when an alias tag of the same year shares the running digest
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceYearAliasTagWithRunningDigestReturnsUpToDate()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "mcr.microsoft.com",
                                              Repository = "mssql/server",
                                              Tag = "2019-CU32-GDR10-ubuntu-20.04",
                                              Digest = "sha256:2019-cu32-gdr10",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "2019-CU32-GDR10-ubuntu-20.04",
                                                  Digest = "sha256:2019-cu32-gdr10",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "2019-latest",
                                                  Digest = "sha256:2019-cu32-gdr10",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpToDate,
                        evaluation.Status,
                        "An alias tag that resolves to the running digest must not be reported as an update");
        Assert.AreEqual("No newer year-based version was found",
                        evaluation.Summary,
                        "The summary must state that no newer year-based version exists");
        Assert.IsEmpty(evaluation.Candidates,
                       "No candidates must be produced when the alias tag carries the running digest");
    }

    /// <summary>
    /// Verify year-based alias tags are not recommended as successors of a concrete servicing tag
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceYearAliasTagIsNotRecommendedAsSuccessor()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "mcr.microsoft.com",
                                              Repository = "mssql/server",
                                              Tag = "2019-CU32-GDR10-ubuntu-20.04",
                                              Digest = "sha256:2019-cu32-gdr10",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "2019-CU32-GDR10-ubuntu-20.04",
                                                  Digest = "sha256:2019-cu32-gdr10",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "2019-latest",
                                                  Digest = "sha256:2019-cu33",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 05, 12, 00, 00, TimeSpan.Zero),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "2019-CU33-ubuntu-20.04",
                                                  Digest = "sha256:2019-cu33",
                                                  PublishedAtUtc = new DateTimeOffset(2025, 06, 05, 12, 00, 00, TimeSpan.Zero),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpdateAvailable,
                        evaluation.Status,
                        "A newer servicing tag of the same year line must still produce an update");
        Assert.AreEqual("2019-CU33-ubuntu-20.04",
                        evaluation.RecommendedTag,
                        "The concrete servicing tag must be recommended instead of the year alias tag");
        Assert.AreSequenceEqual(["2019-CU33-ubuntu-20.04"],
                                evaluation.Candidates.Select(candidate => candidate.Tag)
                                                     .ToArray(),
                                "Alias tags of a different variant family must not become update candidates");
    }

    /// <summary>
    /// Verify successors of the current major line win over a higher major version
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceSemverPrefersCurrentMajorLineSuccessor()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "docker.io",
                                              Repository = "grafana/mimir",
                                              Tag = "2.7.10",
                                              Digest = "sha256:2710",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "2.7.10",
                                                  Digest = "sha256:2710",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-720),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "2.16.1",
                                                  Digest = "sha256:2161",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-90),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "3.1.4",
                                                  Digest = "sha256:314",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-60),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpdateAvailable,
                        evaluation.Status,
                        "A newer version inside the current major line must produce an update");
        Assert.AreEqual("2.16.1",
                        evaluation.RecommendedTag,
                        "Updates must stay inside the current major line while newer versions exist there");
        Assert.AreSequenceEqual(["2.16.1", "3.1.4"],
                                evaluation.Candidates.Select(candidate => candidate.Tag)
                                                     .ToArray(),
                                "Higher major versions must remain selectable behind the recommended in-line successor");
    }

    /// <summary>
    /// Verify a freshly published major version line is not recommended yet
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceSemverDefersRecentMajorUpgrade()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "docker.io",
                                              Repository = "grafana/mimir",
                                              Tag = "2.16.1",
                                              Digest = "sha256:2161",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "2.16.1",
                                                  Digest = "sha256:2161",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-20),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "3.0.0",
                                                  Digest = "sha256:300",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-10),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "3.0.1",
                                                  Digest = "sha256:301",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-2),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpToDate,
                        evaluation.Status,
                        "A major version line that is younger than the configured waiting period must not be recommended");
        Assert.AreEqual("No newer version was found in the 2.x version line",
                        evaluation.Summary,
                        "The summary must state that the current major line has no newer version");
        Assert.AreEqual("Tag '3.0.1' introduces a new major version and is not recommended yet",
                        evaluation.Details,
                        "The details must name the major version that is held back");
    }

    /// <summary>
    /// Verify a major version line with too few releases is not recommended yet
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceSemverDefersMajorUpgradeWithSingleRelease()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "docker.io",
                                              Repository = "grafana/mimir",
                                              Tag = "2.16.1",
                                              Digest = "sha256:2161",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "2.16.1",
                                                  Digest = "sha256:2161",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-400),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "3.0.0",
                                                  Digest = "sha256:300",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-300),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpToDate,
                        evaluation.Status,
                        "A major version line with a single release must not be recommended");
        Assert.AreEqual("Tag '3.0.0' introduces a new major version and is not recommended yet",
                        evaluation.Details,
                        "The details must name the major version that is held back");
    }

    /// <summary>
    /// Verify a major upgrade is deferred while the current major line still receives releases
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceSemverDefersMajorUpgradeWhileCurrentLineIsActive()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "docker.io",
                                              Repository = "grafana/mimir",
                                              Tag = "2.16.1",
                                              Digest = "sha256:2162",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "2.16.2",
                                                  Digest = "sha256:2162",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-3),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "3.0.0",
                                                  Digest = "sha256:300",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-300),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "3.1.4",
                                                  Digest = "sha256:314",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-200),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpToDate,
                        evaluation.Status,
                        "A major upgrade must wait while the current major line still receives releases");
        Assert.AreEqual("Tag '3.1.4' introduces a new major version and is not recommended yet",
                        evaluation.Details,
                        "The details must name the major version that is held back");
    }

    /// <summary>
    /// Verify an established major version line is recommended once the current line is dormant
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceSemverRecommendsEstablishedMajorUpgrade()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "docker.io",
                                              Repository = "grafana/mimir",
                                              Tag = "2.16.1",
                                              Digest = "sha256:2161",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "2.16.1",
                                                  Digest = "sha256:2161",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-400),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "3.0.0",
                                                  Digest = "sha256:300",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-300),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "3.1.4",
                                                  Digest = "sha256:314",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-200),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpdateAvailable,
                        evaluation.Status,
                        "An established major version line must be recommended when the current line is dormant");
        Assert.AreEqual("3.1.4",
                        evaluation.RecommendedTag,
                        "The newest version of the next major line must be recommended");
        Assert.AreEqual("No newer version is available in the 2.x version line",
                        evaluation.Details,
                        "The details must explain why the update crosses a major version boundary");
    }

    /// <summary>
    /// Verify the major upgrade waiting periods can be switched off
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceSemverRecommendsMajorUpgradeWithoutWaitingPeriod()
    {
        var service = CreateService(new ScanningOptions
                                    {
                                        MajorUpgradeMinimumAgeDays = 0,
                                        MajorUpgradeMinimumReleaseCount = 0,
                                    });

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "docker.io",
                                              Repository = "grafana/mimir",
                                              Tag = "2.16.1",
                                              Digest = "sha256:2161",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "2.16.1",
                                                  Digest = "sha256:2161",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-20),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "3.0.0",
                                                  Digest = "sha256:300",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-2),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpdateAvailable,
                        evaluation.Status,
                        "Disabled waiting periods must report the major upgrade immediately");
        Assert.AreEqual("3.0.0",
                        evaluation.RecommendedTag,
                        "The newest version of the next major line must be recommended when no waiting period applies");
    }

    /// <summary>
    /// Verify the next major line is preferred over an even higher major line
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceSemverRecommendsNextMajorLineFirst()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "docker.io",
                                              Repository = "grafana/mimir",
                                              Tag = "2.16.1",
                                              Digest = "sha256:2161",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "2.16.1",
                                                  Digest = "sha256:2161",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-400),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "3.0.0",
                                                  Digest = "sha256:300",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-300),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "3.1.4",
                                                  Digest = "sha256:314",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-200),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "4.0.0",
                                                  Digest = "sha256:400",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-100),
                                              },
                                          ]);

        Assert.AreEqual("3.1.4",
                        evaluation.RecommendedTag,
                        "A major upgrade must advance to the next major line instead of the highest one");
        Assert.AreSequenceEqual(["3.1.4", "3.0.0", "4.0.0"],
                                evaluation.Candidates.Select(candidate => candidate.Tag)
                                                     .ToArray(),
                                "Higher major lines must remain selectable behind the recommended next major line");
    }

    /// <summary>
    /// Verify a major upgrade is deferred while the current major line is still served next to the new line
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceSemverDefersMajorUpgradeForParallelMaintainedLine()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "docker.io",
                                              Repository = "grafana/mimir",
                                              Tag = "2.17.11",
                                              Digest = "sha256:21711",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "2.17.10",
                                                  Digest = "sha256:21710",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-98),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "2.17.11",
                                                  Digest = "sha256:21711",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-74),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "3.0.0",
                                                  Digest = "sha256:300",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-270),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "3.1.0",
                                                  Digest = "sha256:310",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-56),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "3.1.4",
                                                  Digest = "sha256:314",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-6),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpToDate,
                        evaluation.Status,
                        "A major line that is served next to the current major line must not be recommended");
        Assert.AreEqual("Tag '3.1.4' introduces a new major version and is not recommended yet",
                        evaluation.Details,
                        "The details must name the major version that is held back");
    }

    /// <summary>
    /// Verify a major upgrade is deferred when the start of the new major line is outside the scanned tags
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceSemverDefersMajorUpgradeWithTruncatedMajorLineHistory()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "docker.io",
                                              Repository = "grafana/mimir",
                                              Tag = "2.17.11",
                                              Digest = "sha256:21711",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "2.17.11",
                                                  Digest = "sha256:21711",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-74),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "3.0.7",
                                                  Digest = "sha256:307",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-41),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "3.1.0",
                                                  Digest = "sha256:310",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-56),
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "3.1.4",
                                                  Digest = "sha256:314",
                                                  PublishedAtUtc = CreateRelativeTimestamp(-6),
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpToDate,
                        evaluation.Status,
                        "A major line whose start is unknown must not be recommended");
        Assert.AreEqual("Tag '3.1.4' introduces a new major version and is not recommended yet",
                        evaluation.Details,
                        "The details must name the major version that is held back");
    }

    /// <summary>
    /// Verify a major upgrade is deferred when the tags carry no publication timestamps
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceSemverDefersMajorUpgradeWithoutPublicationTimestamps()
    {
        var service = CreateService();

        var evaluation = service.Evaluate(new ImageReference
                                          {
                                              Registry = "ghcr.io",
                                              Repository = "grafana/mimir",
                                              Tag = "2.17.11",
                                              Digest = "sha256:21711",
                                          },
                                          [
                                              new DockerHubTagData
                                              {
                                                  Tag = "2.17.11",
                                                  Digest = "sha256:21711",
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "3.0.0",
                                                  Digest = "sha256:300",
                                              },
                                              new DockerHubTagData
                                              {
                                                  Tag = "3.1.4",
                                                  Digest = "sha256:314",
                                              },
                                          ]);

        Assert.AreEqual(UpdateEvaluationStatus.UpToDate,
                        evaluation.Status,
                        "A major upgrade must not be recommended when the release dates are unknown");
        Assert.AreEqual("Tag '3.1.4' introduces a new major version and is not recommended yet",
                        evaluation.Details,
                        "The details must name the major version that is held back");
    }

    #endregion // Methods

    #region Static methods

    /// <summary>
    /// Create an update detection service with the default scanning options
    /// </summary>
    /// <returns>Update detection service</returns>
    private static UpdateDetectionService CreateService()
    {
        return CreateService(new ScanningOptions());
    }

    /// <summary>
    /// Create an update detection service with the given scanning options
    /// </summary>
    /// <param name="scanningOptions">Scanning options</param>
    /// <returns>Update detection service</returns>
    private static UpdateDetectionService CreateService(ScanningOptions scanningOptions)
    {
        var options = new DockerUpdateGuardOptions
                      {
                          Scanning = scanningOptions,
                      };

        return new UpdateDetectionService(new TestOptionsMonitor<DockerUpdateGuardOptions>(options));
    }

    /// <summary>
    /// Create a publication timestamp relative to the current time
    /// </summary>
    /// <param name="offsetDays">Offset in days</param>
    /// <returns>Publication timestamp</returns>
    private static DateTimeOffset CreateRelativeTimestamp(int offsetDays)
    {
        return DateTimeOffset.UtcNow.AddDays(offsetDays);
    }

    #endregion // Static methods
}