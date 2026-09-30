using HPD.Agent.Audio.LiveKit.Generated;

namespace HPD.Agent.Audio.LiveKit;

internal enum LiveKitIssuedOperationDisposition : byte
{
    Pending = 0,
    Completed = 1,
    Detached = 2,
    DetachedCompleted = 3,
    OutcomeUnknown = 4,
    Quarantined = 5
}

internal readonly record struct LiveKitIssuedOperationSnapshot(
    LiveKitIssuedOperationDisposition Disposition,
    LiveKitFfiEventCase CompletionCase,
    int ObservedCompletionCount);

internal sealed class LiveKitIssuedOperationRegistry : ILiveKitFfiIssuedOperationSink
{
    internal const int MaximumEarlyCompletions = 256;

    private readonly object _gate = new();
    private readonly Dictionary<LiveKitFfiCompletionKey, Entry> _issued = [];
    private readonly Dictionary<LiveKitFfiCompletionKey, EarlyCompletion> _early = [];
    private string? _quarantineCode;
    private int _roomEvents;
    private int _audioStreamEvents;

    internal bool IsQuarantined { get { lock (_gate) return _quarantineCode is not null; } }
    internal string? QuarantineCode { get { lock (_gate) return _quarantineCode; } }
    internal int RoomEventCount { get { lock (_gate) return _roomEvents; } }
    internal int AudioStreamEventCount { get { lock (_gate) return _audioStreamEvents; } }

    internal void Register(LiveKitFfiCompletionKey key)
    {
        lock (_gate)
        {
            ThrowIfQuarantined();
            if (key.AsyncId == 0 || !LiveKitFfiGeneratedProtocol.TryGetCompletionEvent(key.Operation, out _) ||
                !_issued.TryAdd(key, new Entry()))
            {
                throw new InvalidDataException($"Invalid or duplicate issued LiveKit operation {key}.");
            }
            if (_early.Remove(key, out var early))
                ApplyCompletion(_issued[key], early.EventCase);
        }
    }

    internal void Detach(LiveKitFfiCompletionKey key)
    {
        lock (_gate)
        {
            var entry = Get(key);
            entry.Detached = true;
            entry.Disposition = entry.ObservedCompletionCount == 0
                ? LiveKitIssuedOperationDisposition.Detached
                : LiveKitIssuedOperationDisposition.DetachedCompleted;
        }
    }

    internal void MarkOutcomeUnknown(LiveKitFfiCompletionKey key)
    {
        lock (_gate)
        {
            var entry = Get(key);
            if (entry.ObservedCompletionCount == 0)
                entry.Disposition = LiveKitIssuedOperationDisposition.OutcomeUnknown;
        }
    }

    internal LiveKitIssuedOperationSnapshot Reconcile(LiveKitFfiCompletionKey key)
    {
        lock (_gate)
        {
            var entry = Get(key);
            return new(entry.Disposition, entry.CompletionCase, entry.ObservedCompletionCount);
        }
    }

    public void ObserveCompletion(LiveKitFfiCompletionKey key, LiveKitFfiEventCase eventCase)
    {
        lock (_gate)
        {
            if (_quarantineCode is not null) return;
            if (!LiveKitFfiGeneratedProtocol.TryGetCompletionEvent(key.Operation, out var expected) || expected != eventCase)
            {
                QuarantineCore("ffi-completion-family-mismatch");
                return;
            }
            if (!_issued.TryGetValue(key, out var entry))
            {
                if (_early.ContainsKey(key)) return;
                if (_early.Count == MaximumEarlyCompletions)
                {
                    QuarantineCore("ffi-early-completion-overflow");
                    return;
                }
                _early.Add(key, new(eventCase));
                return;
            }
            ApplyCompletion(entry, eventCase);
        }
    }

    public void ObserveRoomEvent()
    {
        lock (_gate)
            if (_quarantineCode is null) _roomEvents++;
    }

    public void ObserveAudioStreamEvent()
    {
        lock (_gate)
            if (_quarantineCode is null) _audioStreamEvents++;
    }

    public void Quarantine(LiveKitFfiEventCase eventCase, string safeCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(safeCode);
        lock (_gate) QuarantineCore(safeCode);
    }

    private Entry Get(LiveKitFfiCompletionKey key)
    {
        ThrowIfQuarantined();
        return _issued.TryGetValue(key, out var entry)
            ? entry
            : throw new InvalidDataException($"LiveKit operation {key} is not registered.");
    }

    private void ThrowIfQuarantined()
    {
        if (QuarantineCode is { } safeCode)
            throw new InvalidDataException($"LiveKit issued-operation registry is quarantined: {safeCode}.");
    }

    private static void ApplyCompletion(Entry entry, LiveKitFfiEventCase eventCase)
    {
        entry.ObservedCompletionCount++;
        if (entry.ObservedCompletionCount != 1) return;
        entry.CompletionCase = eventCase;
        entry.Disposition = entry.Detached
            ? LiveKitIssuedOperationDisposition.DetachedCompleted
            : LiveKitIssuedOperationDisposition.Completed;
    }

    private void QuarantineCore(string safeCode) => _quarantineCode ??= safeCode;

    private sealed class Entry
    {
        internal bool Detached;
        internal int ObservedCompletionCount;
        internal LiveKitFfiEventCase CompletionCase;
        internal LiveKitIssuedOperationDisposition Disposition;
    }

    private readonly record struct EarlyCompletion(LiveKitFfiEventCase EventCase);
}

