using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ExternalOperationResult"/>
/// </summary>
[TestClass]
public class ExternalOperationResultTests
{
    #region Methods

    /// <summary>
    /// Verify a succeeded result carries the data and no message
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void ExternalOperationResultSucceededCarriesDataWithoutMessage()
    {
        List<string> data = ["a", "b"];

        var result = ExternalOperationResult.Succeeded<List<string>>(data);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The status must be Succeeded");
        Assert.AreSame(data, result.Data, "The data must be the given instance");
        Assert.IsNull(result.Message, "A succeeded result must carry no message");
    }

    /// <summary>
    /// Verify every failure factory carries its own status and the message without data
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void ExternalOperationResultFailureFactoriesCarryStatusAndMessageWithoutData()
    {
        var cases = new (ExternalOperationStatus Status, ExternalOperationResult<string> Result)[]
                    {
                        (ExternalOperationStatus.NotConfigured, ExternalOperationResult.NotConfigured<string>("m.internal.test")),
                        (ExternalOperationStatus.Unsupported, ExternalOperationResult.Unsupported<string>("m.internal.test")),
                        (ExternalOperationStatus.NotFound, ExternalOperationResult.NotFound<string>("m.internal.test")),
                        (ExternalOperationStatus.Failed, ExternalOperationResult.Failed<string>("m.internal.test")),
                        (ExternalOperationStatus.Unknown, ExternalOperationResult.Unknown<string>("m.internal.test"))
                    };

        foreach (var (status, result) in cases)
        {
            Assert.AreEqual(status, result.Status, $"The status of the {status} factory must match");
            Assert.AreEqual("m.internal.test", result.Message, $"The message of the {status} factory must be kept");
            Assert.IsNull(result.Data, $"The {status} factory must carry no data");
        }
    }

    /// <summary>
    /// Verify the pass-through statuses keep their status and message and carry no data
    /// </summary>
    [TestMethod]
    [TestCategory("AC5")]
    public void ExternalOperationResultPropagateFailureKeepsPassThroughStatusAndMessage()
    {
        var statuses = new[]
                       {
                           ExternalOperationStatus.NotConfigured,
                           ExternalOperationStatus.Unsupported,
                           ExternalOperationStatus.NotFound,
                           ExternalOperationStatus.Unknown
                       };

        foreach (var status in statuses)
        {
            var source = new ExternalOperationResult<string>(status, "payload", "m.internal.test");

            var result = source.PropagateFailure<int>("fallback.internal.test");

            Assert.AreEqual(status, result.Status, $"The {status} status must be kept");
            Assert.AreEqual("m.internal.test", result.Message, $"The message of the {status} source must be kept");
            Assert.AreEqual(0, result.Data, $"The {status} result must carry default data");
        }
    }

    /// <summary>
    /// Verify every other status is mapped to Failed with the message kept
    /// </summary>
    [TestMethod]
    [TestCategory("AC5")]
    public void ExternalOperationResultPropagateFailureMapsOtherStatusesToFailed()
    {
        var statuses = new[]
                       {
                           ExternalOperationStatus.Succeeded,
                           ExternalOperationStatus.Failed,
                           ExternalOperationStatus.NotSet
                       };

        foreach (var status in statuses)
        {
            var source = new ExternalOperationResult<string>(status, "payload", "m.internal.test");

            var result = source.PropagateFailure<string>("fallback.internal.test");

            Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, $"The {status} status must be mapped to Failed");
            Assert.AreEqual("m.internal.test", result.Message, $"The message of the {status} source must be kept");
            Assert.IsNull(result.Data, $"The {status} result must carry default data");
        }
    }

    /// <summary>
    /// Verify the fallback message is used when the source has none
    /// </summary>
    [TestMethod]
    [TestCategory("AC5")]
    public void ExternalOperationResultPropagateFailureUsesFallbackWhenMessageIsNull()
    {
        var source = new ExternalOperationResult<string>(ExternalOperationStatus.NotFound, null, null);

        var result = source.PropagateFailure<string>("fallback.internal.test");

        Assert.AreEqual(ExternalOperationStatus.NotFound, result.Status, "The status must be kept");
        Assert.AreEqual("fallback.internal.test", result.Message, "The fallback message must be used when the source message is null");
    }

    #endregion // Methods
}