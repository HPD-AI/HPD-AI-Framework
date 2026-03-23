using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace HPD.Agent;

/// <summary>
/// Tracks observer health and implements circuit breaker pattern.
/// Automatically disables observers that exceed failure thresholds.
/// </summary>
internal class ObserverHealthTracker
{
    private readonly ConcurrentDictionary<string, ObserverHealth> _healthState = new();
    private readonly ILogger? _logger;
    private readonly Counter<long>? _errorCounter;
    private readonly int _maxConsecutiveFailures;
    private readonly int _successesToReset;

    public ObserverHealthTracker(
        ILogger? logger,
        Counter<long>? errorCounter,
        int maxConsecutiveFailures = 10,
        int successesToReset = 3)
    {
        _logger = logger;
        _errorCounter = errorCounter;
        _maxConsecutiveFailures = maxConsecutiveFailures;
        _successesToReset = successesToReset;
    }

    /// <summary>
    /// Checks if an observer should be allowed to process events.
    /// </summary>
    public bool ShouldProcess(IAgentEventObserver observer)
    {
        var health = GetOrCreateHealth(observer);
        return !health.IsCircuitOpen;
    }

    /// <summary>
    /// Records a successful event processing.
    /// </summary>
    public void RecordSuccess(IAgentEventObserver observer)
    {
        var health = GetOrCreateHealth(observer);
        health.RecordSuccess();

        // If circuit was open and we've hit success threshold, close it
        if (health.IsCircuitOpen && health.ConsecutiveSuccesses >= _successesToReset)
        {
            _logger?.LogInformation(
                "Circuit breaker CLOSED for observer {ObserverType} after {Successes} successful operations",
                observer.GetType().Name, health.ConsecutiveSuccesses);

            health.CloseCircuit();
        }
    }

    /// <summary>
    /// Records a failed event processing.
    /// Opens circuit breaker if threshold exceeded.
    /// </summary>
    public void RecordFailure(IAgentEventObserver observer, Exception ex)
    {
        var health = GetOrCreateHealth(observer);
        health.RecordFailure();

        // Open circuit if threshold exceeded
        if (!health.IsCircuitOpen && health.ConsecutiveFailures >= _maxConsecutiveFailures)
        {
            _logger?.LogError(
                "Circuit breaker OPENED for observer {ObserverType} after {Failures} consecutive failures. " +
                "Observer disabled until manual reset or successful retry.",
                observer.GetType().Name, health.ConsecutiveFailures);

            health.OpenCircuit();

            _errorCounter?.Add(1,
                new KeyValuePair<string, object?>("observer.type", observer.GetType().Name),
                new KeyValuePair<string, object?>("circuit_opened", true));
        }
        else
        {
            // Still failing but circuit not open yet
            _errorCounter?.Add(1,
                new KeyValuePair<string, object?>("observer.type", observer.GetType().Name),
                new KeyValuePair<string, object?>("circuit_opened", false));
        }
    }

    private ObserverHealth GetOrCreateHealth(IAgentEventObserver observer)
    {
        var key = observer.GetType().FullName ?? observer.GetType().Name;
        return _healthState.GetOrAdd(key, _ => new ObserverHealth());
    }

    private class ObserverHealth
    {
        private int _consecutiveFailures;
        private int _consecutiveSuccesses;
        private int _isCircuitOpen;

        public int ConsecutiveFailures => _consecutiveFailures;
        public int ConsecutiveSuccesses => _consecutiveSuccesses;
        public bool IsCircuitOpen => _isCircuitOpen == 1;

        public void RecordSuccess()
        {
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            Interlocked.Increment(ref _consecutiveSuccesses);
        }

        public void RecordFailure()
        {
            Interlocked.Increment(ref _consecutiveFailures);
            Interlocked.Exchange(ref _consecutiveSuccesses, 0);
        }

        public void OpenCircuit()
        {
            Interlocked.Exchange(ref _isCircuitOpen, 1);
        }

        public void CloseCircuit()
        {
            Interlocked.Exchange(ref _isCircuitOpen, 0);
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            Interlocked.Exchange(ref _consecutiveSuccesses, 0);
        }
    }
}
