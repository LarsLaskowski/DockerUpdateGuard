namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Switches the local time zone of the test process for the lifetime of the scope (Linux, through the TZ variable)
/// </summary>
internal sealed class LocalTimeZoneScope : IDisposable
{
    #region Constants

    /// <summary>
    /// Name of the environment variable that selects the local time zone
    /// </summary>
    private const string TimeZoneVariable = "TZ";

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Value of the variable before the scope, null when it was not set
    /// </summary>
    private readonly string? _previousValue;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Switch the local time zone
    /// </summary>
    /// <param name="timeZoneId">Identifier of the time zone to switch to</param>
    public LocalTimeZoneScope(string timeZoneId)
    {
        _previousValue = Environment.GetEnvironmentVariable(TimeZoneVariable);

        Environment.SetEnvironmentVariable(TimeZoneVariable, timeZoneId);
        TimeZoneInfo.ClearCachedData();

        if (string.Equals(TimeZoneInfo.Local.Id, timeZoneId, StringComparison.Ordinal) == false)
        {
            Dispose();

            throw new InvalidOperationException($"The local time zone could not be switched to '{timeZoneId}'; the time zone switch needs a Linux host with the zone database");
        }
    }

    #endregion // Constructors

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable(TimeZoneVariable, _previousValue);
        TimeZoneInfo.ClearCachedData();
    }

    #endregion // IDisposable
}