using DockerUpdateGuard.Images.Data;
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

    /// <summary>
    /// Verify hosts of the reference grammar are valid registries
    /// </summary>
    [TestMethod]
    public void RegistryReferenceValidatorIsValidRegistryWhenHostMatchesGrammarReturnsTrue()
    {
        string[] registries = ["docker.io", "localhost:5000", "registry.example.com:443", "[::1]:5000", new string('a', RegistryReferenceValidator.MaxRegistryLength)];

        foreach (var registry in registries)
        {
            Assert.IsTrue(RegistryReferenceValidator.IsValidRegistry(registry),
                          $"A host matching the reference grammar must be a valid registry (length {registry.Length}): {registry}");
        }
    }

    /// <summary>
    /// Verify strings outside the registry grammar are rejected
    /// </summary>
    [TestMethod]
    public void RegistryReferenceValidatorIsValidRegistryWhenInvalidReturnsFalse()
    {
        string?[] registries = [
                                   null,
                                   string.Empty,
                                   "https:",
                                   "reg istry.example",
                                   "under_score.example",
                                   "-lead.example",
                                   "trail.example.",
                                   "host:port",
                                   new string('a', RegistryReferenceValidator.MaxRegistryLength + 1),
                               ];

        foreach (var registry in registries)
        {
            Assert.IsFalse(RegistryReferenceValidator.IsValidRegistry(registry),
                           $"A string outside the registry grammar must be invalid: '{registry}' (length {registry?.Length})");
        }
    }

    /// <summary>
    /// Verify paths of the repository grammar are valid repositories
    /// </summary>
    [TestMethod]
    public void RegistryReferenceValidatorIsValidRepositoryWhenPathMatchesGrammarReturnsTrue()
    {
        string[] repositories = ["library/nginx", "a__b/c-d.e", "a---b", new string('a', RegistryReferenceValidator.MaxRepositoryLength)];

        foreach (var repository in repositories)
        {
            Assert.IsTrue(RegistryReferenceValidator.IsValidRepository(repository),
                          $"A path matching the repository grammar must be valid (length {repository.Length}): {repository}");
        }
    }

    /// <summary>
    /// Verify strings outside the repository grammar, dot segments included, are rejected
    /// </summary>
    [TestMethod]
    public void RegistryReferenceValidatorIsValidRepositoryWhenInvalidReturnsFalse()
    {
        string?[] repositories = [
                                     null,
                                     string.Empty,
                                     "Library/nginx",
                                     "_a",
                                     "a___b",
                                     "a//b",
                                     "a/",
                                     "a b",
                                     "..",
                                     "a/../b",
                                     "a/./b",
                                     new string('a', RegistryReferenceValidator.MaxRepositoryLength + 1),
                                 ];

        foreach (var repository in repositories)
        {
            Assert.IsFalse(RegistryReferenceValidator.IsValidRepository(repository),
                           $"A string outside the repository grammar must be invalid, dot segments included: '{repository}' (length {repository?.Length})");
        }
    }

    /// <summary>
    /// Verify an invalid registry, repository or tag makes the reference invalid
    /// </summary>
    [TestMethod]
    public void RegistryReferenceValidatorValidReferenceOrNullWhenPartIsInvalidReturnsNull()
    {
        var invalidRegistry = new ImageReference
                              {
                                  Registry = "under_score.example",
                                  Repository = "library/nginx",
                                  Tag = "latest"
                              };
        var invalidRepository = new ImageReference
                                {
                                    Registry = "docker.io",
                                    Repository = "a/../b",
                                    Tag = "latest"
                                };
        var invalidTag = new ImageReference
                         {
                             Registry = "docker.io",
                             Repository = "library/nginx",
                             Tag = new string('t', RegistryReferenceValidator.MaxTagLength + 1)
                         };

        Assert.IsNull(RegistryReferenceValidator.ValidReferenceOrNull(invalidRegistry), "A reference with an invalid registry must be rejected");
        Assert.IsNull(RegistryReferenceValidator.ValidReferenceOrNull(invalidRepository), "A reference with an invalid repository must be rejected");
        Assert.IsNull(RegistryReferenceValidator.ValidReferenceOrNull(invalidTag), "A reference with a tag over 128 characters must be rejected");
    }

    /// <summary>
    /// Verify an invalid digest is removed and the rest of the reference is kept
    /// </summary>
    [TestMethod]
    public void RegistryReferenceValidatorValidReferenceOrNullWhenDigestIsInvalidRemovesDigest()
    {
        var reference = new ImageReference
                        {
                            Registry = "docker.io",
                            Repository = "library/nginx",
                            Tag = "1.0",
                            Digest = "sha256:ab cd"
                        };

        var result = RegistryReferenceValidator.ValidReferenceOrNull(reference);

        Assert.IsNotNull(result, "An invalid digest must not make the reference invalid");
        Assert.AreSame(reference, result, "The given instance must be returned");
        Assert.IsNull(result.Digest, "A digest outside the digest grammar must be removed");
        Assert.AreEqual("docker.io/library/nginx:1.0", result.FullReference, "Registry, repository and tag must be kept");
    }

    /// <summary>
    /// Verify a valid reference is returned with all its parts
    /// </summary>
    [TestMethod]
    public void RegistryReferenceValidatorValidReferenceOrNullWhenValidKeepsAllParts()
    {
        var digest = "sha256:" + new string('a', 64);
        var reference = new ImageReference
                        {
                            Registry = "localhost:5000",
                            Repository = "a/b-c",
                            Tag = "v1",
                            Digest = digest
                        };

        var result = RegistryReferenceValidator.ValidReferenceOrNull(reference);

        Assert.IsNotNull(result, "A valid reference must be returned");
        Assert.AreEqual("localhost:5000", result.Registry, "The registry must be kept");
        Assert.AreEqual("a/b-c", result.Repository, "The repository must be kept");
        Assert.AreEqual("v1", result.Tag, "The tag must be kept");
        Assert.AreEqual(digest, result.Digest, "A valid digest must be kept");
    }

    /// <summary>
    /// Verify a path with a segment equal to '.' or '..' is reported as a dot segment
    /// </summary>
    /// <param name="path">Path to check</param>
    [TestMethod]
    [DataRow(".")]
    [DataRow("..")]
    [DataRow("a/.")]
    [DataRow("a/../b")]
    [DataRow("./a")]
    [DataRow("a//..")]
    public void RegistryReferenceValidatorHasDotSegmentDotSegmentReturnsTrue(string path)
    {
        Assert.IsTrue(RegistryReferenceValidator.HasDotSegment(path),
                      $"A path with a '.' or '..' segment must be reported: {path}");
    }

    /// <summary>
    /// Verify a path without a segment equal to '.' or '..' is not reported as a dot segment
    /// </summary>
    /// <param name="path">Path to check</param>
    [TestMethod]
    [DataRow("a")]
    [DataRow("a.b/c")]
    [DataRow("...")]
    [DataRow(".a")]
    [DataRow("a.")]
    [DataRow("..a")]
    [DataRow("%2E%2E")]
    [DataRow("a//b")]
    [DataRow("")]
    public void RegistryReferenceValidatorHasDotSegmentNoDotSegmentReturnsFalse(string path)
    {
        Assert.IsFalse(RegistryReferenceValidator.HasDotSegment(path),
                       $"A path without a '.' or '..' segment must not be reported: {path}");
    }

    #endregion // Methods
}