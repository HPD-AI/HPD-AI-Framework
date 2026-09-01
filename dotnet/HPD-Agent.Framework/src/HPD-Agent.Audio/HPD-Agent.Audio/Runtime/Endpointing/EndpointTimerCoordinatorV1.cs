using System.Collections.ObjectModel;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Endpointing;

internal readonly record struct TimerArmIdV1
{
    private TimerArmIdV1(StableId128 value) => Value = value;
    internal StableId128 Value { get; }
    internal bool IsValid => !Value.Equals(default);
    internal static TimerArmIdV1 Create() => new(StableId128.CreateRandom());
}

internal enum EndpointTimerKindV1 : ushort
{
    ActivityClose = 1,
    CadenceFloor = 2,
    SttFinality = 3,
    TranscriptInactivity = 4,
    SemanticEot = 5,
    ProviderEndpoint = 6,
    CorrectionGrace = 7,
    Manual = 8,
    BackchannelVerdict = 9,
    Speculation = 10,
    MaximumTurn = 11,
    CandidateHorizon = 12,
    ProviderOperation = 13,
    SemanticHandoff = 14,
}

internal enum EndpointTimerTerminalDispositionV1 : ushort
{
    Fired = 1,
    Cancelled = 2,
    Superseded = 3,
    Stale = 4,
}

internal sealed record EndpointTimerArmV1
{
    internal EndpointTimerArmV1(TimerArmIdV1 armId, EndpointTimerKindV1 kind,
        ExpectedAuthorityVectorV1 authority, CandidateFamilyIdV1 familyId,
        EndpointCandidateIdV1 candidateId, uint evaluationRevision, uint planRevision,
        ulong dueMonotonicNanoseconds, ulong hardHorizonMonotonicNanoseconds,
        ushort rearmCount, ushort maximumRearms)
    {
        if (!armId.IsValid || !Enum.IsDefined(kind) || authority is null || !familyId.IsValid ||
            !candidateId.IsValid || evaluationRevision == 0 || planRevision == 0 ||
            dueMonotonicNanoseconds == 0 || hardHorizonMonotonicNanoseconds == 0 ||
            dueMonotonicNanoseconds > hardHorizonMonotonicNanoseconds || rearmCount > maximumRearms)
            throw new ArgumentException("Timer arm is outside the closed bounded contract.");
        ArmId = armId;
        Kind = kind;
        Authority = authority;
        FamilyId = familyId;
        CandidateId = candidateId;
        EvaluationRevision = evaluationRevision;
        PlanRevision = planRevision;
        DueMonotonicNanoseconds = dueMonotonicNanoseconds;
        HardHorizonMonotonicNanoseconds = hardHorizonMonotonicNanoseconds;
        RearmCount = rearmCount;
        MaximumRearms = maximumRearms;
    }
    internal TimerArmIdV1 ArmId { get; }
    internal EndpointTimerKindV1 Kind { get; }
    internal ExpectedAuthorityVectorV1 Authority { get; }
    internal CandidateFamilyIdV1 FamilyId { get; }
    internal EndpointCandidateIdV1 CandidateId { get; }
    internal uint EvaluationRevision { get; }
    internal uint PlanRevision { get; }
    internal ulong DueMonotonicNanoseconds { get; }
    internal ulong HardHorizonMonotonicNanoseconds { get; }
    internal ushort RearmCount { get; }
    internal ushort MaximumRearms { get; }
}

internal sealed record EndpointTimerTerminalV1(TimerArmIdV1 ArmId,
    EndpointTimerTerminalDispositionV1 Disposition, ulong SequencedAtMonotonicNanoseconds,
    ulong? SchedulerFiredAtMonotonicNanoseconds);

internal sealed class EndpointTimerStateV1
{
    private readonly ReadOnlyDictionary<TimerArmIdV1, EndpointTimerArmV1> _active;
    private readonly ReadOnlyDictionary<TimerArmIdV1, EndpointTimerTerminalV1> _terminal;
    internal EndpointTimerStateV1(IDictionary<TimerArmIdV1, EndpointTimerArmV1>? active = null,
        IDictionary<TimerArmIdV1, EndpointTimerTerminalV1>? terminal = null)
    {
        _active = new(active is null ? new Dictionary<TimerArmIdV1, EndpointTimerArmV1>() : new(active));
        _terminal = new(terminal is null ? new Dictionary<TimerArmIdV1, EndpointTimerTerminalV1>() : new(terminal));
    }
    internal IReadOnlyDictionary<TimerArmIdV1, EndpointTimerArmV1> Active => _active;
    internal IReadOnlyDictionary<TimerArmIdV1, EndpointTimerTerminalV1> Terminal => _terminal;
}

internal abstract record EndpointTimerResultV1
{
    private EndpointTimerResultV1() { }
    internal sealed record Armed(EndpointTimerStateV1 State, EndpointTimerArmV1 Arm) : EndpointTimerResultV1;
    internal sealed record Rearmed(EndpointTimerStateV1 State, EndpointTimerArmV1 Arm) : EndpointTimerResultV1;
    internal sealed record Terminal(EndpointTimerStateV1 State, EndpointTimerTerminalV1 Receipt) : EndpointTimerResultV1;
    internal sealed record Duplicate(EndpointTimerStateV1 State, EndpointTimerTerminalV1? Receipt) : EndpointTimerResultV1;
    internal sealed record Rejected(EndpointTimerStateV1 State, BoundedAscii SafeCode) : EndpointTimerResultV1;
}

internal static class EndpointTimerCoordinatorV1
{
    internal static EndpointTimerStateV1 Create() => new();

    internal static EndpointTimerResultV1 Arm(EndpointTimerStateV1 state, EndpointTimerArmV1 arm,
        ushort maximumActiveArms, ushort maximumTerminalTombstones)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(arm);
        ValidateBounds(maximumActiveArms, maximumTerminalTombstones);
        if (state.Terminal.TryGetValue(arm.ArmId, out var terminal))
            return new EndpointTimerResultV1.Duplicate(state, terminal);
        if (state.Active.TryGetValue(arm.ArmId, out var existing))
            return existing == arm ? new EndpointTimerResultV1.Duplicate(state, null)
                : new EndpointTimerResultV1.Rejected(state, new BoundedAscii("timer-arm-identity-conflict"));
        if (state.Active.Count >= maximumActiveArms)
            return new EndpointTimerResultV1.Rejected(state, new BoundedAscii("timer-arm-capacity-refused"));
        var active = Copy(state.Active);
        active.Add(arm.ArmId, arm);
        return new EndpointTimerResultV1.Armed(new EndpointTimerStateV1(active, Copy(state.Terminal)), arm);
    }

    internal static EndpointTimerResultV1 Rearm(EndpointTimerStateV1 state, TimerArmIdV1 priorArmId,
        EndpointTimerArmV1 nextArm, ulong sequencedAtMonotonicNanoseconds,
        ushort maximumActiveArms, ushort maximumTerminalTombstones)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(nextArm);
        ValidateBounds(maximumActiveArms, maximumTerminalTombstones);
        if (!state.Active.TryGetValue(priorArmId, out var prior))
            return new EndpointTimerResultV1.Rejected(state, new BoundedAscii("timer-prior-not-active"));
        if (prior.RearmCount >= prior.MaximumRearms)
            return new EndpointTimerResultV1.Rejected(state, new BoundedAscii("timer-rearm-bound"));
        if (nextArm.ArmId == priorArmId || nextArm.Authority != prior.Authority ||
            nextArm.FamilyId != prior.FamilyId || nextArm.CandidateId != prior.CandidateId ||
            nextArm.Kind != prior.Kind || nextArm.EvaluationRevision < prior.EvaluationRevision ||
            nextArm.PlanRevision < prior.PlanRevision || nextArm.HardHorizonMonotonicNanoseconds != prior.HardHorizonMonotonicNanoseconds ||
            nextArm.DueMonotonicNanoseconds < prior.DueMonotonicNanoseconds || nextArm.RearmCount != prior.RearmCount + 1 ||
            nextArm.MaximumRearms != prior.MaximumRearms)
            return new EndpointTimerResultV1.Rejected(state, new BoundedAscii("timer-rearm-invalid"));
        var terminalized = Terminalize(state, prior, EndpointTimerTerminalDispositionV1.Superseded,
            sequencedAtMonotonicNanoseconds, null, maximumTerminalTombstones);
        if (terminalized is EndpointTimerResultV1.Rejected rejected) return rejected;
        var terminalState = ((EndpointTimerResultV1.Terminal)terminalized).State;
        var active = Copy(terminalState.Active);
        active.Add(nextArm.ArmId, nextArm);
        return new EndpointTimerResultV1.Rearmed(new EndpointTimerStateV1(active, Copy(terminalState.Terminal)), nextArm);
    }

    internal static EndpointTimerResultV1 Fire(EndpointTimerStateV1 state, TimerArmIdV1 armId,
        ExpectedAuthorityVectorV1 authority, EndpointCandidateIdV1 candidateId,
        uint evaluationRevision, uint planRevision, ulong schedulerFiredAtMonotonicNanoseconds,
        ulong sequencedAtMonotonicNanoseconds, ushort maximumTerminalTombstones)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(authority);
        if (maximumTerminalTombstones == 0) throw new ArgumentOutOfRangeException(nameof(maximumTerminalTombstones));
        if (state.Terminal.TryGetValue(armId, out var prior)) return new EndpointTimerResultV1.Duplicate(state, prior);
        if (!state.Active.TryGetValue(armId, out var arm))
            return new EndpointTimerResultV1.Rejected(state, new BoundedAscii("timer-arm-not-active"));
        if (authority != arm.Authority || candidateId != arm.CandidateId || evaluationRevision != arm.EvaluationRevision || planRevision != arm.PlanRevision)
            return Terminalize(state, arm, EndpointTimerTerminalDispositionV1.Stale,
                sequencedAtMonotonicNanoseconds, schedulerFiredAtMonotonicNanoseconds, maximumTerminalTombstones);
        if (schedulerFiredAtMonotonicNanoseconds < arm.DueMonotonicNanoseconds)
            return new EndpointTimerResultV1.Rejected(state, new BoundedAscii("timer-fired-early"));
        return Terminalize(state, arm, EndpointTimerTerminalDispositionV1.Fired,
            sequencedAtMonotonicNanoseconds, schedulerFiredAtMonotonicNanoseconds, maximumTerminalTombstones);
    }

    internal static EndpointTimerResultV1 Cancel(EndpointTimerStateV1 state, TimerArmIdV1 armId,
        ulong sequencedAtMonotonicNanoseconds, ushort maximumTerminalTombstones)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (maximumTerminalTombstones == 0) throw new ArgumentOutOfRangeException(nameof(maximumTerminalTombstones));
        if (state.Terminal.TryGetValue(armId, out var prior)) return new EndpointTimerResultV1.Duplicate(state, prior);
        if (!state.Active.TryGetValue(armId, out var arm))
            return new EndpointTimerResultV1.Rejected(state, new BoundedAscii("timer-arm-not-active"));
        return Terminalize(state, arm, EndpointTimerTerminalDispositionV1.Cancelled,
            sequencedAtMonotonicNanoseconds, null, maximumTerminalTombstones);
    }

    private static EndpointTimerResultV1 Terminalize(EndpointTimerStateV1 state, EndpointTimerArmV1 arm,
        EndpointTimerTerminalDispositionV1 disposition, ulong sequencedAt, ulong? firedAt, ushort maximumTerminalTombstones)
    {
        if (sequencedAt == 0) throw new ArgumentOutOfRangeException(nameof(sequencedAt));
        if (state.Terminal.Count >= maximumTerminalTombstones)
            return new EndpointTimerResultV1.Rejected(state, new BoundedAscii("timer-tombstone-capacity-refused"));
        var active = Copy(state.Active);
        active.Remove(arm.ArmId);
        var terminal = Copy(state.Terminal);
        var receipt = new EndpointTimerTerminalV1(arm.ArmId, disposition, sequencedAt, firedAt);
        terminal.Add(arm.ArmId, receipt);
        return new EndpointTimerResultV1.Terminal(new EndpointTimerStateV1(active, terminal), receipt);
    }

    private static Dictionary<TKey, TValue> Copy<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> source) where TKey : notnull =>
        source.ToDictionary(static entry => entry.Key, static entry => entry.Value);
    private static void ValidateBounds(ushort active, ushort terminal)
    { if (active == 0 || terminal == 0) throw new ArgumentOutOfRangeException(nameof(active)); }
}
