using DockerUpdateGuard.UI;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="TagCandidateViewHelper"/>
/// </summary>
[TestClass]
public class TagCandidateViewHelperTests
{
    #region Methods

    /// <summary>
    /// Verify the runtime filter keeps a later-year calendar tag and drops an older one
    /// </summary>
    [TestMethod]
    [TestCategory("AC6")]
    public void TagCandidateViewHelperFilterRuntimeAvailableTagCandidatesKeepsLaterYearCalendarTag()
    {
        var candidates = TagCandidateViewHelper.FilterRuntimeAvailableTagCandidates([CreateCandidate("2025-01-05"), CreateCandidate("2024-11-01")],
                                                                                    "2024-12-15",
                                                                                    null,
                                                                                    null);

        Assert.AreSequenceEqual(["2025-01-05"],
                                candidates.Select(candidate => candidate.Tag)
                                          .ToArray(),
                                "A calendar tag of a later year must stay visible and an older calendar tag must be dropped");
    }

    /// <summary>
    /// Verify the runtime filter drops a later-year product tag and keeps a same-year successor
    /// </summary>
    [TestMethod]
    [TestCategory("AC6")]
    public void TagCandidateViewHelperFilterRuntimeAvailableTagCandidatesDropsLaterYearProductTag()
    {
        var candidates = TagCandidateViewHelper.FilterRuntimeAvailableTagCandidates([CreateCandidate("2022-CU1-ubuntu-20.04"), CreateCandidate("2019-CU33-ubuntu-20.04")],
                                                                                    "2019-CU32-ubuntu-20.04",
                                                                                    null,
                                                                                    null);

        Assert.AreSequenceEqual(["2019-CU33-ubuntu-20.04"],
                                candidates.Select(candidate => candidate.Tag)
                                          .ToArray(),
                                "A product-year tag of a later year must be dropped while the same-year successor stays");
    }

    #endregion // Methods

    #region Static methods

    /// <summary>
    /// Create a tag candidate
    /// </summary>
    /// <param name="tag">Tag</param>
    /// <returns>Candidate</returns>
    private static TagCandidateViewData CreateCandidate(string tag)
    {
        return new TagCandidateViewData
               {
                   Tag = tag,
                   Digest = "sha256:" + tag,
               };
    }

    #endregion // Static methods
}