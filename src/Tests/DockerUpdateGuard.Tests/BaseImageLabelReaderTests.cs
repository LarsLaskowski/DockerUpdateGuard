using DockerUpdateGuard.Images.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="BaseImageLabelReader"/>
/// </summary>
[TestClass]
public class BaseImageLabelReaderTests
{
    #region Methods

    /// <summary>
    /// Verify a valid name is parsed and its own digest is kept
    /// </summary>
    [TestMethod]
    public void BaseImageLabelReaderReadReferenceWhenNameIsValidReturnsReference()
    {
        var reference = BaseImageLabelReader.ReadReference("mcr.microsoft.com/dotnet/runtime:8.0", "sha256:label");

        Assert.IsNotNull(reference, "A valid label must be parsed");
        Assert.AreEqual("mcr.microsoft.com", reference.Registry, "The registry must be parsed");
        Assert.AreEqual("dotnet/runtime", reference.Repository, "The repository must be parsed");
        Assert.AreEqual("8.0", reference.Tag, "The tag must be parsed");
        Assert.AreEqual("sha256:label", reference.Digest, "Without a digest in the name the digest label must be used");
    }

    /// <summary>
    /// Verify a digest in the name wins over the digest label
    /// </summary>
    [TestMethod]
    public void BaseImageLabelReaderReadReferenceWhenNameHasDigestKeepsNameDigest()
    {
        var reference = BaseImageLabelReader.ReadReference("docker.io/library/debian:12@sha256:own", "sha256:label");

        Assert.IsNotNull(reference, "A valid label must be parsed");
        Assert.AreEqual("sha256:own", reference.Digest, "The digest of the name must win over the digest label");
    }

    /// <summary>
    /// Verify a blank name gives no reference
    /// </summary>
    /// <param name="name">Name label</param>
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void BaseImageLabelReaderReadReferenceWhenNameIsBlankReturnsNull(string? name)
    {
        var reference = BaseImageLabelReader.ReadReference(name, "sha256:label");

        Assert.IsNull(reference, "A blank name must give no reference");
    }

    /// <summary>
    /// Verify a name the parser rejects gives no reference and no exception
    /// </summary>
    /// <param name="name">Name label</param>
    [TestMethod]
    [DataRow(":12")]
    [DataRow("/")]
    public void BaseImageLabelReaderReadReferenceWhenNameCannotBeParsedReturnsNull(string name)
    {
        var reference = BaseImageLabelReader.ReadReference(name, null);

        Assert.IsNull(reference, "A name the parser rejects must give no reference instead of an exception");
    }

    /// <summary>
    /// Verify a reference with an invalid registry, repository or tag gives no reference
    /// </summary>
    [TestMethod]
    public void BaseImageLabelReaderReadReferenceWhenPartIsInvalidReturnsNull()
    {
        string[] names = [
                             $"{new string('a', 201)}.io/library/debian:12",
                             $"docker.io/{new string('a', 256)}:12",
                             $"docker.io/library/debian:{new string('t', 129)}",
                             "debian 12",
                         ];

        foreach (var name in names)
        {
            Assert.IsNull(BaseImageLabelReader.ReadReference(name, null),
                          $"A name with an invalid part must give no reference (length {name.Length})");
        }
    }

    /// <summary>
    /// Verify an invalid digest is dropped and the reference is kept
    /// </summary>
    [TestMethod]
    public void BaseImageLabelReaderReadReferenceWhenDigestIsInvalidReturnsReferenceWithoutDigest()
    {
        var fromLabel = BaseImageLabelReader.ReadReference("docker.io/library/debian:12", "sha256:ab cd");
        var fromName = BaseImageLabelReader.ReadReference("docker.io/library/debian:12@sha256:ab cd", null);

        Assert.IsNotNull(fromLabel, "An invalid digest label must not drop the reference");
        Assert.IsNull(fromLabel.Digest, "A digest label outside the grammar must become none");
        Assert.IsNotNull(fromName, "An invalid digest in the name must not drop the reference");
        Assert.IsNull(fromName.Digest, "A digest in the name outside the grammar must become none");
    }

    #endregion // Methods
}