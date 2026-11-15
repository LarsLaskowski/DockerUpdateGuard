using DockerUpdateGuard.Data.Entities;

namespace DockerUpdateGuard.Data.Tests;

/// <summary>
/// Tests for <see cref="ContainerActionType"/>
/// </summary>
[TestClass]
public class ContainerActionTypeTests
{
    #region Static methods

    /// <summary>
    /// Describe every member of the enumeration with its stored number, ordered by number
    /// </summary>
    /// <returns>Members and numbers, for example <c>NotSet=0, RestartContainer=1</c></returns>
    private static string DescribeStoredNumbers()
    {
        var members = Enum.GetValues<ContainerActionType>()
                          .OrderBy(value => (int)value)
                          .Select(value => $"{value}={(int)value}");

        return string.Join(", ", members);
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Verify every action type keeps the integer stored in the database: the existing numbers are unchanged and the new start and stop actions follow them
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void ContainerActionTypeValuesKeepStoredNumbers()
    {
        var storedNumbers = DescribeStoredNumbers();

        Assert.AreEqual("NotSet=0, RestartContainer=1, RedeployStack=2, UpdateService=3, StartContainer=4, StopContainer=5",
                        storedNumbers,
                        "Action types are stored as integers, so no existing member may change its number and no member may be added without its own number");
    }

    #endregion // Methods
}