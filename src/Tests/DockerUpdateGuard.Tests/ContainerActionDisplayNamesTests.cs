using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.UI;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ContainerActionDisplayNames"/>
/// </summary>
[TestClass]
public class ContainerActionDisplayNamesTests
{
    #region Methods

    /// <summary>
    /// Verify every action type maps to its display name and a type without one falls back to its member name
    /// </summary>
    /// <param name="actionType">Action type</param>
    /// <param name="expectedName">Expected display name</param>
    [TestMethod]
    [DataRow(ContainerActionType.StartContainer, "Start")]
    [DataRow(ContainerActionType.StopContainer, "Stop")]
    [DataRow(ContainerActionType.RestartContainer, "Restart")]
    [DataRow(ContainerActionType.RedeployStack, "Redeploy stack")]
    [DataRow(ContainerActionType.UpdateService, "Update service")]
    [DataRow(ContainerActionType.NotSet, "NotSet")]
    public void ContainerActionDisplayNamesGetDisplayNameReturnsNameOfActionType(ContainerActionType actionType, string expectedName)
    {
        var displayName = ContainerActionDisplayNames.GetDisplayName(actionType);

        Assert.AreEqual(expectedName, displayName, $"Action type {actionType} must be shown as '{expectedName}'");
    }

    #endregion // Methods
}