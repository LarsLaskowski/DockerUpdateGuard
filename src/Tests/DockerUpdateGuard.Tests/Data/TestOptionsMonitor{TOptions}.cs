using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Tests.Data;

/// <summary>
/// Lightweight options monitor for deterministic tests
/// </summary>
/// <typeparam name="TOptions">Options type</typeparam>
internal sealed partial class TestOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
    where TOptions : class
{
    #region Fields

    private readonly TOptions _currentValue;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="currentValue">Current value</param>
    public TestOptionsMonitor(TOptions currentValue)
    {
        _currentValue = currentValue;
    }

    #endregion // Constructors

    #region Properties

    /// <inheritdoc/>
    public TOptions CurrentValue => _currentValue;

    #endregion // Properties

    #region Methods

    /// <inheritdoc/>
    public TOptions Get(string? name)
    {
        return _currentValue;
    }

    /// <inheritdoc/>
    public IDisposable OnChange(Action<TOptions, string?> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        return NullDisposable.Instance;
    }

    #endregion // Methods
}