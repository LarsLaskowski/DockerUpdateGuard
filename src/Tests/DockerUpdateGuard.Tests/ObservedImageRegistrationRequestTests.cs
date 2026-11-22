using System.ComponentModel.DataAnnotations;

using DockerUpdateGuard.Images.Data;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ObservedImageRegistrationRequest"/>
/// </summary>
[TestClass]
public class ObservedImageRegistrationRequestTests
{
    #region Static methods

    /// <summary>
    /// Validate a request as object-level validation does
    /// </summary>
    /// <param name="request">Request</param>
    /// <returns>Validation results</returns>
    private static List<ValidationResult> Validate(ObservedImageRegistrationRequest request)
    {
        return request.Validate(new ValidationContext(request)).ToList();
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Verify a name over 200 characters after trimming gives one message for the name
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void ObservedImageRegistrationRequestValidateTooLongNameReturnsNameMessage()
    {
        var request = new ObservedImageRegistrationRequest
                      {
                          Name = new string('a', 201),
                          ImageReference = "docker.io/company/api:1.0.0",
                      };

        var results = Validate(request);

        Assert.HasCount(1, results, "Only the name is invalid");
        Assert.AreEqual("The name must not be longer than 200 characters", results[0].ErrorMessage, "The result must carry the name text");
        Assert.AreEqual("Name", results[0].MemberNames.Single(), "The result must belong to the name field");
    }

    /// <summary>
    /// Verify a name of 200 characters with surrounding whitespace gives no result
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void ObservedImageRegistrationRequestValidateMaximumNameWithWhitespaceReturnsNoResult()
    {
        var request = new ObservedImageRegistrationRequest
                      {
                          Name = $"  {new string('a', 200)}  ",
                          ImageReference = "docker.io/company/api:1.0.0",
                      };

        Assert.IsEmpty(Validate(request), "A name that is 200 characters after trimming must pass");
    }

    /// <summary>
    /// Verify an over-long reference part gives one message for the reference field
    /// </summary>
    [TestMethod]
    [TestCategory("AC2")]
    public void ObservedImageRegistrationRequestValidateTooLongReferencePartReturnsReferenceMessage()
    {
        var request = new ObservedImageRegistrationRequest
                      {
                          Name = "Company API",
                          ImageReference = $"docker.io/company/api:{new string('t', 201)}",
                      };

        var results = Validate(request);

        Assert.HasCount(1, results, "Only the reference is invalid");
        Assert.AreEqual("The tag must not be longer than 200 characters", results[0].ErrorMessage, "The result must carry the tag text");
        Assert.AreEqual("ImageReference", results[0].MemberNames.Single(), "The result must belong to the reference field");
    }

    #endregion // Methods
}