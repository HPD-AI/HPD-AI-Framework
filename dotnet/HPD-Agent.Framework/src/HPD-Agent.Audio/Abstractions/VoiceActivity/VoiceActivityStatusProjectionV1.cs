namespace HPD.Agent.Audio.VoiceActivity;

internal abstract record VoiceActivityProviderStatusUpdateResultV1
{
    private VoiceActivityProviderStatusUpdateResultV1() { }
    internal sealed record Applied(ProviderActivityVisibilityV1 Status) : VoiceActivityProviderStatusUpdateResultV1;
    internal sealed record Duplicate(ProviderActivityVisibilityV1 Status) : VoiceActivityProviderStatusUpdateResultV1;
    internal sealed record Stale(ProviderActivityVisibilityV1 Status) : VoiceActivityProviderStatusUpdateResultV1;
    internal sealed record Rejected(ProviderActivityVisibilityV1 Status, string SafeCode) :
        VoiceActivityProviderStatusUpdateResultV1;
}

internal sealed class VoiceActivityProviderStatusTrackerV1
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries;

    internal VoiceActivityProviderStatusTrackerV1(IReadOnlyDictionary<string, ulong> sourceGenerations)
    {
        ArgumentNullException.ThrowIfNull(sourceGenerations);
        _entries = sourceGenerations.ToDictionary(static row =>
            ActivitySourceRequestV1.RequireAscii(row.Key, nameof(sourceGenerations)), static row =>
            row.Value == 0 ? throw new ArgumentOutOfRangeException(nameof(sourceGenerations)) :
                new Entry(row.Value, 0, ProviderActivityVisibilityV1.Requested), StringComparer.Ordinal);
        if (_entries.Count == 0) throw new ArgumentException("At least one provider status source is required.");
    }

    internal IReadOnlyDictionary<string, ProviderActivityVisibilityV1> Snapshot
    {
        get
        {
            lock (_gate) return _entries.ToDictionary(static row => row.Key,
                static row => row.Value.Status, StringComparer.Ordinal);
        }
    }

    internal VoiceActivityProviderStatusUpdateResultV1 Apply(string sourceKey, ulong sourceGeneration,
        ulong sequence, ProviderActivityVisibilityV1 next)
    {
        sourceKey = ActivitySourceRequestV1.RequireAscii(sourceKey, nameof(sourceKey));
        if (sourceGeneration == 0 || sequence == 0 || !Enum.IsDefined(next)) throw new ArgumentOutOfRangeException();
        lock (_gate)
        {
            if (!_entries.TryGetValue(sourceKey, out var entry) || sourceGeneration < entry.Generation)
                return new VoiceActivityProviderStatusUpdateResultV1.Stale(
                    entry?.Status ?? ProviderActivityVisibilityV1.Unknown);
            if (sourceGeneration > entry.Generation)
            {
                if (next != ProviderActivityVisibilityV1.Requested)
                    return new VoiceActivityProviderStatusUpdateResultV1.Rejected(entry.Status,
                        "provider-generation-must-restart-requested");
                _entries[sourceKey] = new Entry(sourceGeneration, sequence, next);
                return new VoiceActivityProviderStatusUpdateResultV1.Applied(next);
            }
            if (sequence < entry.Sequence) return new VoiceActivityProviderStatusUpdateResultV1.Stale(entry.Status);
            if (sequence == entry.Sequence)
                return next == entry.Status
                    ? new VoiceActivityProviderStatusUpdateResultV1.Duplicate(entry.Status)
                    : new VoiceActivityProviderStatusUpdateResultV1.Rejected(entry.Status,
                        "provider-status-sequence-contradiction");
            if (!CanTransition(entry.Status, next))
                return new VoiceActivityProviderStatusUpdateResultV1.Rejected(entry.Status,
                    "provider-status-transition-invalid");
            entry.Sequence = sequence;
            entry.Status = next;
            return new VoiceActivityProviderStatusUpdateResultV1.Applied(next);
        }
    }

    private static bool CanTransition(ProviderActivityVisibilityV1 current,
        ProviderActivityVisibilityV1 next)
    {
        if (next == current) return true;
        if (next is ProviderActivityVisibilityV1.Rejected or ProviderActivityVisibilityV1.ReconnectRequired or
            ProviderActivityVisibilityV1.Unknown or ProviderActivityVisibilityV1.NotObservable)
            return true;
        return (current, next) switch
        {
            (ProviderActivityVisibilityV1.Requested, ProviderActivityVisibilityV1.Translated) => true,
            (ProviderActivityVisibilityV1.Translated, ProviderActivityVisibilityV1.AcceptedLocally) => true,
            (ProviderActivityVisibilityV1.AcceptedLocally, ProviderActivityVisibilityV1.Acknowledged) => true,
            (ProviderActivityVisibilityV1.Acknowledged, ProviderActivityVisibilityV1.ObservedConsistent) => true,
            (ProviderActivityVisibilityV1.Unknown, ProviderActivityVisibilityV1.Acknowledged) => true,
            (ProviderActivityVisibilityV1.ReconnectRequired, ProviderActivityVisibilityV1.Requested) => false,
            _ => false,
        };
    }

    private sealed class Entry(ulong generation, ulong sequence, ProviderActivityVisibilityV1 status)
    {
        internal ulong Generation { get; } = generation;
        internal ulong Sequence { get; set; } = sequence;
        internal ProviderActivityVisibilityV1 Status { get; set; } = status;
    }
}

internal readonly record struct VoiceActivityDiagnosticProjectionV1(
    ulong ProjectionSequence,
    ulong LifecycleRevision,
    ulong PlanGeneration,
    ulong ConfigRevision,
    VoiceActivityPromotionStateV1 PromotionState,
    VoiceActivityLifecycleStateV1 LifecycleState,
    VoiceActivityHealthStateV1 PlanHealth,
    ulong ObserverDrops,
    int WarningCount);

internal interface IVoiceActivityDiagnosticSinkV1
{
    bool TryWrite(VoiceActivityDiagnosticProjectionV1 projection);
}

internal sealed class VoiceActivityDiagnosticAdapterV1
{
    private readonly VoiceActivityObservationSubscriptionV1 _subscription;
    private readonly IVoiceActivityDiagnosticSinkV1 _sink;

    internal VoiceActivityDiagnosticAdapterV1(VoiceActivityObservationSubscriptionV1 subscription,
        IVoiceActivityDiagnosticSinkV1 sink)
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
                var projection = new VoiceActivityDiagnosticProjectionV1(snapshot!.ProjectionSequence,
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
