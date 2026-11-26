using System.Text.Json;

using DockerUpdateGuard.Images.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ManifestPlatformSelector"/>
/// </summary>
[TestClass]
public class ManifestPlatformSelectorTests
{
    #region Constants

    /// <summary>
    /// Image index of a BuildKit build with attestation manifests (real sample of ghcr.io/home-assistant/home-assistant:stable)
    /// </summary>
    private const string BuildKitIndex = """
                                         {
                                           "schemaVersion": 2,
                                           "mediaType": "application/vnd.oci.image.index.v1+json",
                                           "manifests": [
                                             {
                                               "mediaType": "application/vnd.oci.image.manifest.v1+json",
                                               "digest": "sha256:fbbd521bd989893b6c350877d17e1eb1126b6a1bfb715c28e6945f1c82ae0093",
                                               "size": 4488,
                                               "platform": {
                                                 "architecture": "amd64",
                                                 "os": "linux"
                                               }
                                             },
                                             {
                                               "mediaType": "application/vnd.oci.image.manifest.v1+json",
                                               "digest": "sha256:74ce6925ef89e320a513225ca1ceb74ee90879400cd9fc89303e7bf420ea630f",
                                               "size": 838,
                                               "annotations": {
                                                 "vnd.docker.reference.digest": "sha256:fbbd521bd989893b6c350877d17e1eb1126b6a1bfb715c28e6945f1c82ae0093",
                                                 "vnd.docker.reference.type": "attestation-manifest"
                                               },
                                               "platform": {
                                                 "architecture": "unknown",
                                                 "os": "unknown"
                                               }
                                             },
                                             {
                                               "mediaType": "application/vnd.oci.image.manifest.v1+json",
                                               "digest": "sha256:ee500645d61b5483d0b05eebd4f2de22c390618ec178df0e1114a77d40d6d650",
                                               "size": 4488,
                                               "platform": {
                                                 "architecture": "arm64",
                                                 "os": "linux"
                                               }
                                             },
                                             {
                                               "mediaType": "application/vnd.oci.image.manifest.v1+json",
                                               "digest": "sha256:9902d80d91773d0f4147cc401bb19ea03637c1e80a5f6f4e8334be1c493e2623",
                                               "size": 838,
                                               "annotations": {
                                                 "vnd.docker.reference.digest": "sha256:ee500645d61b5483d0b05eebd4f2de22c390618ec178df0e1114a77d40d6d650",
                                                 "vnd.docker.reference.type": "attestation-manifest"
                                               },
                                               "platform": {
                                                 "architecture": "unknown",
                                                 "os": "unknown"
                                               }
                                             }
                                           ]
                                         }
                                         """;

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Verify an attestation entry in front of the real platform entry is skipped
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void ManifestPlatformSelectorSelectManifestDigestAttestationFirstReturnsLinuxDigest()
    {
        var digest = Select("""
                            [
                              { "digest": "sha256:attestation", "platform": { "os": "unknown", "architecture": "unknown" } },
                              { "digest": "sha256:amd64", "platform": { "os": "linux", "architecture": "amd64" } }
                            ]
                            """,
                            null,
                            null);

        Assert.AreEqual("sha256:amd64", digest, "An attestation entry must never be chosen while a real platform entry exists");
    }

    /// <summary>
    /// Verify without a requested platform the first linux entry wins over earlier entries of another operating system
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void ManifestPlatformSelectorSelectManifestDigestNoPlatformPrefersLinux()
    {
        var digest = Select("""
                            [
                              { "digest": "sha256:windows", "platform": { "os": "windows", "architecture": "amd64" } },
                              { "digest": "sha256:arm64", "platform": { "os": "linux", "architecture": "arm64" } }
                            ]
                            """,
                            null,
                            null);

        Assert.AreEqual("sha256:arm64", digest, "Without a requested platform the first linux entry must be chosen");
    }

    /// <summary>
    /// Verify without a linux entry the first usable entry is chosen, an entry without platform included
    /// </summary>
    /// <param name="manifestsJson">Manifests array</param>
    /// <param name="expectedDigest">Expected digest</param>
    [TestMethod]
    [TestCategory("AC1")]
    [DataRow("""[ { "digest": "sha256:windows", "platform": { "os": "windows", "architecture": "amd64" } }, { "digest": "sha256:freebsd", "platform": { "os": "freebsd", "architecture": "arm" } } ]""", "sha256:windows")]
    [DataRow("""[ { "digest": "sha256:unknown", "platform": { "os": "unknown", "architecture": "unknown" } }, { "digest": "sha256:plain" } ]""", "sha256:plain")]
    public void ManifestPlatformSelectorSelectManifestDigestWithoutLinuxReturnsFirstUsableEntry(string manifestsJson, string expectedDigest)
    {
        var digest = Select(manifestsJson, null, null);

        Assert.AreEqual(expectedDigest, digest, "Without a linux entry the first usable entry must be chosen");
    }

    /// <summary>
    /// Verify an entry without a usable digest is skipped
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void ManifestPlatformSelectorSelectManifestDigestEntryWithoutDigestIsSkipped()
    {
        var digest = Select("""
                            [
                              { "platform": { "os": "linux", "architecture": "amd64" } },
                              { "digest": "   ", "platform": { "os": "linux", "architecture": "amd64" } },
                              { "digest": 5, "platform": { "os": "linux", "architecture": "amd64" } },
                              { "digest": "sha256:s390x", "platform": { "os": "linux", "architecture": "s390x" } }
                            ]
                            """,
                            "linux",
                            "amd64");

        Assert.AreEqual("sha256:s390x", digest, "Entries without a non-blank string digest must never be chosen, not even on an exact match");
    }

    /// <summary>
    /// Verify the first exact match of the requested operating system and architecture is chosen
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void ManifestPlatformSelectorSelectManifestDigestRequestedPlatformReturnsExactMatch()
    {
        var digest = Select("""
                            [
                              { "digest": "sha256:linux", "platform": { "os": "linux", "architecture": "amd64" } },
                              { "digest": "sha256:windows", "platform": { "os": "windows", "architecture": "amd64" } },
                              { "digest": "sha256:windows-again", "platform": { "os": "windows", "architecture": "amd64" } }
                            ]
                            """,
                            "windows",
                            "amd64");

        Assert.AreEqual("sha256:windows", digest, "The first entry with the requested operating system and architecture must be chosen");
    }

    /// <summary>
    /// Verify without an exact match the first entry with the requested architecture is chosen
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void ManifestPlatformSelectorSelectManifestDigestRequestedArchitectureReturnsArchitectureMatch()
    {
        var digest = Select("""
                            [
                              { "digest": "sha256:linux-amd64", "platform": { "os": "linux", "architecture": "amd64" } },
                              { "digest": "sha256:windows-arm64", "platform": { "os": "windows", "architecture": "arm64" } },
                              { "digest": "sha256:linux-arm64", "platform": { "os": "linux", "architecture": "arm64" } }
                            ]
                            """,
                            "freebsd",
                            "arm64");

        Assert.AreEqual("sha256:windows-arm64", digest, "Without an exact match the first entry with the requested architecture must be chosen");
    }

    /// <summary>
    /// Verify a requested platform no entry matches falls back to the linux entry, never to an unknown one
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void ManifestPlatformSelectorSelectManifestDigestUnmatchedPlatformSkipsUnknownEntries()
    {
        var digest = Select("""
                            [
                              { "digest": "sha256:attestation", "platform": { "os": "unknown", "architecture": "unknown" } },
                              { "digest": "sha256:amd64", "platform": { "os": "linux", "architecture": "amd64" } }
                            ]
                            """,
                            "linux",
                            "s390x");

        Assert.AreEqual("sha256:amd64", digest, "An unmatched platform must fall back to the linux entry and skip the attestation");
    }

    /// <summary>
    /// Verify a requested unknown platform never selects an unknown entry
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void ManifestPlatformSelectorSelectManifestDigestRequestedUnknownPlatformIsNeverSelected()
    {
        var digest = Select("""
                            [
                              { "digest": "sha256:attestation", "platform": { "os": "unknown", "architecture": "unknown" } },
                              { "digest": "sha256:amd64", "platform": { "os": "linux", "architecture": "amd64" } }
                            ]
                            """,
                            "unknown",
                            "unknown");

        Assert.AreEqual("sha256:amd64", digest, "A request for the unknown platform must not select the attestation entry");
    }

    /// <summary>
    /// Verify the requested values are trimmed and every comparison ignores the case
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void ManifestPlatformSelectorSelectManifestDigestPaddedRequestMatchesCaseInsensitively()
    {
        var digest = Select("""
                            [
                              { "digest": "sha256:amd64", "platform": { "os": "linux", "architecture": "amd64" } },
                              { "digest": "sha256:arm64", "platform": { "os": "Linux", "architecture": "ARM64" } }
                            ]
                            """,
                            " LINUX ",
                            " arm64 ");

        Assert.AreEqual("sha256:arm64", digest, "Padded requested values must match case-insensitively");
    }

    /// <summary>
    /// Verify an index without a usable entry answers null
    /// </summary>
    /// <param name="manifestsJson">Manifests array</param>
    [TestMethod]
    [TestCategory("AC1")]
    [DataRow("""[ { "digest": "sha256:a", "platform": { "os": "unknown", "architecture": "unknown" } }, { "digest": "sha256:b", "platform": { "os": "linux", "architecture": "Unknown" } }, { "digest": "sha256:c", "platform": { "os": "UNKNOWN", "architecture": "amd64" } } ]""")]
    [DataRow("""[ { "platform": { "os": "linux", "architecture": "amd64" } }, { "digest": "", "platform": { "os": "linux", "architecture": "amd64" } } ]""")]
    [DataRow("[]")]
    public void ManifestPlatformSelectorSelectManifestDigestOnlyUnknownEntriesReturnsNull(string manifestsJson)
    {
        var digest = Select(manifestsJson, "linux", "amd64");

        Assert.IsNull(digest, "An index without a usable entry must not answer a digest");
    }

    /// <summary>
    /// Verify the real BuildKit index selects the platform manifests and never an attestation
    /// </summary>
    /// <param name="operatingSystem">Requested operating system</param>
    /// <param name="architecture">Requested architecture</param>
    /// <param name="expectedDigest">Expected digest</param>
    [TestMethod]
    [TestCategory("AC1")]
    [DataRow(null, null, "sha256:fbbd521bd989893b6c350877d17e1eb1126b6a1bfb715c28e6945f1c82ae0093")]
    [DataRow("linux", "arm64", "sha256:ee500645d61b5483d0b05eebd4f2de22c390618ec178df0e1114a77d40d6d650")]
    [DataRow("linux", "s390x", "sha256:fbbd521bd989893b6c350877d17e1eb1126b6a1bfb715c28e6945f1c82ae0093")]
    public void ManifestPlatformSelectorSelectManifestDigestRealBuildKitIndexReturnsLinuxAmd64(string? operatingSystem, string? architecture, string expectedDigest)
    {
        using var document = JsonDocument.Parse(BuildKitIndex);

        var digest = ManifestPlatformSelector.SelectManifestDigest(document.RootElement.GetProperty("manifests"), operatingSystem, architecture);

        Assert.AreEqual(expectedDigest, digest, "The real index must select a platform manifest, never an attestation manifest");
    }

    #endregion // Methods

    #region Static methods

    /// <summary>
    /// Select a manifest digest from a manifests array
    /// </summary>
    /// <param name="manifestsJson">Manifests array</param>
    /// <param name="operatingSystem">Requested operating system</param>
    /// <param name="architecture">Requested architecture</param>
    /// <returns>Selected digest or null</returns>
    private static string? Select(string manifestsJson, string? operatingSystem, string? architecture)
    {
        using var document = JsonDocument.Parse(manifestsJson);

        return ManifestPlatformSelector.SelectManifestDigest(document.RootElement, operatingSystem, architecture);
    }

    #endregion // Static methods
}