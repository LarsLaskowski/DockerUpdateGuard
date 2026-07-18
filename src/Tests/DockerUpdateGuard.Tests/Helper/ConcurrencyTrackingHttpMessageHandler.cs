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

    #endregion // Constructors

    #region Properties

    /// <summary>
    /// Peak number of requests observed in flight at the same time
    /// </summary>
    public int MaxObservedConcurrency => Volatile.Read(ref _maxConcurrency);

    #endregion // Properties

    #region Methods

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
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var current = Interlocked.Increment(ref _currentConcurrency);

        UpdateMaxConcurrency(current);

        try
        {
            await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);

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