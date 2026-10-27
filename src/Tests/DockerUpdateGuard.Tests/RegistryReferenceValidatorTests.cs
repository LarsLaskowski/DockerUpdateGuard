using DockerUpdateGuard.Images.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="RegistryReferenceValidator"/>
/// </summary>
[TestClass]
public class RegistryReferenceValidatorTests
{
    #region Methods

    /// <summary>
    /// Verify tags that match the tag grammar are accepted
    /// </summary>
    [TestMethod]
    public void RegistryReferenceValidatorIsValidTagAcceptsGrammarTags()
    {
        string[] tags = ["latest", "1.2.3", "v1.0-rc_1", "_x", "A", new string('a', 128)];

        foreach (var tag in tags)
        {
            Assert.IsTrue(RegistryReferenceValidator.IsValidTag(tag),
                          $"A tag matching the grammar must be valid (length {tag.Length}): {tag}");
        }
    }

    /// <summary>
    /// Verify strings outside the tag grammar are rejected
    /// </summary>
    [TestMethod]
    public void RegistryReferenceValidatorIsValidTagRejectsInvalidTags()
    {
        string?[] tags = [
                             null,
                             string.Empty,
                             " ",
                             new string('a', 129),
                             ".hidden",
                             "-x",
                             "1.2.3" + (char)0x0A,
                             "1.2.3" + (char)0x0D + (char)0x0A,
                             "1.2 3",
                             "1.2.3+build",
                             "a/b",
                             "a:b",
                             "a@b",
                             "..",
                             "a" + (char)0x00,
                             (char)0xFF11 + ".2.3",
                             "é1",
                             "K",
                         ];

        Assert.IsTrue(tags[6]!.EndsWith('\n'), "The line feed case must contain a line feed");
        Assert.IsTrue(tags[16]!.Contains('é'), "The Unicode letter case must contain the letter");

        foreach (var tag in tags)
        {
            Assert.IsFalse(RegistryReferenceValidator.IsValidTag(tag),
                           $"A string outside the tag grammar must be invalid: '{tag}' (length {tag?.Length})");
        }
    }

    /// <summary>
    /// Verify digests that match the digest grammar are returned unchanged
    /// </summary>
    [TestMethod]
    public void RegistryReferenceValidatorValidDigestOrNullKeepsValidDigests()
    {
        string[] digests = [
                               "sha256:" + new string('a', 64),
                               "sha512:" + new string('0', 128),
                               "sha256:current",
                               "sha256+b64u:LCa0a2j_xo_5m0U8HTBBNBNCLXBkg7-g-YpeiGJm564",
                               "sha256:" + new string('a', 248),
                           ];

        Assert.AreEqual(RegistryReferenceValidator.MaxDigestLength,
                        digests[4].Length,
                        "The longest case must have exactly the maximum digest length");

        foreach (var digest in digests)
        {
            Assert.AreEqual(digest,
                            RegistryReferenceValidator.ValidDigestOrNull(digest),
                            $"A valid digest must be returned unchanged (length {digest.Length})");
        }
    }

    /// <summary>
    /// Verify strings outside the digest grammar become null
    /// </summary>
    [TestMethod]
    public void RegistryReferenceValidatorValidDigestOrNullRejectsInvalidDigests()
    {
        string?[] digests = [
                                null,
                                string.Empty,
                                "sha256",
                                ":abc",
                                "sha256:",
                                "SHA256:abc",
                                "sha256:abc" + (char)0x0A,
                                "sha256:ab c",
                                "sha256:abc/def",
                                "sha256..x:abc",
                                "sha256:" + new string('a', 249),
                                "sha256:" + (char)0xFF41 + "bc",
                            ];

        Assert.AreEqual(RegistryReferenceValidator.MaxDigestLength + 1,
                        digests[10]!.Length,
                        "The oversized case must be one character over the maximum");

        foreach (var digest in digests)
        {
            Assert.IsNull(RegistryReferenceValidator.ValidDigestOrNull(digest),
                          $"A string outside the digest grammar must become null (length {digest?.Length})");
        }
    }

    #endregion // Methods
}