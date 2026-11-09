using System.Collections.ObjectModel;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Endpointing;

internal readonly record struct TimerArmId
{
    private TimerArmId(StableId128 value) => Value = value;
    internal StableId128 Value { get; }
    internal bool IsValid => !Value.Equals(default);
    internal static TimerArmId Create() => new(StableId128.CreateRandom());
}

internal enum EndpointTimerKind : ushort
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

internal enum EndpointTimerTerminalDisposition : ushort
{
    Fired = 1,
    Cancelled = 2,
    Superseded = 3,
    Stale = 4,
}

internal sealed record EndpointTimerArm
{
    internal EndpointTimerArm(TimerArmId armId, EndpointTimerKind kind,
        ExpectedAuthorityVectorV1 authority, CandidateFamilyId familyId,
        EndpointCandidateId candidateId, uint evaluationRevision, uint planRevision,
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
    internal TimerArmId ArmId { get; }
    internal EndpointTimerKind Kind { get; }
    internal ExpectedAuthorityVectorV1 Authority { get; }
    internal CandidateFamilyId FamilyId { get; }
    internal EndpointCandidateId CandidateId { get; }
    internal uint EvaluationRevision { get; }
    internal uint PlanRevision { get; }
    internal ulong DueMonotonicNanoseconds { get; }
    internal ulong HardHorizonMonotonicNanoseconds { get; }
    internal ushort RearmCount { get; }
    internal ushort MaximumRearms { get; }
}

internal sealed record EndpointTimerTerminal(TimerArmId ArmId,
    EndpointTimerTerminalDisposition Disposition, ulong SequencedAtMonotonicNanoseconds,
    ulong? SchedulerFiredAtMonotonicNanoseconds);

internal sealed class EndpointTimerState
{
    private readonly ReadOnlyDictionary<TimerArmId, EndpointTimerArm> _active;
    private readonly ReadOnlyDictionary<TimerArmId, EndpointTimerTerminal> _terminal;
    internal EndpointTimerState(IDictionary<TimerArmId, EndpointTimerArm>? active = null,
        IDictionary<TimerArmId, EndpointTimerTerminal>? terminal = null)
    {
        _active = new(active is null ? new Dictionary<TimerArmId, EndpointTimerArm>() : new(active));
        _terminal = new(terminal is null ? new Dictionary<TimerArmId, EndpointTimerTerminal>() : new(terminal));
    }
    internal IReadOnlyDictionary<TimerArmId, EndpointTimerArm> Active => _active;
    internal IReadOnlyDictionary<TimerArmId, EndpointTimerTerminal> Terminal => _terminal;
}

internal abstract record EndpointTimerResult
{
    private EndpointTimerResult() { }
    internal sealed record Armed(EndpointTimerState State, EndpointTimerArm Arm) : EndpointTimerResult;
    internal sealed record Rearmed(EndpointTimerState State, EndpointTimerArm Arm) : EndpointTimerResult;
    internal sealed record Terminal(EndpointTimerState State, EndpointTimerTerminal Receipt) : EndpointTimerResult;
    internal sealed record Duplicate(EndpointTimerState State, EndpointTimerTerminal? Receipt) : EndpointTimerResult;
    internal sealed record Rejected(EndpointTimerState State, BoundedAscii SafeCode) : EndpointTimerResult;
}

internal static class EndpointTimerCoordinator
{
    internal static EndpointTimerState Create() => new();

    internal static EndpointTimerResult Arm(EndpointTimerState state, EndpointTimerArm arm,
        ushort maximumActiveArms, ushort maximumTerminalTombstones)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(arm);
        ValidateBounds(maximumActiveArms, maximumTerminalTombstones);
        if (state.Terminal.TryGetValue(arm.ArmId, out var terminal))
            return new EndpointTimerResult.Duplicate(state, terminal);
        if (state.Active.TryGetValue(arm.ArmId, out var existing))
            return existing == arm ? new EndpointTimerResult.Duplicate(state, null)
                : new EndpointTimerResult.Rejected(state, new BoundedAscii("timer-arm-identity-conflict"));
        if (state.Active.Count >= maximumActiveArms)
            return new EndpointTimerResult.Rejected(state, new BoundedAscii("timer-arm-capacity-refused"));
        var active = Copy(state.Active);
        active.Add(arm.ArmId, arm);
        return new EndpointTimerResult.Armed(new EndpointTimerState(active, Copy(state.Terminal)), arm);
    }

    internal static EndpointTimerResult Rearm(EndpointTimerState state, TimerArmId priorArmId,
        EndpointTimerArm nextArm, ulong sequencedAtMonotonicNanoseconds,
        ushort maximumActiveArms, ushort maximumTerminalTombstones)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(nextArm);
        ValidateBounds(maximumActiveArms, maximumTerminalTombstones);
        if (!state.Active.TryGetValue(priorArmId, out var prior))
            return new EndpointTimerResult.Rejected(state, new BoundedAscii("timer-prior-not-active"));
        if (prior.RearmCount >= prior.MaximumRearms)
            return new EndpointTimerResult.Rejected(state, new BoundedAscii("timer-rearm-bound"));
        if (nextArm.ArmId == priorArmId || nextArm.Authority != prior.Authority ||
            nextArm.FamilyId != prior.FamilyId || nextArm.CandidateId != prior.CandidateId ||
            nextArm.Kind != prior.Kind || nextArm.EvaluationRevision < prior.EvaluationRevision ||
            nextArm.PlanRevision < prior.PlanRevision || nextArm.HardHorizonMonotonicNanoseconds != prior.HardHorizonMonotonicNanoseconds ||
            nextArm.DueMonotonicNanoseconds < prior.DueMonotonicNanoseconds || nextArm.RearmCount != prior.RearmCount + 1 ||
            nextArm.MaximumRearms != prior.MaximumRearms)
            return new EndpointTimerResult.Rejected(state, new BoundedAscii("timer-rearm-invalid"));
        var terminalized = Terminalize(state, prior, EndpointTimerTerminalDisposition.Superseded,
            sequencedAtMonotonicNanoseconds, null, maximumTerminalTombstones);
        if (terminalized is EndpointTimerResult.Rejected rejected) return rejected;
        var terminalState = ((EndpointTimerResult.Terminal)terminalized).State;
        var active = Copy(terminalState.Active);
        active.Add(nextArm.ArmId, nextArm);
        return new EndpointTimerResult.Rearmed(new EndpointTimerState(active, Copy(terminalState.Terminal)), nextArm);
    }

    internal static EndpointTimerResult Fire(EndpointTimerState state, TimerArmId armId,
        ExpectedAuthorityVectorV1 authority, EndpointCandidateId candidateId,
        uint evaluationRevision, uint planRevision, ulong schedulerFiredAtMonotonicNanoseconds,
        ulong sequencedAtMonotonicNanoseconds, ushort maximumTerminalTombstones)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(authority);
        if (maximumTerminalTombstones == 0) throw new ArgumentOutOfRangeException(nameof(maximumTerminalTombstones));
        if (state.Terminal.TryGetValue(armId, out var prior)) return new EndpointTimerResult.Duplicate(state, prior);
        if (!state.Active.TryGetValue(armId, out var arm))
            return new EndpointTimerResult.Rejected(state, new BoundedAscii("timer-arm-not-active"));
        if (authority != arm.Authority || candidateId != arm.CandidateId || evaluationRevision != arm.EvaluationRevision || planRevision != arm.PlanRevision)
            return Terminalize(state, arm, EndpointTimerTerminalDisposition.Stale,
                sequencedAtMonotonicNanoseconds, schedulerFiredAtMonotonicNanoseconds, maximumTerminalTombstones);
        if (schedulerFiredAtMonotonicNanoseconds < arm.DueMonotonicNanoseconds)
            return new EndpointTimerResult.Rejected(state, new BoundedAscii("timer-fired-early"));
        return Terminalize(state, arm, EndpointTimerTerminalDisposition.Fired,
            sequencedAtMonotonicNanoseconds, schedulerFiredAtMonotonicNanoseconds, maximumTerminalTombstones);
    }

    internal static EndpointTimerResult Cancel(EndpointTimerState state, TimerArmId armId,
        ulong sequencedAtMonotonicNanoseconds, ushort maximumTerminalTombstones)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (maximumTerminalTombstones == 0) throw new ArgumentOutOfRangeException(nameof(maximumTerminalTombstones));
        if (state.Terminal.TryGetValue(armId, out var prior)) return new EndpointTimerResult.Duplicate(state, prior);
        if (!state.Active.TryGetValue(armId, out var arm))
            return new EndpointTimerResult.Rejected(state, new BoundedAscii("timer-arm-not-active"));
        return Terminalize(state, arm, EndpointTimerTerminalDisposition.Cancelled,
            sequencedAtMonotonicNanoseconds, null, maximumTerminalTombstones);
    }

    private static EndpointTimerResult Terminalize(EndpointTimerState state, EndpointTimerArm arm,
        EndpointTimerTerminalDisposition disposition, ulong sequencedAt, ulong? firedAt, ushort maximumTerminalTombstones)
    {
        if (sequencedAt == 0) throw new ArgumentOutOfRangeException(nameof(sequencedAt));
        if (state.Terminal.Count >= maximumTerminalTombstones)
            return new EndpointTimerResult.Rejected(state, new BoundedAscii("timer-tombstone-capacity-refused"));
        var active = Copy(state.Active);
        active.Remove(arm.ArmId);
        var terminal = Copy(state.Terminal);
        var receipt = new EndpointTimerTerminal(arm.ArmId, disposition, sequencedAt, firedAt);
        terminal.Add(arm.ArmId, receipt);
        return new EndpointTimerResult.Terminal(new EndpointTimerState(active, terminal), receipt);
    }

    private static Dictionary<TKey, TValue> Copy<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> source) where TKey : notnull =>
        source.ToDictionary(static entry => entry.Key, static entry => entry.Value);
    private static void ValidateBounds(ushort active, ushort terminal)
    { if (active == 0 || terminal == 0) throw new ArgumentOutOfRangeException(nameof(active)); }
}
