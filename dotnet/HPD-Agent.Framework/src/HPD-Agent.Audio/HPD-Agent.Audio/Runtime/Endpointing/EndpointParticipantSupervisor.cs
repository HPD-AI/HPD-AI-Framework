using System.Collections.ObjectModel;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Endpointing;

internal sealed record EndpointCapacityCharge
{
    internal EndpointCapacityCharge(BoundedAscii dimension, long amount)
    {
        if (!dimension.IsValid) throw new ArgumentException("A dimension is required.", nameof(dimension));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        Dimension = dimension;
        Amount = amount;
    }
    internal BoundedAscii Dimension { get; }
    internal long Amount { get; }
}

internal sealed record EndpointParticipantPlan
{
    private readonly EndpointCapacityCharge[] _charges;
    internal EndpointParticipantPlan(ParticipantId participantId, TurnGenerationId generation,
        ExpectedAuthorityVectorV1 authority, IEnumerable<EndpointCapacityCharge> charges)
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
    internal IReadOnlyList<EndpointCapacityCharge> Charges { get; }
}

internal enum EndpointParticipantState : ushort
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

internal sealed class EndpointCapacityLedger
{
    private readonly ReadOnlyDictionary<BoundedAscii, long> _limits;
    private readonly ReadOnlyDictionary<BoundedAscii, long> _used;
    internal EndpointCapacityLedger(IDictionary<BoundedAscii, long> limits, IDictionary<BoundedAscii, long>? used = null)
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
    internal bool CanAdd(IReadOnlyList<EndpointCapacityCharge> charges) => charges.All(charge =>
        _limits.TryGetValue(charge.Dimension, out var limit) &&
        _used.TryGetValue(charge.Dimension, out var used) && charge.Amount <= limit - used);
    internal EndpointCapacityLedger Add(IReadOnlyList<EndpointCapacityCharge> charges)
    {
        if (!CanAdd(charges)) throw new InvalidOperationException("Capacity is unavailable.");
        var used = new Dictionary<BoundedAscii, long>(_used);
        foreach (var charge in charges) used[charge.Dimension] = checked(used[charge.Dimension] + charge.Amount);
        return new EndpointCapacityLedger(new Dictionary<BoundedAscii, long>(_limits), used);
    }
    internal EndpointCapacityLedger Remove(IReadOnlyList<EndpointCapacityCharge> charges)
    {
        var used = new Dictionary<BoundedAscii, long>(_used);
        foreach (var charge in charges)
        {
            if (!used.TryGetValue(charge.Dimension, out var current) || current < charge.Amount)
                throw new InvalidOperationException("Capacity release would violate conservation.");
            used[charge.Dimension] = current - charge.Amount;
        }
        return new EndpointCapacityLedger(new Dictionary<BoundedAscii, long>(_limits), used);
    }
}

internal abstract record EndpointParticipantCommand
{
    private protected EndpointParticipantCommand(OperationId operationId, ulong expectedRevision)
    {
        if (!operationId.IsValid) throw new ArgumentException("An operation is required.", nameof(operationId));
        OperationId = operationId;
        ExpectedRevision = expectedRevision;
    }
    internal OperationId OperationId { get; }
    internal ulong ExpectedRevision { get; }
    internal sealed record Reserve : EndpointParticipantCommand
    { internal Reserve(OperationId o, ulong r, EndpointParticipantPlan p) : base(o, r) => Plan = p ?? throw new ArgumentNullException(nameof(p)); internal EndpointParticipantPlan Plan { get; } }
    internal sealed record Prepared(OperationId O, ulong R) : EndpointParticipantCommand(O, R);
    internal sealed record Effective(OperationId O, ulong R) : EndpointParticipantCommand(O, R);
    internal sealed record PrepareReplacement : EndpointParticipantCommand
    { internal PrepareReplacement(OperationId o, ulong r, EndpointParticipantPlan p) : base(o, r) => Plan = p ?? throw new ArgumentNullException(nameof(p)); internal EndpointParticipantPlan Plan { get; } }
    internal sealed record CommitReplacement(OperationId O, ulong R) : EndpointParticipantCommand(O, R);
    internal sealed record Stop(OperationId O, ulong R) : EndpointParticipantCommand(O, R);
    internal sealed record Stopped(OperationId O, ulong R) : EndpointParticipantCommand(O, R);
    internal sealed record Quarantine(OperationId O, ulong R, BoundedAscii SafeCode) : EndpointParticipantCommand(O, R);
}

internal sealed record EndpointParticipantSnapshot(ulong Revision, EndpointParticipantState State,
    EndpointParticipantPlan? CurrentPlan, EndpointParticipantPlan? PendingPlan,
    EndpointCapacityLedger Capacity, BoundedAscii? SafeCode);

internal sealed record EndpointParticipantReceipt(EndpointParticipantCommand Command,
    EndpointParticipantSnapshot Snapshot);

internal sealed class EndpointParticipantSupervisorState
{
    private readonly ReadOnlyDictionary<OperationId, EndpointParticipantReceipt> _receipts;
    internal EndpointParticipantSupervisorState(EndpointParticipantSnapshot snapshot,
        IDictionary<OperationId, EndpointParticipantReceipt>? receipts = null)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _receipts = new(receipts is null ? new Dictionary<OperationId, EndpointParticipantReceipt>() : new(receipts));
    }
    internal EndpointParticipantSnapshot Snapshot { get; }
    internal IReadOnlyDictionary<OperationId, EndpointParticipantReceipt> Receipts => _receipts;
}

internal abstract record EndpointParticipantResult
{
    private EndpointParticipantResult() { }
    internal sealed record Applied(EndpointParticipantSupervisorState State, EndpointParticipantReceipt Receipt) : EndpointParticipantResult;
    internal sealed record Duplicate(EndpointParticipantSupervisorState State, EndpointParticipantReceipt Receipt) : EndpointParticipantResult;
    internal sealed record Rejected(EndpointParticipantSupervisorState State, BoundedAscii SafeCode) : EndpointParticipantResult;
}

internal static class EndpointParticipantSupervisor
{
    internal static EndpointParticipantSupervisorState Create(EndpointCapacityLedger capacity) =>
        new(new EndpointParticipantSnapshot(0, EndpointParticipantState.None, null, null,
            capacity ?? throw new ArgumentNullException(nameof(capacity)), null));

    internal static EndpointParticipantResult Apply(EndpointParticipantSupervisorState state,
        EndpointParticipantCommand command, ushort maximumReceipts)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        if (maximumReceipts == 0) throw new ArgumentOutOfRangeException(nameof(maximumReceipts));
        if (state.Receipts.TryGetValue(command.OperationId, out var prior))
            return prior.Command == command ? new EndpointParticipantResult.Duplicate(state, prior)
                : new EndpointParticipantResult.Rejected(state, new BoundedAscii("participant-operation-contradiction"));
        if (state.Receipts.Count >= maximumReceipts)
            return new EndpointParticipantResult.Rejected(state, new BoundedAscii("participant-receipt-capacity-refused"));
        if (command.ExpectedRevision != state.Snapshot.Revision)
            return new EndpointParticipantResult.Rejected(state, new BoundedAscii("participant-revision-conflict"));
        var current = state.Snapshot;
        EndpointParticipantSnapshot? next = command switch
        {
            EndpointParticipantCommand.Reserve reserve when current.State == EndpointParticipantState.None && current.Capacity.CanAdd(reserve.Plan.Charges) =>
                new(current.Revision + 1, EndpointParticipantState.Reserved, reserve.Plan, null, current.Capacity.Add(reserve.Plan.Charges), null),
            EndpointParticipantCommand.Prepared when current.State == EndpointParticipantState.Reserved =>
                current with { Revision = current.Revision + 1, State = EndpointParticipantState.Prepared },
            EndpointParticipantCommand.Effective when current.State == EndpointParticipantState.Prepared =>
                current with { Revision = current.Revision + 1, State = EndpointParticipantState.Effective },
            EndpointParticipantCommand.PrepareReplacement replacement when current.State == EndpointParticipantState.Effective &&
                EligibleReplacement(current.CurrentPlan!, replacement.Plan) && current.Capacity.CanAdd(replacement.Plan.Charges) =>
                new(current.Revision + 1, EndpointParticipantState.ReplacementPrepared, current.CurrentPlan, replacement.Plan,
                    current.Capacity.Add(replacement.Plan.Charges), null),
            EndpointParticipantCommand.CommitReplacement when current.State == EndpointParticipantState.ReplacementPrepared =>
                new(current.Revision + 1, EndpointParticipantState.Effective, current.PendingPlan, null,
                    current.Capacity.Remove(current.CurrentPlan!.Charges), null),
            EndpointParticipantCommand.Stop when current.State is EndpointParticipantState.Effective or EndpointParticipantState.ReplacementPrepared =>
                current with { Revision = current.Revision + 1, State = EndpointParticipantState.Stopping },
            EndpointParticipantCommand.Stopped when current.State == EndpointParticipantState.Stopping =>
                Stop(current),
            EndpointParticipantCommand.Quarantine quarantine when quarantine.SafeCode.IsValid && current.State != EndpointParticipantState.Stopped =>
                current with { Revision = current.Revision + 1, State = EndpointParticipantState.Quarantined, SafeCode = quarantine.SafeCode },
            _ => null,
        };
        if (next is null)
        {
            var code = command is EndpointParticipantCommand.Reserve reserve && !current.Capacity.CanAdd(reserve.Plan.Charges) ||
                command is EndpointParticipantCommand.PrepareReplacement replacement && !current.Capacity.CanAdd(replacement.Plan.Charges)
                ? "participant-capacity-refused" : "participant-transition-invalid";
            return new EndpointParticipantResult.Rejected(state, new BoundedAscii(code));
        }
        var receipt = new EndpointParticipantReceipt(command, next);
        var receipts = state.Receipts.ToDictionary(static row => row.Key, static row => row.Value);
        receipts.Add(command.OperationId, receipt);
        return new EndpointParticipantResult.Applied(new EndpointParticipantSupervisorState(next, receipts), receipt);
    }

    private static bool EligibleReplacement(EndpointParticipantPlan current, EndpointParticipantPlan next) =>
        current.ParticipantId == next.ParticipantId && current.Authority == next.Authority && current.Generation != next.Generation;

    private static EndpointParticipantSnapshot Stop(EndpointParticipantSnapshot current)
    {
        var capacity = current.Capacity;
        if (current.CurrentPlan is not null) capacity = capacity.Remove(current.CurrentPlan.Charges);
        if (current.PendingPlan is not null) capacity = capacity.Remove(current.PendingPlan.Charges);
        return new(current.Revision + 1, EndpointParticipantState.Stopped, null, null, capacity, null);
    }
}
