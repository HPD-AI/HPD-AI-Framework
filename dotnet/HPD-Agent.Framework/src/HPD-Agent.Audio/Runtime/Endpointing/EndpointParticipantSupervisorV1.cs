using System.Collections.ObjectModel;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Endpointing;

internal sealed record EndpointCapacityChargeV1
{
    internal EndpointCapacityChargeV1(BoundedAscii dimension, long amount)
    {
        if (!dimension.IsValid) throw new ArgumentException("A dimension is required.", nameof(dimension));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        Dimension = dimension;
        Amount = amount;
    }
    internal BoundedAscii Dimension { get; }
    internal long Amount { get; }
}

internal sealed record EndpointParticipantPlanV1
{
    private readonly EndpointCapacityChargeV1[] _charges;
    internal EndpointParticipantPlanV1(ParticipantId participantId, TurnGenerationId generation,
        ExpectedAuthorityVectorV1 authority, IEnumerable<EndpointCapacityChargeV1> charges)
    {
        if (!participantId.IsValid || !generation.IsValid) throw new ArgumentException("Participant and generation are required.");
        Authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _charges = charges?.OrderBy(static charge => charge.Dimension.ToString(), StringComparer.Ordinal).ToArray()
            ?? throw new ArgumentNullException(nameof(charges));
        if (_charges.Length == 0 || _charges.Select(static charge => charge.Dimension).Distinct().Count() != _charges.Length)
            throw new ArgumentException("Charges must be non-empty and dimension-distinct.", nameof(charges));
        ParticipantId = participantId;
        Generation = generation;
        Charges = Array.AsReadOnly(_charges);
    }
    internal ParticipantId ParticipantId { get; }
    internal TurnGenerationId Generation { get; }
    internal ExpectedAuthorityVectorV1 Authority { get; }
    internal IReadOnlyList<EndpointCapacityChargeV1> Charges { get; }
}

internal enum EndpointParticipantStateV1 : ushort
{
    None = 1,
    Reserved = 2,
    Prepared = 3,
    Effective = 4,
    ReplacementPrepared = 5,
    Stopping = 6,
    Stopped = 7,
    Quarantined = 8,
}

internal sealed class EndpointCapacityLedgerV1
{
    private readonly ReadOnlyDictionary<BoundedAscii, long> _limits;
    private readonly ReadOnlyDictionary<BoundedAscii, long> _used;
    internal EndpointCapacityLedgerV1(IDictionary<BoundedAscii, long> limits, IDictionary<BoundedAscii, long>? used = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (limits.Count == 0 || limits.Any(static row => !row.Key.IsValid || row.Value <= 0))
            throw new ArgumentException("Capacity limits must be positive and named.", nameof(limits));
        var usedCopy = used is null ? limits.Keys.ToDictionary(static key => key, static _ => 0L) : new(used);
        if (usedCopy.Count != limits.Count || limits.Any(row => !usedCopy.TryGetValue(row.Key, out var value) || value < 0 || value > row.Value))
            throw new ArgumentException("Used capacity must exactly cover limits and remain conserved.", nameof(used));
        _limits = new(new Dictionary<BoundedAscii, long>(limits));
        _used = new(usedCopy);
    }
    internal IReadOnlyDictionary<BoundedAscii, long> Limits => _limits;
    internal IReadOnlyDictionary<BoundedAscii, long> Used => _used;
    internal bool CanAdd(IReadOnlyList<EndpointCapacityChargeV1> charges) => charges.All(charge =>
        _limits.TryGetValue(charge.Dimension, out var limit) &&
        _used.TryGetValue(charge.Dimension, out var used) && charge.Amount <= limit - used);
    internal EndpointCapacityLedgerV1 Add(IReadOnlyList<EndpointCapacityChargeV1> charges)
    {
        if (!CanAdd(charges)) throw new InvalidOperationException("Capacity is unavailable.");
        var used = new Dictionary<BoundedAscii, long>(_used);
        foreach (var charge in charges) used[charge.Dimension] = checked(used[charge.Dimension] + charge.Amount);
        return new EndpointCapacityLedgerV1(new Dictionary<BoundedAscii, long>(_limits), used);
    }
    internal EndpointCapacityLedgerV1 Remove(IReadOnlyList<EndpointCapacityChargeV1> charges)
    {
        var used = new Dictionary<BoundedAscii, long>(_used);
        foreach (var charge in charges)
        {
            if (!used.TryGetValue(charge.Dimension, out var current) || current < charge.Amount)
                throw new InvalidOperationException("Capacity release would violate conservation.");
            used[charge.Dimension] = current - charge.Amount;
        }
        return new EndpointCapacityLedgerV1(new Dictionary<BoundedAscii, long>(_limits), used);
    }
}

internal abstract record EndpointParticipantCommandV1
{
    private protected EndpointParticipantCommandV1(OperationId operationId, ulong expectedRevision)
    {
        if (!operationId.IsValid) throw new ArgumentException("An operation is required.", nameof(operationId));
        OperationId = operationId;
        ExpectedRevision = expectedRevision;
    }
    internal OperationId OperationId { get; }
    internal ulong ExpectedRevision { get; }
    internal sealed record Reserve : EndpointParticipantCommandV1
    { internal Reserve(OperationId o, ulong r, EndpointParticipantPlanV1 p) : base(o, r) => Plan = p ?? throw new ArgumentNullException(nameof(p)); internal EndpointParticipantPlanV1 Plan { get; } }
    internal sealed record Prepared(OperationId O, ulong R) : EndpointParticipantCommandV1(O, R);
    internal sealed record Effective(OperationId O, ulong R) : EndpointParticipantCommandV1(O, R);
    internal sealed record PrepareReplacement : EndpointParticipantCommandV1
    { internal PrepareReplacement(OperationId o, ulong r, EndpointParticipantPlanV1 p) : base(o, r) => Plan = p ?? throw new ArgumentNullException(nameof(p)); internal EndpointParticipantPlanV1 Plan { get; } }
    internal sealed record CommitReplacement(OperationId O, ulong R) : EndpointParticipantCommandV1(O, R);
    internal sealed record Stop(OperationId O, ulong R) : EndpointParticipantCommandV1(O, R);
    internal sealed record Stopped(OperationId O, ulong R) : EndpointParticipantCommandV1(O, R);
    internal sealed record Quarantine(OperationId O, ulong R, BoundedAscii SafeCode) : EndpointParticipantCommandV1(O, R);
}

internal sealed record EndpointParticipantSnapshotV1(ulong Revision, EndpointParticipantStateV1 State,
    EndpointParticipantPlanV1? CurrentPlan, EndpointParticipantPlanV1? PendingPlan,
    EndpointCapacityLedgerV1 Capacity, BoundedAscii? SafeCode);

internal sealed record EndpointParticipantReceiptV1(EndpointParticipantCommandV1 Command,
    EndpointParticipantSnapshotV1 Snapshot);

internal sealed class EndpointParticipantSupervisorStateV1
{
    private readonly ReadOnlyDictionary<OperationId, EndpointParticipantReceiptV1> _receipts;
    internal EndpointParticipantSupervisorStateV1(EndpointParticipantSnapshotV1 snapshot,
        IDictionary<OperationId, EndpointParticipantReceiptV1>? receipts = null)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _receipts = new(receipts is null ? new Dictionary<OperationId, EndpointParticipantReceiptV1>() : new(receipts));
    }
    internal EndpointParticipantSnapshotV1 Snapshot { get; }
    internal IReadOnlyDictionary<OperationId, EndpointParticipantReceiptV1> Receipts => _receipts;
}

internal abstract record EndpointParticipantResultV1
{
    private EndpointParticipantResultV1() { }
    internal sealed record Applied(EndpointParticipantSupervisorStateV1 State, EndpointParticipantReceiptV1 Receipt) : EndpointParticipantResultV1;
    internal sealed record Duplicate(EndpointParticipantSupervisorStateV1 State, EndpointParticipantReceiptV1 Receipt) : EndpointParticipantResultV1;
    internal sealed record Rejected(EndpointParticipantSupervisorStateV1 State, BoundedAscii SafeCode) : EndpointParticipantResultV1;
}

internal static class EndpointParticipantSupervisorV1
{
    internal static EndpointParticipantSupervisorStateV1 Create(EndpointCapacityLedgerV1 capacity) =>
        new(new EndpointParticipantSnapshotV1(0, EndpointParticipantStateV1.None, null, null,
            capacity ?? throw new ArgumentNullException(nameof(capacity)), null));

    internal static EndpointParticipantResultV1 Apply(EndpointParticipantSupervisorStateV1 state,
        EndpointParticipantCommandV1 command, ushort maximumReceipts)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        if (maximumReceipts == 0) throw new ArgumentOutOfRangeException(nameof(maximumReceipts));
        if (state.Receipts.TryGetValue(command.OperationId, out var prior))
            return prior.Command == command ? new EndpointParticipantResultV1.Duplicate(state, prior)
                : new EndpointParticipantResultV1.Rejected(state, new BoundedAscii("participant-operation-contradiction"));
        if (state.Receipts.Count >= maximumReceipts)
            return new EndpointParticipantResultV1.Rejected(state, new BoundedAscii("participant-receipt-capacity-refused"));
        if (command.ExpectedRevision != state.Snapshot.Revision)
            return new EndpointParticipantResultV1.Rejected(state, new BoundedAscii("participant-revision-conflict"));
        var current = state.Snapshot;
        EndpointParticipantSnapshotV1? next = command switch
        {
            EndpointParticipantCommandV1.Reserve reserve when current.State == EndpointParticipantStateV1.None && current.Capacity.CanAdd(reserve.Plan.Charges) =>
                new(current.Revision + 1, EndpointParticipantStateV1.Reserved, reserve.Plan, null, current.Capacity.Add(reserve.Plan.Charges), null),
            EndpointParticipantCommandV1.Prepared when current.State == EndpointParticipantStateV1.Reserved =>
                current with { Revision = current.Revision + 1, State = EndpointParticipantStateV1.Prepared },
            EndpointParticipantCommandV1.Effective when current.State == EndpointParticipantStateV1.Prepared =>
                current with { Revision = current.Revision + 1, State = EndpointParticipantStateV1.Effective },
            EndpointParticipantCommandV1.PrepareReplacement replacement when current.State == EndpointParticipantStateV1.Effective &&
                EligibleReplacement(current.CurrentPlan!, replacement.Plan) && current.Capacity.CanAdd(replacement.Plan.Charges) =>
                new(current.Revision + 1, EndpointParticipantStateV1.ReplacementPrepared, current.CurrentPlan, replacement.Plan,
                    current.Capacity.Add(replacement.Plan.Charges), null),
            EndpointParticipantCommandV1.CommitReplacement when current.State == EndpointParticipantStateV1.ReplacementPrepared =>
                new(current.Revision + 1, EndpointParticipantStateV1.Effective, current.PendingPlan, null,
                    current.Capacity.Remove(current.CurrentPlan!.Charges), null),
            EndpointParticipantCommandV1.Stop when current.State is EndpointParticipantStateV1.Effective or EndpointParticipantStateV1.ReplacementPrepared =>
                current with { Revision = current.Revision + 1, State = EndpointParticipantStateV1.Stopping },
            EndpointParticipantCommandV1.Stopped when current.State == EndpointParticipantStateV1.Stopping =>
                Stop(current),
            EndpointParticipantCommandV1.Quarantine quarantine when quarantine.SafeCode.IsValid && current.State != EndpointParticipantStateV1.Stopped =>
                current with { Revision = current.Revision + 1, State = EndpointParticipantStateV1.Quarantined, SafeCode = quarantine.SafeCode },
            _ => null,
        };
        if (next is null)
        {
            var code = command is EndpointParticipantCommandV1.Reserve reserve && !current.Capacity.CanAdd(reserve.Plan.Charges) ||
                command is EndpointParticipantCommandV1.PrepareReplacement replacement && !current.Capacity.CanAdd(replacement.Plan.Charges)
                ? "participant-capacity-refused" : "participant-transition-invalid";
            return new EndpointParticipantResultV1.Rejected(state, new BoundedAscii(code));
        }
        var receipt = new EndpointParticipantReceiptV1(command, next);
        var receipts = state.Receipts.ToDictionary(static row => row.Key, static row => row.Value);
        receipts.Add(command.OperationId, receipt);
        return new EndpointParticipantResultV1.Applied(new EndpointParticipantSupervisorStateV1(next, receipts), receipt);
    }

    private static bool EligibleReplacement(EndpointParticipantPlanV1 current, EndpointParticipantPlanV1 next) =>
        current.ParticipantId == next.ParticipantId && current.Authority == next.Authority && current.Generation != next.Generation;

    private static EndpointParticipantSnapshotV1 Stop(EndpointParticipantSnapshotV1 current)
    {
        var capacity = current.Capacity;
        if (current.CurrentPlan is not null) capacity = capacity.Remove(current.CurrentPlan.Charges);
        if (current.PendingPlan is not null) capacity = capacity.Remove(current.PendingPlan.Charges);
        return new(current.Revision + 1, EndpointParticipantStateV1.Stopped, null, null, capacity, null);
    }
}
