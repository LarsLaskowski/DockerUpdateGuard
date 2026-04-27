namespace DockerUpdateGuard.UI;

/// <summary>
/// Simple UI refresh state for dashboard-related summary widgets
/// </summary>
public class DashboardRefreshState
{
    #region Events

    /// <summary>
    /// Raised when dashboard-derived widgets should refresh
    /// </summary>
    public event Action? Changed;

    #endregion // Events

    #region Methods

    /// <summary>
    /// Notify listeners that dashboard-derived widgets should refresh
    /// </summary>
    public void NotifyChanged()
    {
        Changed?.Invoke();
    }

    #endregion // Methods
}