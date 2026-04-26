using Microsoft.Extensions.Logging;

namespace DockerUpdateGuard.Tests.Data;

/// <summary>
/// Test logger that captures emitted log entries
/// </summary>
/// <typeparam name="TCategoryName">Logger category</typeparam>
internal sealed class TestLogger<TCategoryName> : ILogger<TCategoryName>
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

    #region Methods

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

    #endregion // Methods

    #region Helper types

    /// <summary>
    /// Empty logging scope
    /// </summary>
    private sealed class NullScope : IDisposable
    {
        #region Properties

        /// <summary>
        /// Shared instance
        /// </summary>
        public static NullScope Instance { get; } = new();

        #endregion // Properties

        #region Methods

        /// <summary>
        /// Release resources
        /// </summary>
        public void Dispose()
        {
        }

        #endregion // Methods
    }

    #endregion // Helper types
}