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

    #endregion // Methods
}