using DockerUpdateGuard.Images;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ImageReferenceParser"/>
/// </summary>
[TestClass]
public class ImageReferenceParserTests
{
    #region Methods

    /// <summary>
    /// Verify wrapped foreign registry references are normalized back to their source registry
    /// </summary>
    [TestMethod]
    public void ImageReferenceParserParseWrappedMicrosoftRegistryReferenceNormalizesRegistry()
    {
        var parser = new ImageReferenceParser();

        var imageReference = parser.Parse("docker.io/mcr.microsoft.com/mssql/server:2019-CU32-GDR7-ubuntu-20.04");

        Assert.AreEqual("mcr.microsoft.com",
                        imageReference.Registry,
                        "Wrapped Microsoft registry references must be normalized back to mcr.microsoft.com");
        Assert.AreEqual("mssql/server",
                        imageReference.Repository,
                        "Wrapped Microsoft registry references must keep the repository after removing the docker.io wrapper");
        Assert.AreEqual("2019-CU32-GDR7-ubuntu-20.04",
                        imageReference.Tag,
                        "Wrapped Microsoft registry references must keep their original tag");
    }

    #endregion // Methods
}