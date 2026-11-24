using DockerUpdateGuard.UI;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ResourceUsageChartBuilder"/>
/// </summary>
[TestClass]
public class ResourceUsageChartBuilderTests
{
    #region Methods

    /// <summary>
    /// Verify chart labels are UTC: the first and last carry date and suffix, visible ones between show the time only, hidden ones stay empty
    /// </summary>
    [TestMethod]
    [TestCategory("AC2")]
    public void ResourceUsageChartBuilderGetChartLabelsWithOffsetTimestampsReturnsUtcLabels()
    {
        var offset = TimeSpan.FromHours(2);
        var history = new List<ResourceUsagePointViewData>
                      {
                          new()
                          {
                              RecordedAtUtc = new DateTimeOffset(2026, 11, 24, 0, 30, 0, offset)
                          },
                          new()
                          {
                              RecordedAtUtc = new DateTimeOffset(2026, 11, 24, 0, 45, 0, offset)
                          },
                          new()
                          {
                              RecordedAtUtc = new DateTimeOffset(2026, 11, 24, 1, 0, 0, offset)
                          },
                          new()
                          {
                              RecordedAtUtc = new DateTimeOffset(2026, 11, 24, 1, 15, 0, offset)
                          },
                          new()
                          {
                              RecordedAtUtc = new DateTimeOffset(2026, 11, 24, 1, 30, 0, offset)
                          }
                      };

        var labels = ResourceUsageChartBuilder.GetChartLabels(history, 3);

        Assert.AreSequenceEqual(new[] { "11-23 22:30 UTC", string.Empty, "23:00", string.Empty, "11-23 23:30 UTC" },
                                labels,
                                "The first and last label must carry date and UTC suffix, the visible one between them only the UTC time, hidden ones stay empty");
    }

    #endregion // Methods
}