namespace HPD.Events;

/// <summary>
/// Time abstraction for deterministic event systems.
/// - TestClock: Manually advanced, for backtest/replay/testing
/// - LiveClock: Real wall-clock time, for production
///
/// Uses .NET types (DateTimeOffset, TimeSpan) to keep HPD-Events domain-agnostic.
/// </summary>
public interface IClock
{
    /// <summary>Current UTC time.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Current time as Unix nanoseconds (for high-precision needs).</summary>
    long UnixNanos { get; }

    /// <summary>Set a one-time alert at absolute time.</summary>
    ITimerHandle SetAlert(string name, DateTimeOffset alertTime, Action<TimeEvent> callback);

    /// <summary>Set a one-time alert after delay.</summary>
    ITimerHandle SetAlert(string name, TimeSpan delay, Action<TimeEvent> callback);

    /// <summary>Set a recurring timer.</summary>
    ITimerHandle SetTimer(
        string name,
        TimeSpan interval,
        Action<TimeEvent> callback,
        DateTimeOffset? startTime = null,
        DateTimeOffset? stopTime = null);

    /// <summary>Cancel timer by name.</summary>
    void CancelTimer(string name);

    /// <summary>Cancel all timers.</summary>
    void CancelAllTimers();

    /// <summary>Names of active timers.</summary>
    IEnumerable<string> TimerNames { get; }
}
