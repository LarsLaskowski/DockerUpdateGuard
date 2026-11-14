using DockerUpdateGuard.Configuration;

using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.DockerHub;

/// <summary>
/// Process-wide limit of the Docker Hub requests in flight, shared by all client instances and scopes and following
/// a reload of <c>DockerHub:MaxParallelRequests</c>; waiting requests are admitted in arrival order
/// </summary>
public sealed class DockerHubRequestLimiter : IDisposable
{
    #region Fields

    /// <summary>
    /// Lock guarding the in-flight count, the limit and the queue of waiters
    /// </summary>
    private readonly Lock _lock = new();

    /// <summary>
    /// Waiters in arrival order
    /// </summary>
    private readonly LinkedList<TaskCompletionSource> _waiters = new();

    /// <summary>
    /// Queued waiters with the registration of their cancellation token, which is set once it is made
    /// </summary>
    private readonly Dictionary<TaskCompletionSource, CancellationTokenRegistration> _queued = [];

    /// <summary>
    /// Registration of the options change listener
    /// </summary>
    private readonly IDisposable? _changeRegistration;

    /// <summary>
    /// Number of requests in flight
    /// </summary>
    private int _inFlight;

    /// <summary>
    /// Current limit of the requests in flight
    /// </summary>
    private int _limit;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="optionsMonitor">Options monitor</param>
    public DockerHubRequestLimiter(IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor)
    {
        _limit = GetLimit(optionsMonitor.CurrentValue);
        _changeRegistration = optionsMonitor.OnChange(OnOptionsChanged);
    }

    #endregion // Constructors

    #region Static methods

    /// <summary>
    /// Determine the limit of the requests in flight from the options
    /// </summary>
    /// <param name="options">Application options</param>
    /// <returns>Limit, at least one</returns>
    private static int GetLimit(DockerUpdateGuardOptions options)
    {
        return Math.Max(1, options.DockerHub.MaxParallelRequests);
    }

    /// <summary>
    /// Complete waiters that were granted a slot; the registration of their token ends first, outside the lock
    /// </summary>
    /// <param name="granted">Granted waiters with their registrations</param>
    private static void CompleteGranted(List<(TaskCompletionSource Waiter, CancellationTokenRegistration Registration)> granted)
    {
        foreach (var (waiter, registration) in granted)
        {
            registration.Dispose();
            waiter.TrySetResult();
        }
    }

    /// <summary>
    /// Remove a queued waiter and cancel it; a waiter that was granted meanwhile keeps its slot
    /// </summary>
    /// <param name="state">Limiter, waiter and cancellation token</param>
    private static void CancelWaiter(object? state)
    {
        var (limiter, waiter, cancellationToken) = ((DockerHubRequestLimiter, TaskCompletionSource, CancellationToken))state!;
        CancellationTokenRegistration registration;
        bool removed;

        lock (limiter._lock)
        {
            removed = limiter._queued.Remove(waiter, out registration);

            if (removed)
            {
                limiter._waiters.Remove(waiter);
            }
        }

        if (removed)
        {
            registration.Dispose();
            waiter.TrySetCanceled(cancellationToken);
        }
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Wait for a request slot
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task that completes once a slot is taken</returns>
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        TaskCompletionSource waiter;

        lock (_lock)
        {
            if (_waiters.Count == 0 && _inFlight < _limit)
            {
                _inFlight++;

                return Task.CompletedTask;
            }

            waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.AddLast(waiter);
            _queued.Add(waiter, default);
        }

        var registration = cancellationToken.Register(CancelWaiter, (this, waiter, cancellationToken));
        var keepRegistration = false;

        lock (_lock)
        {
            if (_queued.ContainsKey(waiter))
            {
                _queued[waiter] = registration;
                keepRegistration = true;
            }
        }

        if (keepRegistration == false)
        {
            registration.Dispose();
        }

        return waiter.Task;
    }

    /// <summary>
    /// Release a request slot
    /// </summary>
    public void Release()
    {
        List<(TaskCompletionSource Waiter, CancellationTokenRegistration Registration)> granted;

        lock (_lock)
        {
            if (_inFlight == 0)
            {
                throw new InvalidOperationException("No Docker Hub request slot is taken");
            }

            _inFlight--;
            granted = AdmitWaiters();
        }

        CompleteGranted(granted);
    }

    /// <summary>
    /// Apply a changed configuration
    /// </summary>
    /// <param name="options">New options</param>
    /// <param name="name">Options name</param>
    private void OnOptionsChanged(DockerUpdateGuardOptions options, string? name)
    {
        List<(TaskCompletionSource Waiter, CancellationTokenRegistration Registration)> granted;

        lock (_lock)
        {
            _limit = GetLimit(options);
            granted = AdmitWaiters();
        }

        CompleteGranted(granted);
    }

    /// <summary>
    /// Admit queued waiters in arrival order while the limit allows; the caller holds the lock
    /// </summary>
    /// <returns>Granted waiters with their registrations, to complete after the lock is released</returns>
    private List<(TaskCompletionSource Waiter, CancellationTokenRegistration Registration)> AdmitWaiters()
    {
        List<(TaskCompletionSource Waiter, CancellationTokenRegistration Registration)> granted = [];

        while (_waiters.First is { } first && _inFlight < _limit)
        {
            _waiters.RemoveFirst();
            _queued.Remove(first.Value, out var registration);
            _inFlight++;
            granted.Add((first.Value, registration));
        }

        return granted;
    }

    #endregion // Methods

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        _changeRegistration?.Dispose();
    }

    #endregion // IDisposable
}