using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal enum GraphReplacementPhaseV1 : ushort { None = 1, Prepared = 2, Committed = 3, SourceSettled = 4 }

internal abstract record GraphReplacementCommandV1
{
    private GraphReplacementCommandV1() { }

    internal sealed record Prepare(OperationId OperationId, JournalPositionV1 ExpectedPredecessor,
        Hash256 SourceFingerprint, GraphTopologyPlanV1 TargetPlan, CapacityGrantSnapshotV1 TargetGrant,
        ExpectedAuthorityVectorV1 CurrentAuthority, MonotonicStampV1 ObservedAt,
        MonotonicStampV1 OverlapDeadline) : GraphReplacementCommandV1;

    internal sealed record Commit(OperationId OperationId, JournalPositionV1 ExpectedPreparationFact)
        : GraphReplacementCommandV1;

    internal sealed record SettleSource(OperationId OperationId, JournalPositionV1 ExpectedCommitFact,
        CapacityGrantSnapshotV1 SourceSettlement)
        : GraphReplacementCommandV1;
}

internal sealed record GraphReplacementStateV1
{
    private GraphReplacementStateV1(GraphTopologyPlanV1 sourcePlan, CapacityGrantSnapshotV1 sourceGrant,
        ExpectedAuthorityVectorV1 authority, JournalPositionV1 lastFact)
    {
        SourcePlan = sourcePlan; SourceGrant = sourceGrant; Authority = authority; LastFact = lastFact;
    }

    internal GraphReplacementPhaseV1 Phase { get; private init; } = GraphReplacementPhaseV1.None;
    internal GraphTopologyPlanV1 SourcePlan { get; }
    internal CapacityGrantSnapshotV1 SourceGrant { get; }
    internal GraphTopologyPlanV1? TargetPlan { get; private init; }
    internal ExpectedAuthorityVectorV1 Authority { get; private init; }
    internal JournalPositionV1 LastFact { get; private init; }
    internal OperationId ReplacementOperationId { get; private init; }
    internal GraphReplacementPrepareIdentityV1? Preparation { get; private init; }
    internal JournalPositionV1 CommitExpectedPredecessor { get; private init; }
    internal JournalPositionV1 SettlementExpectedPredecessor { get; private init; }
    internal JournalPositionV1 SettlementGrantFact { get; private init; }
    internal CapacityGrantStateV1 SettlementGrantState { get; private init; }

    internal static GraphReplacementStateV1 Create(GraphTopologyPlanV1 sourcePlan,
        CapacityGrantSnapshotV1 sourceGrant, ExpectedAuthorityVectorV1 authority, JournalPositionV1 lastFact)
    {
        ArgumentNullException.ThrowIfNull(sourcePlan);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(sourceGrant);
        if (!lastFact.IsValid || lastFact.Session != sourcePlan.Session || authority.Session != sourcePlan.Session ||
            !GraphReplacementReducerV1.HasExactGraph(authority, sourcePlan.GraphGeneration) ||
            !GraphReplacementReducerV1.GrantMatches(sourceGrant, sourcePlan, authority))
            throw new ArgumentException("The source plan, authority, and predecessor are not an exact graph scope.");
        return new(sourcePlan, sourceGrant, authority, lastFact);
    }

    internal GraphReplacementStateV1 WithPrepared(GraphTopologyPlanV1 target, OperationId operation,
        GraphReplacementPrepareIdentityV1 preparation, JournalPositionV1 fact) => this with
    {
        Phase = GraphReplacementPhaseV1.Prepared, TargetPlan = target, ReplacementOperationId = operation,
        Preparation = preparation, LastFact = fact
    };

    internal GraphReplacementStateV1 WithCommitted(ExpectedAuthorityVectorV1 authority,
        JournalPositionV1 expectedPredecessor, JournalPositionV1 fact) => this with
    { Phase = GraphReplacementPhaseV1.Committed, Authority = authority,
        CommitExpectedPredecessor = expectedPredecessor, LastFact = fact };

    internal GraphReplacementStateV1 WithSettled(JournalPositionV1 expectedPredecessor,
        CapacityGrantSnapshotV1 settlement, JournalPositionV1 fact) => this with
    { Phase = GraphReplacementPhaseV1.SourceSettled,
        SettlementExpectedPredecessor = expectedPredecessor, SettlementGrantFact = settlement.CurrentFact,
        SettlementGrantState = settlement.State, LastFact = fact };
}

internal sealed record GraphReplacementPrepareIdentityV1(OperationId OperationId,
    JournalPositionV1 ExpectedPredecessor, Hash256 SourceFingerprint, Hash256 TargetFingerprint,
    GraphGenerationId TargetGeneration, CapacityGrantId TargetGrantId, JournalPositionV1 TargetGrantFact,
    ExpectedAuthorityVectorV1 CurrentAuthority, MonotonicStampV1 ObservedAt, MonotonicStampV1 Deadline);

internal abstract record GraphReplacementReductionResultV1
{
    private GraphReplacementReductionResultV1() { }
    internal sealed record Applied(GraphReplacementStateV1 State) : GraphReplacementReductionResultV1;
    internal sealed record Idempotent(GraphReplacementStateV1 State) : GraphReplacementReductionResultV1;
    internal sealed record Rejected(GraphReplacementStateV1 State, BoundedAscii SafeCode) : GraphReplacementReductionResultV1;
    internal sealed record Conflict(GraphReplacementStateV1 State, JournalPositionV1 ActualPredecessor) : GraphReplacementReductionResultV1;
    internal sealed record GenerationReplaced(GraphReplacementStateV1 State, GraphGenerationId ActualGeneration) : GraphReplacementReductionResultV1;
}

internal static class GraphReplacementReducerV1
{
    internal const ulong MaximumOverlapNanoseconds = 30_000_000_000;

    internal static GraphReplacementReductionResultV1 Apply(GraphReplacementStateV1 state,
        GraphReplacementCommandV1? command, JournalPositionV1 admittedAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (command is null) return Reject(state, "replacement-command-missing");
        if (!admittedAt.IsValid || admittedAt.Session != state.SourcePlan.Session ||
            state.LastFact.Sequence == long.MaxValue || admittedAt.Sequence != state.LastFact.Sequence + 1)
            return Reject(state, "replacement-position-invalid");
        return command switch
        {
            GraphReplacementCommandV1.Prepare prepare => ApplyPrepare(state, prepare, admittedAt),
            GraphReplacementCommandV1.Commit commit => ApplyCommit(state, commit, admittedAt),
            GraphReplacementCommandV1.SettleSource settle => ApplySettle(state, settle, admittedAt),
            _ => Reject(state, "replacement-command-unknown")
        };
    }

    private static GraphReplacementReductionResultV1 ApplyPrepare(GraphReplacementStateV1 state,
        GraphReplacementCommandV1.Prepare command, JournalPositionV1 admittedAt)
    {
        var identity = Identity(command);
        if (state.Phase != GraphReplacementPhaseV1.None)
            return state.Preparation == identity && state.ReplacementOperationId == command.OperationId
                ? new GraphReplacementReductionResultV1.Idempotent(state)
                : new GraphReplacementReductionResultV1.Conflict(state, state.LastFact);
        if (!command.OperationId.IsValid || command.ExpectedPredecessor != state.LastFact)
            return command.ExpectedPredecessor.IsValid
                ? new GraphReplacementReductionResultV1.Conflict(state, state.LastFact)
                : Reject(state, "replacement-predecessor-invalid");
        if (command.CurrentAuthority is null || command.CurrentAuthority.Session != state.SourcePlan.Session)
            return Reject(state, "replacement-source-stale");
        if (!TryGraph(command.CurrentAuthority, out var actualGraph) || actualGraph != state.SourcePlan.GraphGeneration)
            return actualGraph.IsValid
                ? new GraphReplacementReductionResultV1.GenerationReplaced(state, actualGraph)
                : Reject(state, "replacement-graph-axis-invalid");
        if (command.SourceFingerprint != state.SourcePlan.Fingerprint || command.CurrentAuthority != state.Authority)
            return Reject(state, "replacement-source-stale");
        if (command.TargetPlan is null || command.TargetPlan.Session != state.SourcePlan.Session ||
            command.TargetPlan.GraphGeneration == state.SourcePlan.GraphGeneration)
            return Reject(state, "replacement-target-invalid");
        if (!GrantMatches(command.TargetGrant, command.TargetPlan, command.CurrentAuthority))
            return Reject(state, "replacement-grant-invalid");
        if (!OverlapValid(command.ObservedAt, command.OverlapDeadline))
            return Reject(state, "replacement-overlap-invalid");
        return new GraphReplacementReductionResultV1.Applied(
            state.WithPrepared(command.TargetPlan, command.OperationId, identity, admittedAt));
    }

    private static GraphReplacementReductionResultV1 ApplyCommit(GraphReplacementStateV1 state,
        GraphReplacementCommandV1.Commit command, JournalPositionV1 admittedAt)
    {
        if (state.Phase is GraphReplacementPhaseV1.Committed or GraphReplacementPhaseV1.SourceSettled)
            return state.ReplacementOperationId == command.OperationId &&
                state.CommitExpectedPredecessor == command.ExpectedPreparationFact
                ? new GraphReplacementReductionResultV1.Idempotent(state)
                : new GraphReplacementReductionResultV1.Conflict(state, state.LastFact);
        if (state.Phase != GraphReplacementPhaseV1.Prepared)
            return Reject(state, "replacement-not-prepared");
        if (command.OperationId != state.ReplacementOperationId)
            return new GraphReplacementReductionResultV1.Conflict(state, state.LastFact);
        if (command.ExpectedPreparationFact != state.LastFact)
            return command.ExpectedPreparationFact.IsValid
                ? new GraphReplacementReductionResultV1.Conflict(state, state.LastFact)
                : Reject(state, "replacement-preparation-fact-invalid");
        var authority = ReplaceGraph(state.Authority, state.TargetPlan!.GraphGeneration);
        return new GraphReplacementReductionResultV1.Applied(
            state.WithCommitted(authority, command.ExpectedPreparationFact, admittedAt));
    }

    private static GraphReplacementReductionResultV1 ApplySettle(GraphReplacementStateV1 state,
        GraphReplacementCommandV1.SettleSource command, JournalPositionV1 admittedAt)
    {
        if (state.Phase == GraphReplacementPhaseV1.SourceSettled)
            return state.ReplacementOperationId == command.OperationId &&
                state.SettlementExpectedPredecessor == command.ExpectedCommitFact &&
                command.SourceSettlement is not null &&
                state.SettlementGrantFact == command.SourceSettlement.CurrentFact &&
                state.SettlementGrantState == command.SourceSettlement.State
                ? new GraphReplacementReductionResultV1.Idempotent(state)
                : new GraphReplacementReductionResultV1.Conflict(state, state.LastFact);
        if (state.Phase != GraphReplacementPhaseV1.Committed)
            return Reject(state, "replacement-not-committed");
        if (command.OperationId != state.ReplacementOperationId || command.ExpectedCommitFact != state.LastFact)
            return command.OperationId == state.ReplacementOperationId && !command.ExpectedCommitFact.IsValid
                ? Reject(state, "replacement-commit-fact-invalid")
                : new GraphReplacementReductionResultV1.Conflict(state, state.LastFact);
        if (!SettlementMatches(command.SourceSettlement, state.SourceGrant, state.SourcePlan))
            return Reject(state, "replacement-source-settlement-invalid");
        return new GraphReplacementReductionResultV1.Applied(
            state.WithSettled(command.ExpectedCommitFact, command.SourceSettlement, admittedAt));
    }

    internal static bool HasExactGraph(ExpectedAuthorityVectorV1 authority, GraphGenerationId generation) =>
        TryGraph(authority, out var actual) && actual == generation;

    private static bool TryGraph(ExpectedAuthorityVectorV1 authority, out GraphGenerationId generation)
    {
        var entries = authority.Axes.Where(entry => entry.AxisId == AuthorityAxisId.Graph).ToArray();
        if (entries.Length == 1 && entries[0].Value is AuthorityAxisValueV1.Graph graph)
        { generation = graph.Value; return generation.IsValid; }
        generation = default; return false;
    }

    private static ExpectedAuthorityVectorV1 ReplaceGraph(ExpectedAuthorityVectorV1 current, GraphGenerationId target) =>
        ExpectedAuthorityVectorV1.Create(current.Session, current.Axes.Select(entry =>
            entry.AxisId == AuthorityAxisId.Graph ? new AuthorityAxisValueV1.Graph(target) : entry.Value));

    internal static bool GrantMatches(CapacityGrantSnapshotV1? grant, GraphTopologyPlanV1 plan,
        ExpectedAuthorityVectorV1 authority) => grant is not null && grant.GrantId == plan.CapacityGrantId &&
        grant.State == CapacityGrantStateV1.Active && grant.Authority == authority && grant.GrantedAt.IsValid &&
        grant.CurrentFact.IsValid && grant.GrantedAt.Session == plan.Session &&
        grant.CurrentFact.Session == plan.Session && grant.Balances is { Count: > 0 } &&
        grant.Balances.All(balance => balance is not null) && plan.CapacityDimensions.All(dimension =>
            grant.Balances.Any(balance => balance.Charge.DimensionId == dimension && balance.Active > 0));

    private static bool OverlapValid(MonotonicStampV1 observed, MonotonicStampV1 deadline) =>
        observed.IsValid && deadline.IsValid && observed.ClockDomainId == deadline.ClockDomainId &&
        observed.BootId == deadline.BootId && observed.Nanoseconds <= deadline.Nanoseconds &&
        deadline.Nanoseconds - observed.Nanoseconds <= MaximumOverlapNanoseconds;

    private static bool SettlementMatches(CapacityGrantSnapshotV1? settlement,
        CapacityGrantSnapshotV1 source, GraphTopologyPlanV1 plan) => settlement is not null &&
        settlement.GrantId == source.GrantId && settlement.GrantId == plan.CapacityGrantId &&
        settlement.Authority == source.Authority && settlement.GrantedAt == source.GrantedAt &&
        settlement.CurrentFact.IsValid && settlement.CurrentFact.Session == plan.Session &&
        settlement.CurrentFact.Sequence > source.CurrentFact.Sequence &&
        settlement.State is CapacityGrantStateV1.Settled or CapacityGrantStateV1.Revoked &&
        settlement.Balances is { Count: > 0 } && settlement.Balances.All(balance => balance is not null &&
            balance.Active == 0 && balance.Unactivated == 0 && balance.ExplicitlyUnknown == 0 &&
            balance.EncumberedNormal == 0 && balance.EncumberedReserve == 0) &&
        plan.CapacityDimensions.All(dimension => settlement.Balances.Any(balance => balance.Charge.DimensionId == dimension));

    private static GraphReplacementPrepareIdentityV1 Identity(GraphReplacementCommandV1.Prepare command) => new(
        command.OperationId, command.ExpectedPredecessor, command.SourceFingerprint,
        command.TargetPlan?.Fingerprint ?? default, command.TargetPlan?.GraphGeneration ?? default,
        command.TargetGrant?.GrantId ?? default, command.TargetGrant?.CurrentFact ?? default,
        command.CurrentAuthority, command.ObservedAt, command.OverlapDeadline);

    private static GraphReplacementReductionResultV1.Rejected Reject(GraphReplacementStateV1 state, string code) =>
        new(state, new BoundedAscii(code));
}
