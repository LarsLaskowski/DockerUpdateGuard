using System.Net;
using System.Text;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// HTTP message handler that records the peak number of concurrent in-flight requests
/// </summary>
internal sealed class ConcurrencyTrackingHttpMessageHandler : HttpMessageHandler
{
    #region Fields

    /// <summary>
    /// JSON payload returned for every request
    /// </summary>
    private readonly string _jsonContent;

    /// <summary>
    /// Artificial delay applied while a request is in flight
    /// </summary>
    private readonly TimeSpan _delay;

    /// <summary>
    /// Gate that holds every request until it is opened; null in the delayed mode
    /// </summary>
    private readonly TaskCompletionSource? _gate;

    /// <summary>
    /// Signal released once per arrived request
    /// </summary>
    private readonly SemaphoreSlim _arrivals = new(0);

    /// <summary>
    /// Number of requests currently in flight
    /// </summary>
    private int _currentConcurrency;

    /// <summary>
    /// Peak number of requests observed in flight at the same time
    /// </summary>
    private int _maxConcurrency;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="jsonContent">JSON payload returned for every request</param>
    /// <param name="delay">Artificial delay applied while a request is in flight</param>
    public ConcurrencyTrackingHttpMessageHandler(string jsonContent, TimeSpan delay)
    {
        _jsonContent = jsonContent;
        _delay = delay;
    }

    /// <summary>
    /// Constructor of the gated mode: every request is held until <see cref="OpenGate"/> is called
    /// </summary>
    /// <param name="jsonContent">JSON payload returned for every request</param>
    public ConcurrencyTrackingHttpMessageHandler(string jsonContent)
    {
        _jsonContent = jsonContent;
        _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    #endregion // Constructors

    #region Properties

    /// <summary>
    /// Peak number of requests observed in flight at the same time
    /// </summary>
    public int MaxObservedConcurrency => Volatile.Read(ref _maxConcurrency);

    /// <summary>
    /// Number of requests currently in flight
    /// </summary>
    public int CurrentConcurrency => Volatile.Read(ref _currentConcurrency);

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Open the gate so the held requests are answered
    /// </summary>
    public void OpenGate()
    {
        _gate?.TrySetResult();
    }

    /// <summary>
    /// Wait until the given number of requests has arrived at the handler (arrival signal, no delay)
    /// </summary>
    /// <param name="count">Number of arrivals to wait for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    public async Task WaitForArrivalsAsync(int count, CancellationToken cancellationToken)
    {
        for (var index = 0; index < count; index++)
        {
            await _arrivals.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Raise the observed peak concurrency to the supplied value when it is higher
    /// </summary>
    /// <param name="candidate">Candidate concurrency value</param>
    private void UpdateMaxConcurrency(int candidate)
    {
        var observedMax = Volatile.Read(ref _maxConcurrency);

        while (candidate > observedMax)
        {
            var previousMax = Interlocked.CompareExchange(ref _maxConcurrency, candidate, observedMax);

            if (previousMax == observedMax)
            {
                break;
            }

            observedMax = previousMax;
        }
    }

    #endregion // Methods

    #region HttpMessageHandler

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _arrivals.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var current = Interlocked.Increment(ref _currentConcurrency);

        UpdateMaxConcurrency(current);
        _arrivals.Release();

        try
        {
            if (_gate is null)
            {
                await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
                   {
                       Content = new StringContent(_jsonContent,
                                                   Encoding.UTF8,
                                                   "application/json"),
                   };
        }
        finally
        {
            Interlocked.Decrement(ref _currentConcurrency);
        }
    }

    #endregion // HttpMessageHandler
}