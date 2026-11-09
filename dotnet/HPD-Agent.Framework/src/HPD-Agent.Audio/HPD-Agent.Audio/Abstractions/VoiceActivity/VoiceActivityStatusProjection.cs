using HPD.Events;

namespace HPD.Agent.Audio.VoiceActivity;

internal abstract record VoiceActivityProviderStatusUpdateResult
{
    private VoiceActivityProviderStatusUpdateResult() { }
    internal sealed record Applied(ProviderActivityVisibility Status) : VoiceActivityProviderStatusUpdateResult;
    internal sealed record Duplicate(ProviderActivityVisibility Status) : VoiceActivityProviderStatusUpdateResult;
    internal sealed record Stale(ProviderActivityVisibility Status) : VoiceActivityProviderStatusUpdateResult;
    internal sealed record Rejected(ProviderActivityVisibility Status, string SafeCode) :
        VoiceActivityProviderStatusUpdateResult;
}

internal sealed class VoiceActivityProviderStatusTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries;

    internal VoiceActivityProviderStatusTracker(IReadOnlyDictionary<string, ulong> sourceGenerations)
    {
        ArgumentNullException.ThrowIfNull(sourceGenerations);
        _entries = sourceGenerations.ToDictionary(static row =>
            ActivitySourceRequest.RequireAscii(row.Key, nameof(sourceGenerations)), static row =>
            row.Value == 0 ? throw new ArgumentOutOfRangeException(nameof(sourceGenerations)) :
                new Entry(row.Value, 0, ProviderActivityVisibility.Requested), StringComparer.Ordinal);
        if (_entries.Count == 0) throw new ArgumentException("At least one provider status source is required.");
    }

    internal IReadOnlyDictionary<string, ProviderActivityVisibility> Snapshot
    {
        get
        {
            lock (_gate) return _entries.ToDictionary(static row => row.Key,
                static row => row.Value.Status, StringComparer.Ordinal);
        }
    }

    internal VoiceActivityProviderStatusUpdateResult Apply(string sourceKey, ulong sourceGeneration,
        ulong sequence, ProviderActivityVisibility next)
    {
        sourceKey = ActivitySourceRequest.RequireAscii(sourceKey, nameof(sourceKey));
        if (sourceGeneration == 0 || sequence == 0 || !Enum.IsDefined(next)) throw new ArgumentOutOfRangeException();
        lock (_gate)
        {
            if (!_entries.TryGetValue(sourceKey, out var entry) || sourceGeneration < entry.Generation)
                return new VoiceActivityProviderStatusUpdateResult.Stale(
                    entry?.Status ?? ProviderActivityVisibility.Unknown);
            if (sourceGeneration > entry.Generation)
            {
                if (next != ProviderActivityVisibility.Requested)
                    return new VoiceActivityProviderStatusUpdateResult.Rejected(entry.Status,
                        "provider-generation-must-restart-requested");
                _entries[sourceKey] = new Entry(sourceGeneration, sequence, next);
                return new VoiceActivityProviderStatusUpdateResult.Applied(next);
            }
            if (sequence < entry.Sequence) return new VoiceActivityProviderStatusUpdateResult.Stale(entry.Status);
            if (sequence == entry.Sequence)
                return next == entry.Status
                    ? new VoiceActivityProviderStatusUpdateResult.Duplicate(entry.Status)
                    : new VoiceActivityProviderStatusUpdateResult.Rejected(entry.Status,
                        "provider-status-sequence-contradiction");
            if (!CanTransition(entry.Status, next))
                return new VoiceActivityProviderStatusUpdateResult.Rejected(entry.Status,
                    "provider-status-transition-invalid");
            entry.Sequence = sequence;
            entry.Status = next;
            return new VoiceActivityProviderStatusUpdateResult.Applied(next);
        }
    }

    private static bool CanTransition(ProviderActivityVisibility current,
        ProviderActivityVisibility next)
    {
        if (next == current) return true;
        if (next is ProviderActivityVisibility.Rejected or ProviderActivityVisibility.ReconnectRequired or
            ProviderActivityVisibility.Unknown or ProviderActivityVisibility.NotObservable)
            return true;
        return (current, next) switch
        {
            (ProviderActivityVisibility.Requested, ProviderActivityVisibility.Translated) => true,
            (ProviderActivityVisibility.Translated, ProviderActivityVisibility.AcceptedLocally) => true,
            (ProviderActivityVisibility.AcceptedLocally, ProviderActivityVisibility.Acknowledged) => true,
            (ProviderActivityVisibility.Acknowledged, ProviderActivityVisibility.ObservedConsistent) => true,
            (ProviderActivityVisibility.Unknown, ProviderActivityVisibility.Acknowledged) => true,
            (ProviderActivityVisibility.ReconnectRequired, ProviderActivityVisibility.Requested) => false,
            _ => false,
        };
    }

    private sealed class Entry(ulong generation, ulong sequence, ProviderActivityVisibility status)
    {
        internal ulong Generation { get; } = generation;
        internal ulong Sequence { get; set; } = sequence;
        internal ProviderActivityVisibility Status { get; set; } = status;
    }
}

internal readonly record struct VoiceActivityDiagnosticProjection(
    ulong ProjectionSequence,
    ulong LifecycleRevision,
    ulong PlanGeneration,
    ulong ConfigRevision,
    VoiceActivityPromotionState PromotionState,
    VoiceActivityLifecycleState LifecycleState,
    VoiceActivityHealthState PlanHealth,
    ulong ObserverDrops,
    int WarningCount);

internal interface IVoiceActivityDiagnosticSink
{
    bool TryWrite(VoiceActivityDiagnosticProjection projection);
}

internal sealed record VoiceActivityDiagnosticEvent(VoiceActivityDiagnosticProjection Projection) : Event
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;
    public override EventKind Kind { get; init; } = EventKind.Diagnostic;
    public override EventDirection Direction { get; init; } = EventDirection.Upstream;
}

internal sealed class VoiceActivityHpdEventDiagnosticSink : IVoiceActivityDiagnosticSink
{
    private readonly IEventCoordinator _events;

    internal VoiceActivityHpdEventDiagnosticSink(IEventCoordinator events) =>
        _events = events ?? throw new ArgumentNullException(nameof(events));

    public bool TryWrite(VoiceActivityDiagnosticProjection projection)
    {
        _events.Emit(new VoiceActivityDiagnosticEvent(projection));
        return true;
    }
}

internal sealed class VoiceActivityDiagnosticAdapter
{
    private readonly VoiceActivityObservationSubscription _subscription;
    private readonly IVoiceActivityDiagnosticSink _sink;

    internal VoiceActivityDiagnosticAdapter(VoiceActivityObservationSubscription subscription,
        IVoiceActivityDiagnosticSink sink)
    {
        _subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    internal ulong Exported { get; private set; }
    internal ulong Rejected { get; private set; }
    internal ulong Faulted { get; private set; }

    internal int Drain(int maximum)
    {
        if (maximum is < 1 or > 4_096) throw new ArgumentOutOfRangeException(nameof(maximum));
        var consumed = 0;
        while (consumed < maximum && _subscription.TryRead(out var snapshot))
        {
            consumed++;
            try
            {
                var projection = new VoiceActivityDiagnosticProjection(snapshot!.ProjectionSequence,
                    snapshot.Lifecycle.LifecycleRevision, snapshot.Lifecycle.Plan.PlanGeneration,
                    snapshot.Lifecycle.Plan.ConfigRevision, snapshot.PromotionState, snapshot.Lifecycle.State,
                    snapshot.Lifecycle.Plan.Health, snapshot.ObserverDrops, snapshot.Warnings.Count);
                if (_sink.TryWrite(projection)) Exported++; else Rejected++;
            }
            catch { Faulted++; }
        }
        return consumed;
    }
}
