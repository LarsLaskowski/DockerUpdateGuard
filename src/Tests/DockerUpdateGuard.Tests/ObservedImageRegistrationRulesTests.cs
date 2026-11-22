using DockerUpdateGuard.Images;
using DockerUpdateGuard.Images.Data;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ObservedImageRegistrationRules"/>
/// </summary>
[TestClass]
public class ObservedImageRegistrationRulesTests
{
    #region Static methods

    /// <summary>
    /// Create a parsed image reference whose parts are all within their limits
    /// </summary>
    /// <returns>Image reference</returns>
    private static ImageReference CreateReference()
    {
        return new ImageReference
               {
                   Registry = "docker.io",
                   Repository = "company/api",
                   Tag = "1.0.0",
                   Digest = "sha256:abc",
               };
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Verify a registry over its limit is reported
    /// </summary>
    [TestMethod]
    [TestCategory("AC2")]
    public void ObservedImageRegistrationRulesGetPartsErrorRegistryOverLimitReturnsRegistryMessage()
    {
        var reference = CreateReference();

        reference.Registry = new string('r', 201);

        Assert.AreEqual("The registry must not be longer than 200 characters",
                        ObservedImageRegistrationRules.GetPartsError(reference),
                        "A registry of 201 characters must be reported with the registry text");
    }

    /// <summary>
    /// Verify a repository over its limit is reported
    /// </summary>
    [TestMethod]
    [TestCategory("AC2")]
    public void ObservedImageRegistrationRulesGetPartsErrorRepositoryOverLimitReturnsRepositoryMessage()
    {
        var reference = CreateReference();

        reference.Repository = new string('p', 301);

        Assert.AreEqual("The repository must not be longer than 300 characters",
                        ObservedImageRegistrationRules.GetPartsError(reference),
                        "A repository of 301 characters must be reported with the repository text");
    }

    /// <summary>
    /// Verify a tag over its limit is reported
    /// </summary>
    [TestMethod]
    [TestCategory("AC2")]
    public void ObservedImageRegistrationRulesGetPartsErrorTagOverLimitReturnsTagMessage()
    {
        var reference = CreateReference();

        reference.Tag = new string('t', 201);

        Assert.AreEqual("The tag must not be longer than 200 characters",
                        ObservedImageRegistrationRules.GetPartsError(reference),
                        "A tag of 201 characters must be reported with the tag text");
    }

    /// <summary>
    /// Verify a digest over its limit is reported
    /// </summary>
    [TestMethod]
    [TestCategory("AC2")]
    public void ObservedImageRegistrationRulesGetPartsErrorDigestOverLimitReturnsDigestMessage()
    {
        var reference = CreateReference();

        reference.Digest = new string('d', 256);

        Assert.AreEqual("The digest must not be longer than 255 characters",
                        ObservedImageRegistrationRules.GetPartsError(reference),
                        "A digest of 256 characters must be reported with the digest text");
    }

    /// <summary>
    /// Verify parts at exactly their limits and an absent digest are accepted
    /// </summary>
    [TestMethod]
    [TestCategory("AC2")]
    public void ObservedImageRegistrationRulesGetPartsErrorPartsAtLimitReturnNull()
    {
        var reference = new ImageReference
                        {
                            Registry = new string('r', 200),
                            Repository = new string('p', 300),
                            Tag = new string('t', 200),
                            Digest = new string('d', 255),
                        };

        Assert.IsNull(ObservedImageRegistrationRules.GetPartsError(reference), "Parts at exactly their limits must pass");

        reference.Digest = null;

        Assert.IsNull(ObservedImageRegistrationRules.GetPartsError(reference), "An absent digest must pass");
    }

    /// <summary>
    /// Verify only the first over-long part in the order registry, repository, tag, digest is reported
    /// </summary>
    [TestMethod]
    [TestCategory("AC2")]
    public void ObservedImageRegistrationRulesGetPartsErrorSeveralPartsOverLimitReturnsFirstMessage()
    {
        var reference = new ImageReference
                        {
                            Registry = new string('r', 201),
                            Repository = new string('p', 301),
                            Tag = new string('t', 201),
                            Digest = new string('d', 256),
                        };

        Assert.AreEqual("The registry must not be longer than 200 characters",
                        ObservedImageRegistrationRules.GetPartsError(reference),
                        "The registry must be reported first");

        reference.Registry = "docker.io";

        Assert.AreEqual("The repository must not be longer than 300 characters",
                        ObservedImageRegistrationRules.GetPartsError(reference),
                        "The repository must be reported before the tag and the digest");

        reference.Repository = "company/api";

        Assert.AreEqual("The tag must not be longer than 200 characters",
                        ObservedImageRegistrationRules.GetPartsError(reference),
                        "The tag must be reported before the digest");
    }

    /// <summary>
    /// Verify the library prefix of a Docker Hub repository counts towards the limit
    /// </summary>
    [TestMethod]
    [TestCategory("AC3")]
    public void ObservedImageRegistrationRulesGetReferenceErrorDockerHubRepositoryWithLibraryPrefixOverLimitReturnsRepositoryMessage()
    {
        var error = ObservedImageRegistrationRules.GetReferenceError(new string('a', 293));

        Assert.AreEqual("The repository must not be longer than 300 characters",
                        error,
                        "A single-segment name of 293 characters is stored as 301 with library/ and must be rejected");
    }

    /// <summary>
    /// Verify a Docker Hub repository that is exactly at the limit with its library prefix passes
    /// </summary>
    [TestMethod]
    [TestCategory("AC3")]
    public void ObservedImageRegistrationRulesGetReferenceErrorDockerHubRepositoryWithLibraryPrefixAtLimitReturnsNull()
    {
        var error = ObservedImageRegistrationRules.GetReferenceError(new string('a', 292));

        Assert.IsNull(error, "A single-segment name of 292 characters is stored as 300 with library/ and must pass");
    }

    /// <summary>
    /// Verify whitespace around the tag and the digest and the separator do not count
    /// </summary>
    [TestMethod]
    [TestCategory("AC3")]
    public void ObservedImageRegistrationRulesGetReferenceErrorPaddedTagAndDigestAtLimitReturnsNull()
    {
        var reference = $"docker.io/company/api:  {new string('t', 200)}  @  {new string('d', 255)}  ";

        Assert.IsNull(ObservedImageRegistrationRules.GetReferenceError(reference),
                      "A tag of 200 and a digest of 255 characters must pass whatever whitespace and separator surround them");
    }

    /// <summary>
    /// Verify a raw reference with an over-long registry is parsed and reported
    /// </summary>
    [TestMethod]
    [TestCategory("AC2")]
    public void ObservedImageRegistrationRulesGetReferenceErrorRawRegistryOverLimitReturnsRegistryMessage()
    {
        var reference = $"{new string('r', 198)}.io/company/api:1.0.0";

        Assert.AreEqual("The registry must not be longer than 200 characters",
                        ObservedImageRegistrationRules.GetReferenceError(reference),
                        "A registry of 201 characters in a raw reference must be reported");
    }

    /// <summary>
    /// Verify a reference the parser rejects and a blank reference give no length message
    /// </summary>
    [TestMethod]
    [TestCategory("AC6")]
    public void ObservedImageRegistrationRulesGetReferenceErrorReferenceWithoutRepositoryReturnsNull()
    {
        Assert.IsNull(ObservedImageRegistrationRules.GetReferenceError(":latest"),
                      "A reference without a repository name must be left to the service");
        Assert.IsNull(ObservedImageRegistrationRules.GetReferenceError("   "),
                      "A blank reference must be left to the required check");
        Assert.IsNull(ObservedImageRegistrationRules.GetReferenceError(null),
                      "A missing reference must be left to the required check");
    }

    #endregion // Methods
}