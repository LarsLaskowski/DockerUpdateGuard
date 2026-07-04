using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="VersionTagResolutionHelper"/>
/// </summary>
[TestClass]
public class VersionTagResolutionHelperTests
{
    #region Methods

    /// <summary>
    /// Verify latest alias resolution only uses strict numeric version tags
    /// </summary>
    [TestMethod]
    public void VersionTagResolutionHelperResolveAliasVersionTagIgnoresNonNumericSuffixTags()
    {
        var resolvedTag = VersionTagResolutionHelper.ResolveAliasVersionTag("latest",
                                                                            "sha256:current",
                                                                            [
                                                                                new VersionTagCandidateData
                                                                                {
                                                                                    Tag = "latest",
                                                                                    Digest = "sha256:current",
                                                                                },
                                                                                new VersionTagCandidateData
                                                                                {
                                                                                    Tag = "1.2-alpine",
                                                                                    Digest = "sha256:current",
                                                                                },
                                                                                new VersionTagCandidateData
                                                                                {
                                                                                    Tag = "1.2.3",
                                                                                    Digest = "sha256:current",
                                                                                },
                                                                            ]);

        Assert.AreEqual("1.2.3",
                        resolvedTag,
                        "Alias resolution must ignore non-numeric suffix tags and only use strict numeric version tags");
    }

    /// <summary>
    /// Verify display version tags include supported year-prefixed tags
    /// </summary>
    [TestMethod]
    public void VersionTagResolutionHelperResolveDisplayVersionTagReturnsYearPrefixedTag()
    {
        var displayTag = VersionTagResolutionHelper.ResolveDisplayVersionTag("2019-release42-build7",
                                                                             "sha256:update",
                                                                             []);

        Assert.AreEqual("2019-release42-build7",
                        displayTag,
                        "Display-version resolution must keep supported year-prefixed tags");
    }

    /// <summary>
    /// Verify MCR aliases resolve to exact tags from the same variant family
    /// </summary>
    [TestMethod]
    public void VersionTagResolutionHelperResolveAliasVersionTagKeepsSameMcrVariantFamily()
    {
        var resolvedTag = VersionTagResolutionHelper.ResolveAliasVersionTag("10.0-alpine",
                                                                            "sha256:current",
                                                                            [
                                                                                new VersionTagCandidateData
                                                                                {
                                                                                    Tag = "10.0-alpine",
                                                                                    Digest = "sha256:current",
                                                                                },
                                                                                new VersionTagCandidateData
                                                                                {
                                                                                    Tag = "10.0.7-alpine3.23",
                                                                                    Digest = "sha256:current",
                                                                                    PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                                                                },
                                                                                new VersionTagCandidateData
                                                                                {
                                                                                    Tag = "10.0.7-jammy",
                                                                                    Digest = "sha256:current",
                                                                                    PublishedAtUtc = new DateTimeOffset(2025, 06, 03, 12, 00, 00, TimeSpan.Zero),
                                                                                },
                                                                            ]);

        Assert.AreEqual("10.0.7-alpine3.23",
                        resolvedTag,
                        "Alias resolution must keep MCR tags within the same variant family when multiple digest-matching exact tags exist");
    }

    /// <summary>
    /// Verify alias resolution prefers digest-matching tags from the running variant family
    /// </summary>
    [TestMethod]
    public void VersionTagResolutionHelperResolveAliasVersionTagPrefersPreferredVariantFamilyCandidate()
    {
        var resolvedTag = VersionTagResolutionHelper.ResolveAliasVersionTag("latest",
                                                                            "sha256:update",
                                                                            [
                                                                                new VersionTagCandidateData
                                                                                {
                                                                                    Tag = "latest",
                                                                                    Digest = "sha256:update",
                                                                                    PublishedAtUtc = new DateTimeOffset(2026, 07, 03, 12, 00, 20, TimeSpan.Zero),
                                                                                },
                                                                                new VersionTagCandidateData
                                                                                {
                                                                                    Tag = "v2.7.6-ls352",
                                                                                    Digest = "sha256:update",
                                                                                    PublishedAtUtc = new DateTimeOffset(2026, 07, 03, 12, 00, 00, TimeSpan.Zero),
                                                                                },
                                                                                new VersionTagCandidateData
                                                                                {
                                                                                    Tag = "2.7.6",
                                                                                    Digest = "sha256:update",
                                                                                    PublishedAtUtc = new DateTimeOffset(2026, 07, 03, 12, 00, 10, TimeSpan.Zero),
                                                                                },
                                                                            ],
                                                                            "v2.7.6-ls350");

        Assert.AreEqual("v2.7.6-ls352",
                        resolvedTag,
                        "Alias resolution must prefer the digest-matching tag from the running variant family over a later-published plain alias");
    }

    /// <summary>
    /// Verify alias resolution falls back to publication order when no candidate matches the preferred variant family
    /// </summary>
    [TestMethod]
    public void VersionTagResolutionHelperResolveAliasVersionTagFallsBackWhenPreferredVariantFamilyMissing()
    {
        var resolvedTag = VersionTagResolutionHelper.ResolveAliasVersionTag("latest",
                                                                            "sha256:update",
                                                                            [
                                                                                new VersionTagCandidateData
                                                                                {
                                                                                    Tag = "2.7.5",
                                                                                    Digest = "sha256:update",
                                                                                    PublishedAtUtc = new DateTimeOffset(2026, 07, 03, 12, 00, 00, TimeSpan.Zero),
                                                                                },
                                                                                new VersionTagCandidateData
                                                                                {
                                                                                    Tag = "2.7.6",
                                                                                    Digest = "sha256:update",
                                                                                    PublishedAtUtc = new DateTimeOffset(2026, 07, 03, 12, 00, 10, TimeSpan.Zero),
                                                                                },
                                                                            ],
                                                                            "v2.7.6-ls350");

        Assert.AreEqual("2.7.6",
                        resolvedTag,
                        "Alias resolution must fall back to the latest published version tag when no candidate matches the preferred variant family");
    }

    /// <summary>
    /// Verify pre-release increments order numerically instead of comparing equal
    /// </summary>
    [TestMethod]
    public void VersionTagResolutionHelperTryCompareVersionTagsOrdersPreReleaseIncrements()
    {
        var firstComparison = VersionTagResolutionHelper.TryCompareVersionTags("1.2.3-rc1", "1.2.3-rc2", out var rc1ToRc2);
        var secondComparison = VersionTagResolutionHelper.TryCompareVersionTags("1.2.3-rc2", "1.2.3-rc10", out var rc2ToRc10);

        Assert.IsTrue(firstComparison, "Pre-release tags of the same version must be comparable");
        Assert.IsLessThan(0, rc1ToRc2, "'1.2.3-rc1' must order below '1.2.3-rc2'");
        Assert.IsTrue(secondComparison, "Pre-release tags of the same version must be comparable");
        Assert.IsLessThan(0, rc2ToRc10, "'1.2.3-rc2' must order below '1.2.3-rc10' using numeric ordering");
    }

    /// <summary>
    /// Verify the general-availability release ranks above its pre-releases
    /// </summary>
    [TestMethod]
    public void VersionTagResolutionHelperTryCompareVersionTagsRanksReleaseAbovePreRelease()
    {
        var comparable = VersionTagResolutionHelper.TryCompareVersionTags("1.2.3", "1.2.3-rc1", out var comparison);

        Assert.IsTrue(comparable, "A pre-release and its general-availability release must be comparable");
        Assert.IsGreaterThan(0, comparison, "'1.2.3' must order above '1.2.3-rc1'");
    }

    /// <summary>
    /// Verify a pre-release tag is offered an update to its general-availability release
    /// </summary>
    [TestMethod]
    public void VersionTagResolutionHelperIsMatchingVersionLineTagMatchesPreReleaseWithRelease()
    {
        var isMatching = VersionTagResolutionHelper.IsMatchingVersionLineTag("1.2.3-rc1", "1.2.3");

        Assert.IsTrue(isMatching,
                      "A pre-release tag and its general-availability release must belong to the same version line");
    }

    /// <summary>
    /// Verify variant sub-versions of the same family order by their numeric base
    /// </summary>
    [TestMethod]
    public void VersionTagResolutionHelperTryCompareVersionTagsOrdersVariantSubVersions()
    {
        var comparable = VersionTagResolutionHelper.TryCompareVersionTags("1.2.3-alpine3.19", "1.2.3-alpine3.18", out var comparison);

        Assert.IsTrue(comparable, "Tags of the same variant family must be comparable");
        Assert.IsGreaterThan(0, comparison, "'1.2.3-alpine3.19' must order above '1.2.3-alpine3.18'");
    }

    /// <summary>
    /// Verify pre-release detection distinguishes pre-releases, releases and build variants
    /// </summary>
    [TestMethod]
    public void VersionTagResolutionHelperIsPreReleaseVersionTagDetectsPreReleaseTags()
    {
        Assert.IsTrue(VersionTagResolutionHelper.IsPreReleaseVersionTag("1.2.3-rc1"),
                      "'1.2.3-rc1' must be detected as a pre-release tag");
        Assert.IsFalse(VersionTagResolutionHelper.IsPreReleaseVersionTag("1.2.3"),
                       "'1.2.3' must not be detected as a pre-release tag");
        Assert.IsFalse(VersionTagResolutionHelper.IsPreReleaseVersionTag("1.2.3-alpine3.19"),
                       "A build variant such as '1.2.3-alpine3.19' must not be detected as a pre-release tag");
    }

    /// <summary>
    /// Verify a build variant is kept separate from the plain release family
    /// </summary>
    [TestMethod]
    public void VersionTagResolutionHelperTryCompareVersionTagsRejectsCrossVariantComparison()
    {
        var comparable = VersionTagResolutionHelper.TryCompareVersionTags("1.2.3-alpine", "1.2.3", out _);

        Assert.IsFalse(comparable,
                       "A build variant and a plain release must not be treated as the same variant family");
    }

    /// <summary>
    /// Verify an overly long numeric version component is rejected instead of throwing
    /// </summary>
    [TestMethod]
    public void VersionTagResolutionHelperTryParseVersionTagRejectsOverlyLongComponent()
    {
        var parsed = VersionTagResolutionHelper.TryParseVersionTag("99999999999.0.0", out _);

        Assert.IsFalse(parsed, "A version component exceeding Int32 range must not throw and must return false");
    }

    /// <summary>
    /// Verify an overly long numeric version-line component is rejected instead of throwing
    /// </summary>
    [TestMethod]
    public void VersionTagResolutionHelperIsMatchingVersionLineTagRejectsOverlyLongComponent()
    {
        var isMatching = VersionTagResolutionHelper.IsMatchingVersionLineTag("99999999999.0", "1.2.3");

        Assert.IsFalse(isMatching, "A version-line component exceeding Int32 range must not throw and must return false");
    }

    #endregion // Methods
}