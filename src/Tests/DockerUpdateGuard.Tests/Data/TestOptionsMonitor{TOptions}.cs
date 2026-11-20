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

    /// <summary>
    /// Listeners registered through <see cref="OnChange"/>
    /// </summary>
    private readonly List<Action<TOptions, string?>> _listeners = [];

    #endregion // Fields

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

    #region Methods

    /// <summary>
    /// Replace the current value and call the registered listeners, as a configuration reload does
    /// </summary>
    /// <param name="currentValue">New current value</param>
    public void Reload(TOptions currentValue)
    {
        Action<TOptions, string?>[] listeners;

        lock (_listeners)
        {
            CurrentValue = currentValue;
            listeners = [.. _listeners];
        }

        foreach (var listener in listeners)
        {
            listener(currentValue, null);
        }
    }

    #endregion // Methods

    #region IOptionsMonitor

    /// <inheritdoc/>
    public TOptions CurrentValue { get; private set; }

    /// <inheritdoc/>
    public TOptions Get(string? name)
    {
        return CurrentValue;
    }

    /// <inheritdoc/>
    public IDisposable OnChange(Action<TOptions, string?> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        lock (_listeners)
        {
            _listeners.Add(listener);
        }

        return new ListenerRegistration(() =>
                                        {
                                            lock (_listeners)
                                            {
                                                _listeners.Remove(listener);
                                            }
                                        });
    }

    #endregion // IOptionsMonitor
}