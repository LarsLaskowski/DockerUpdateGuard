using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Tests.Data;

/// <summary>
/// Lightweight options monitor for deterministic tests
/// </summary>
/// <typeparam name="TOptions">Options type</typeparam>
internal sealed partial class TestOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
    where TOptions : class
{
    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="currentValue">Current value</param>
    public TestOptionsMonitor(TOptions currentValue)
    {
        CurrentValue = currentValue;
    }

    #endregion // Constructors

    #region IOptionsMonitor

    #region Properties

    /// <inheritdoc/>
    public TOptions CurrentValue { get; }

    #endregion // Properties

    #region Methods

    /// <inheritdoc/>
    public TOptions Get(string? name)
    {
        return CurrentValue;
    }

    /// <inheritdoc/>
    public IDisposable OnChange(Action<TOptions, string?> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        return NullDisposable.Instance;
    }

    #endregion // Methods

    #endregion // IOptionsMonitor
}