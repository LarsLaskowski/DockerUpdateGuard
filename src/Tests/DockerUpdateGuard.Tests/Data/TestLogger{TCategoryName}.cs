using Microsoft.Extensions.Logging;

namespace DockerUpdateGuard.Tests.Data;

/// <summary>
/// Test logger that captures emitted log entries
/// </summary>
/// <typeparam name="TCategoryName">Logger category</typeparam>
internal sealed partial class TestLogger<TCategoryName> : ILogger<TCategoryName>
{
    #region Fields

    private readonly List<TestLogEntry> _entries;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    public TestLogger()
    {
        _entries = [];
    }

    #endregion // Constructors

    #region Properties

    /// <summary>
    /// Captured log entries
    /// </summary>
    public IReadOnlyList<TestLogEntry> Entries => _entries;

    #endregion // Properties

    #region ILogger

    /// <inheritdoc/>
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        return NullScope.Instance;
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    /// <inheritdoc/>
    public void Log<TState>(LogLevel logLevel,
                            EventId eventId,
                            TState state,
                            Exception? exception,
                            Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        _entries.Add(new TestLogEntry
                     {
                         EventId = eventId,
                         Exception = exception,
                         LogLevel = logLevel,
                         Message = formatter(state, exception),
                     });
    }

    #endregion // ILogger
}