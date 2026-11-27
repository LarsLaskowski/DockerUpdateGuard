using System.Globalization;

using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ExternalTimestamp"/>
/// </summary>
[TestClass]
public class ExternalTimestampTests
{
    #region Constants

    /// <summary>
    /// Time zone the tests switch to, five hours behind UTC without daylight saving time
    /// </summary>
    private const string WestZone = "Etc/GMT+5";

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Verify a value without an offset is read as UTC and not in the local time zone
    /// </summary>
    /// <param name="value">Timestamp text</param>
    [TestMethod]
    [TestCategory("AC5")]
    [OSCondition(OperatingSystems.Linux)]
    [DataRow("2024-05-01T10:00:00")]
    [DataRow("2024-05-01")]
    public void ExternalTimestampTryParseUtcWithoutOffsetReadsUtc(string value)
    {
        var expected = DateTimeOffset.Parse(value + (value.Contains('T') ? "Z" : "T00:00:00Z"), CultureInfo.InvariantCulture);

        DateTimeOffset? result;

        using (new LocalTimeZoneScope(WestZone))
        {
            result = ExternalTimestamp.TryParseUtc(value);
        }

        Assert.IsNotNull(result, "A timestamp without an offset must be readable");
        Assert.AreEqual(expected, result.Value, "A timestamp without an offset must be the same wall time in UTC");
        Assert.AreEqual(TimeSpan.Zero, result.Value.Offset, "A timestamp without an offset must be kept with offset zero");
    }

    /// <summary>
    /// Verify a value with an offset is returned as the same instant with offset zero
    /// </summary>
    /// <param name="value">Timestamp text</param>
    [TestMethod]
    [TestCategory("AC5")]
    [OSCondition(OperatingSystems.Linux)]
    [DataRow("2024-05-01T10:00:00Z")]
    [DataRow("2024-05-01T12:00:00+02:00")]
    [DataRow("2024-05-01T05:00:00-05:00")]
    public void ExternalTimestampTryParseUtcWithOffsetReturnsSameInstantInUtc(string value)
    {
        var expected = new DateTimeOffset(2024, 5, 1, 10, 0, 0, TimeSpan.Zero);

        DateTimeOffset? result;

        using (new LocalTimeZoneScope(WestZone))
        {
            result = ExternalTimestamp.TryParseUtc(value);
        }

        Assert.IsNotNull(result, "A timestamp with an offset must be readable");
        Assert.AreEqual(expected, result.Value, "A timestamp with an offset must keep its instant");
        Assert.AreEqual(TimeSpan.Zero, result.Value.Offset, "A timestamp with an offset must be returned with offset zero");
    }

    /// <summary>
    /// Verify an empty or unreadable value is unknown
    /// </summary>
    /// <param name="value">Timestamp text</param>
    [TestMethod]
    [TestCategory("AC5")]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("not a time")]
    public void ExternalTimestampTryParseUtcUnreadableValueReturnsNull(string? value)
    {
        var result = ExternalTimestamp.TryParseUtc(value);

        Assert.IsNull(result, "A null, empty, blank or unreadable value must be unknown");
    }

    #endregion // Methods
}