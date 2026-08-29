using HPD.Agent.Audio.LiveKit.Generated;

namespace HPD.Agent.Audio.LiveKit;

internal sealed class LiveKitNativeHandleOwner : IAsyncDisposable
{
    private LiveKitFfiHost? _host;
    private ulong _value;

    internal LiveKitNativeHandleOwner(LiveKitFfiHost host, LiveKitFfiHandleKind kind, ulong value)
    {
        _host = host;
        Kind = kind;
        _value = value;
    }

    internal LiveKitFfiHandleKind Kind { get; }
    internal ulong Value => Volatile.Read(ref _value);
    internal bool IsReleased => Value == 0;

    public ValueTask DisposeAsync()
    {
        var value = Interlocked.Exchange(ref _value, 0);
        if (value == 0) return ValueTask.CompletedTask;
        var host = Interlocked.Exchange(ref _host, null)
            ?? throw new InvalidOperationException("LiveKit handle lost its owning host.");
        host.DropOwned(value, $"ffi-{Kind.ToString().ToLowerInvariant()}-release-failed");
        return ValueTask.CompletedTask;
    }
}

internal sealed class LiveKitSessionGenerationFence
{
    private readonly object _gate = new();
    private long _generation;
    private Action<string>? _activeQuarantine;

    internal long Replace(Action<string> quarantinePrior)
    {
        ArgumentNullException.ThrowIfNull(quarantinePrior);
        lock (_gate)
        {
            _activeQuarantine?.Invoke("stale-session-generation");
            _activeQuarantine = quarantinePrior;
            return ++_generation;
        }
    }

    internal bool IsCurrent(long generation) { lock (_gate) return generation == _generation; }
    internal void Release(long generation) { lock (_gate) if (generation == _generation) _activeQuarantine = null; }
}

