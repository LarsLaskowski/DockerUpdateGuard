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
        Assert.HasCount(1,
                        evaluation.Candidates,
                        "Digest-only updates must include the latest current-tag candidate");
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

    #endregion // Methods
}