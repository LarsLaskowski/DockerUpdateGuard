using System.Text.Json;

using DockerUpdateGuard.DockerHub;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DockerHubTagData"/>
/// </summary>
[TestClass]
public class DockerHubTagDataTests
{
    #region Methods

    /// <summary>
    /// Verify the serialized tag data omits the platform digests
    /// </summary>
    [TestMethod]
    [TestCategory("AC9")]
    public void DockerHubTagDataSerializeOmitsPlatformDigests()
    {
        var data = new DockerHubTagData
                   {
                       Tag = "latest",
                       Digest = "sha256:index",
                       PlatformDigests = ["sha256:platform"],
                   };

        var json = JsonSerializer.Serialize(data);

        Assert.DoesNotContain("PlatformDigests", json, StringComparison.OrdinalIgnoreCase, "The platform digests must not be serialized");
        Assert.DoesNotContain("sha256:platform", json, StringComparison.Ordinal, "No platform digest value may be serialized");
        Assert.Contains("sha256:index", json, StringComparison.Ordinal, "The top-level digest must still be serialized");
    }

    #endregion // Methods
}