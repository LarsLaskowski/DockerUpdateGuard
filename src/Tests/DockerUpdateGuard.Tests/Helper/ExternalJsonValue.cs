namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Value with one property that the tests of the external JSON reader fill by name
/// </summary>
internal sealed class ExternalJsonValue
{
    #region Properties

    /// <summary>
    /// Value read from the property of the same name
    /// </summary>
    public int Value { get; set; }

    #endregion // Properties
}