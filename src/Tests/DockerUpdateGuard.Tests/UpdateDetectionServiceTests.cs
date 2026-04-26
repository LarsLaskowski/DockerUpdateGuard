using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images;

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
        var service = new UpdateDetectionService();

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
        var service = new UpdateDetectionService();

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
        CollectionAssert.AreEqual(new[] { "latest", "2.4.1" },
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
        var service = new UpdateDetectionService();

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
    /// Verify non-semantic current tags produce manual review candidates
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceNonSemverTagReturnsNeedsReview()
    {
        var service = new UpdateDetectionService();

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
        CollectionAssert.AreEqual(new[] { "preview", "canary" },
                                  evaluation.Candidates.Select(candidate => candidate.Tag)
                                                       .ToArray(),
                                  "Manual review candidates must exclude the current tag and keep the newest alternatives");
    }

    /// <summary>
    /// Verify latest aliases can resolve the running semantic version from the current digest
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceLatestAliasWithMatchingSemanticDigestReturnsSemanticSuccessor()
    {
        var service = new UpdateDetectionService();

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

        Assert.AreEqual(UpdateEvaluationStatus.UpdateAvailable,
                        evaluation.Status,
                        "A latest alias with a digest-matched semantic version must report a newer semantic successor");
        Assert.AreEqual("2.5.0",
                        evaluation.RecommendedTag,
                        "The highest semantic successor must be recommended once the running digest resolves to a version tag");
        CollectionAssert.AreEqual(new[] { "2.5.0", "2.4.1" },
                                  evaluation.Candidates.Select(candidate => candidate.Tag)
                                                       .ToArray(),
                                  "Candidates must include the newer semantic successor and the resolved current semantic version");
    }

    /// <summary>
    /// Verify stale calendar tags are not recommended ahead of newer published tags
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceLatestAliasIgnoresOlderPublishedCalendarTagAsync()
    {
        var service = new UpdateDetectionService();

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

        Assert.AreEqual(UpdateEvaluationStatus.UpdateAvailable,
                        evaluation.Status,
                        "A newer published semantic tag must still be reported as an available update");
        Assert.AreEqual("2.5.0",
                        evaluation.RecommendedTag,
                        "Older calendar tags must not outrank newer published semantic successors");
        CollectionAssert.AreEquivalent(new[] { "2.5.0", "2.4.1" },
                                       evaluation.Candidates.Select(candidate => candidate.Tag)
                                                            .ToArray(),
                                       "Stale calendar tags must be excluded from the latest alias recommendation set");
    }

    /// <summary>
    /// Verify latest aliases with a matching semantic digest stay up to date when no newer version exists
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceLatestAliasWithMatchingSemanticDigestAndNoSuccessorReturnsUpToDate()
    {
        var service = new UpdateDetectionService();

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
        Assert.AreEqual("The running digest matches version tag '2.4.1'",
                        evaluation.Summary,
                        "The summary must explain which semantic version was resolved from the running digest");
    }

    /// <summary>
    /// Verify latest aliases without semantic matches stay up to date when the registry digest is unchanged
    /// </summary>
    [TestMethod]
    public void UpdateDetectionServiceLatestAliasWithoutSemanticMatchAndUnchangedDigestReturnsUpToDate()
    {
        var service = new UpdateDetectionService();

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

    #endregion // Methods
}