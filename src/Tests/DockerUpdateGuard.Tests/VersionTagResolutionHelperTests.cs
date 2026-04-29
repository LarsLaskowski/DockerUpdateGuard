using DockerUpdateGuard.Images;

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

    #endregion // Methods
}