namespace HPD.Events;

/// <summary>
/// Handle to a scheduled timer. Dispose or Cancel() to stop.
/// </summary>
public interface ITimerHandle : IDisposable
{
    /// <summary>Timer name.</summary>
    string Name { get; }

    /// <summary>Whether timer is still active.</summary>
    bool IsActive { get; }

    /// <summary>Next scheduled fire time (if known).</summary>
    DateTimeOffset? NextFireTime { get; }

    /// <summary>Cancel the timer.</summary>
    void Cancel();
}
