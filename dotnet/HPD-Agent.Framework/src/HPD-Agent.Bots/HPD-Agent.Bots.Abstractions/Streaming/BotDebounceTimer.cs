namespace HPD.Agent.Bots.Streaming;

/// <summary>
/// Schedules a callback after a debounce window, cancelling any pending callback
/// when another value arrives before the window elapses.
/// </summary>
public sealed class BotDebounceTimer(int debounceMs) : IDisposable
{
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;

    /// <summary>Schedules <paramref name="callback"/> after the debounce window.</summary>
    public void Schedule(Func<Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        CancellationTokenSource? old;
        var next = new CancellationTokenSource();
        lock (_lock)
        {
            old = _cts;
            _cts = next;
        }

        old?.Cancel();
        old?.Dispose();

        _ = Task.Delay(debounceMs, next.Token)
            .ContinueWith(t => t.IsCanceled ? Task.CompletedTask : callback(),
                next.Token,
                TaskContinuationOptions.NotOnCanceled,
                TaskScheduler.Default);
    }

    /// <summary>Cancels any pending scheduled callback.</summary>
    public void Cancel()
    {
        CancellationTokenSource? old;
        lock (_lock)
        {
            old = _cts;
            _cts = null;
        }

        old?.Cancel();
        old?.Dispose();
    }

    /// <inheritdoc />
    public void Dispose() => Cancel();
}
