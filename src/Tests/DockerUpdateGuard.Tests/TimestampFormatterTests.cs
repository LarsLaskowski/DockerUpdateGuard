using System.Globalization;

using DockerUpdateGuard.UI;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="TimestampFormatter"/>
/// </summary>
[TestClass]
public class TimestampFormatterTests
{
    #region Methods

    /// <summary>
    /// Verify a timestamp with an offset is converted to UTC and carries the suffix
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void TimestampFormatterFormatWithOffsetReturnsUtcWithSuffix()
    {
        var timestamp = new DateTimeOffset(2026, 11, 23, 23, 27, 0, TimeSpan.FromHours(2));

        var text = TimestampFormatter.Format(timestamp);

        Assert.AreEqual("2026-11-23 21:27 UTC", text, "The timestamp must be converted to UTC and end with the UTC suffix");
    }

    /// <summary>
    /// Verify the text does not follow the separators of the current culture
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void TimestampFormatterFormatUnderOtherSeparatorsReturnsInvariantText()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        var timestamp = new DateTimeOffset(2026, 11, 23, 23, 27, 0, TimeSpan.FromHours(2));

        culture.DateTimeFormat.TimeSeparator = ".";
        culture.DateTimeFormat.DateSeparator = "/";

        try
        {
            CultureInfo.CurrentCulture = culture;

            var text = TimestampFormatter.Format(timestamp);
            var label = TimestampFormatter.FormatChartLabel(timestamp, false);

            Assert.AreEqual("2026-11-23 21:27 UTC", text, "The text must keep '-' and ':' whatever the current culture's separators");
            Assert.AreEqual("21:27", label, "The chart label must keep ':' whatever the current culture's time separator");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    /// <summary>
    /// Verify an optional value is formatted like a required one
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void TimestampFormatterFormatOptionalWithValueReturnsFormattedText()
    {
        DateTimeOffset? timestamp = new DateTimeOffset(2026, 11, 23, 23, 27, 0, TimeSpan.FromHours(2));

        var text = TimestampFormatter.FormatOptional(timestamp);

        Assert.AreEqual("2026-11-23 21:27 UTC", text, "A present value must be formatted as UTC text");
    }

    /// <summary>
    /// Verify a missing value gives an empty text
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void TimestampFormatterFormatOptionalWithNullReturnsEmpty()
    {
        var text = TimestampFormatter.FormatOptional(null);

        Assert.AreEqual(string.Empty, text, "A missing value must give an empty text");
    }

    #endregion // Methods
}