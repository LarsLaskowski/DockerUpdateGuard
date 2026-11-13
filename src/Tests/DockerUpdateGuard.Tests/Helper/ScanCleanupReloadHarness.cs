using System.Globalization;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Queries;
using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Tests.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Runs the cleanup job against a real validating options monitor and simulates a configuration reload
/// </summary>
internal sealed class ScanCleanupReloadHarness : IAsyncDisposable
{
    #region Constants

    /// <summary>
    /// Configuration key of the cleanup interval
    /// </summary>
    private const string CleanupIntervalKey = "DockerUpdateGuard:Scanning:CleanupIntervalMinutes";

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Configuration root the options are bound to
    /// </summary>
    private readonly IConfigurationRoot _configuration;

    /// <summary>
    /// Cancellation source of the job
    /// </summary>
    private readonly CancellationTokenSource _cancellationTokenSource;

    /// <summary>
    /// Service provider holding the options and the database
    /// </summary>
    private readonly ServiceProvider _serviceProvider;

    /// <summary>
    /// Job under test
    /// </summary>
    private readonly TestScanCleanupBackgroundService _service;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="initialCleanupIntervalMinutes">Valid cleanup interval the job is created with</param>
    public ScanCleanupReloadHarness(int initialCleanupIntervalMinutes)
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();

        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                                                                          {
                                                                              ["DockerUpdateGuard:ConnectionString"] = "Host=database;Database=dug;Username=test;Password=test",
                                                                              [CleanupIntervalKey] = initialCleanupIntervalMinutes.ToString(CultureInfo.InvariantCulture),
                                                                          })
                                                   .Build();
        Logger = new TestLogger<ScanCleanupBackgroundService>();
        Clock = new WaitRecordingTimeProvider();
        _cancellationTokenSource = new CancellationTokenSource();

        services.AddSingleton<IConfiguration>(_configuration);
        services.AddSingleton<IValidateOptions<DockerUpdateGuardOptions>, DockerUpdateGuardOptionsValidator>();
        services.AddOptions<DockerUpdateGuardOptions>()
                .Bind(_configuration.GetSection(DockerUpdateGuardOptions.SectionName));
        services.AddScoped(_ => new DockerUpdateGuardDbContext(new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(databaseName)
                                                                                                                        .Options));
        services.AddScoped(_ => new ApplicationTelemetry());
        services.AddScoped<ILiveImageInventoryQueryService, LiveImageInventoryQueryService>();

        _serviceProvider = services.BuildServiceProvider();
        _service = new TestScanCleanupBackgroundService(Logger,
                                                        _serviceProvider.GetRequiredService<IOptionsMonitor<DockerUpdateGuardOptions>>(),
                                                        _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                                                        Clock.TimeProvider);
    }

    #endregion // Constructors

    #region Properties

    /// <summary>
    /// Logger of the job
    /// </summary>
    public TestLogger<ScanCleanupBackgroundService> Logger { get; }

    /// <summary>
    /// Time provider recording the waits of the job
    /// </summary>
    public WaitRecordingTimeProvider Clock { get; }

    /// <summary>
    /// Task of the running job
    /// </summary>
    public Task RunTask { get; private set; } = Task.CompletedTask;

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Start the job's loop
    /// </summary>
    public void Start()
    {
        RunTask = _service.RunAsync(_cancellationTokenSource.Token);
    }

    /// <summary>
    /// Simulate a configuration reload that sets the cleanup interval, as the options monitor does on a change
    /// </summary>
    /// <param name="cleanupIntervalMinutes">New value of the cleanup interval key</param>
    public void Reload(int cleanupIntervalMinutes)
    {
        _configuration[CleanupIntervalKey] = cleanupIntervalMinutes.ToString(CultureInfo.InvariantCulture);

        _serviceProvider.GetRequiredService<IOptionsMonitorCache<DockerUpdateGuardOptions>>()
                        .TryRemove(Options.DefaultName);
    }

    /// <summary>
    /// Cancel the job and wait until its task ended
    /// </summary>
    /// <returns>Task</returns>
    public async Task StopAsync()
    {
        await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);
        await RunTask.ConfigureAwait(false);
    }

    #endregion // Methods

    #region IAsyncDisposable

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _service.Dispose();
        _cancellationTokenSource.Dispose();

        await _serviceProvider.DisposeAsync().ConfigureAwait(false);
    }

    #endregion // IAsyncDisposable
}