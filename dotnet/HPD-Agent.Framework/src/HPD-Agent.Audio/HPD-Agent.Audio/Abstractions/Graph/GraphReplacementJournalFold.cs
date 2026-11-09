using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal sealed record PendingGraphReplacementCommand(
    AuthorityFactEnvelopeV1 Envelope,
    GraphOwnerPayload Outer,
    GraphReplacementJournalCommand Command,
    CapacityGrantSnapshotV1? ReferencedGrant);

internal abstract record GraphReplacementJournalFoldResult
{
    private GraphReplacementJournalFoldResult() { }

    internal sealed record Current(long SnapshotThrough, CurrentAuthorityVectorSnapshotV1 Authority,
        GraphReplacementState? State, IReadOnlyList<PendingGraphReplacementCommand> PendingCommands,
        AuthorityFactEnvelopeV1? TargetCommandFact = null,
        AuthorityFactEnvelopeV1? TargetResultFact = null,
        AuthorityFactEnvelopeV1? InstallationFact = null,
        GraphReplacementSnapshot? Wire = null,
        AuthorityFactEnvelopeV1? TargetTransitionFact = null) : GraphReplacementJournalFoldResult;

    internal sealed record RuntimeReplaced(RuntimeGenerationId Replacement, long LastPosition)
        : GraphReplacementJournalFoldResult;

    internal sealed record InvalidHistory(BoundedAscii SafeCode, long LastVerifiedPosition)
        : GraphReplacementJournalFoldResult;

    internal sealed record AtomicCommitIncomplete(long LastVerifiedPosition)
        : GraphReplacementJournalFoldResult;
}

internal abstract record GraphReplacementJournalInspection
{
    private GraphReplacementJournalInspection() { }
    internal sealed record Other(AuthorityFactEnvelopeV1 Envelope) : GraphReplacementJournalInspection;
    internal sealed record Installed(AuthorityFactEnvelopeV1 Envelope, GraphOwnerPayload Outer,
        GraphTopologyInstalled Body) : GraphReplacementJournalInspection;
    internal sealed record Command(AuthorityFactEnvelopeV1 Envelope, GraphOwnerPayload Outer,
        GraphReplacementJournalCommand Body) : GraphReplacementJournalInspection;
    internal sealed record Fact(AuthorityFactEnvelopeV1 Envelope, GraphOwnerPayload Outer,
        GraphReplacementFact Body) : GraphReplacementJournalInspection;
    internal sealed record Invalid(AuthorityFactEnvelopeV1? Envelope, BoundedAscii SafeCode)
        : GraphReplacementJournalInspection;

    internal JournalPositionV1? CapacityReference => this switch
    {
        Installed installed => installed.Body.ActiveSourceGrantFact,
        Command { Body: GraphReplacementJournalCommand.Prepare prepare } => prepare.TargetGrantFact,
        Command { Body: GraphReplacementJournalCommand.SettleSource settle } => settle.SourceSettlementFact,
        _ => null,
    };
}

internal static class GraphReplacementJournalFold
{
    internal const int MaximumPendingCommands = 256;
    private static readonly AuthorityPayloadRegistrationV1 CommandRegistration = GraphReplacementPayloadRegistrations.Command;
    private static readonly AuthorityPayloadRegistrationV1 InstalledRegistration = GraphReplacementPayloadRegistrations.Installed;
    private static readonly AuthorityPayloadRegistrationV1 FactRegistration = GraphReplacementPayloadRegistrations.Fact;
    private static readonly SchemaReferenceV1 GraphTransitionSchema = AuthorityGenerationTransitionCodecV1.SchemaFor(AuthorityAxisId.Graph);

    internal static Accumulator CreateAccumulator(SessionAuthorityStampV1 session,
        JournalFactId? targetCommandFactId = null) => new(session, targetCommandFactId);

    internal static GraphReplacementJournalFoldResult Fold(SessionAuthorityStampV1 session,
        IEnumerable<(AuthorityFactEnvelopeV1 Envelope, CapacityGrantSnapshotV1? Proof)> facts,
        JournalFactId? targetCommandFactId = null)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var accumulator = new Accumulator(session, targetCommandFactId);
        foreach (var item in facts)
        {
            var inspected = accumulator.Inspect(item.Envelope);
            accumulator.Apply(inspected, item.Proof);
        }
        return accumulator.Complete();
    }

    internal sealed class Accumulator
    {
        private readonly SessionAuthorityStampV1 _session;
        private readonly JournalFactId? _targetCommandFactId;
        private readonly AuthorityVectorReplayFoldV1.AuthorityVectorReplayAccumulatorV1 _vector;
        private readonly Dictionary<long, PendingGraphReplacementCommand> _commands = [];
        private long _expectedPosition = 1;
        private GraphReplacementState? _state;
        private GraphReplacementSnapshot? _wire;
        private GraphReplacementJournalFoldResult.InvalidHistory? _invalid;
        private GraphReplacementJournalFoldResult.RuntimeReplaced? _runtimeReplaced;
        private AuthorityFactEnvelopeV1? _installationFact;
        private BufferedCommit? _bufferedCommit;
        private AuthorityFactEnvelopeV1? _targetCommand;
        private AuthorityFactEnvelopeV1? _targetResult;
        private AuthorityFactEnvelopeV1? _targetTransition;
        private bool _targetSeen;

        internal Accumulator(SessionAuthorityStampV1 session, JournalFactId? targetCommandFactId = null)
        {
            if (!session.IsValid) throw new ArgumentException("A valid session is required.", nameof(session));
            if (targetCommandFactId is { IsValid: false }) throw new ArgumentException("A valid target command identity is required.", nameof(targetCommandFactId));
            _session = session; _targetCommandFactId = targetCommandFactId;
            _vector = AuthorityVectorReplayFoldV1.CreateAccumulator(session);
        }

        internal GraphReplacementJournalInspection Inspect(AuthorityFactEnvelopeV1? envelope)
        {
            if (envelope is null)
                return new GraphReplacementJournalInspection.Invalid(null, Code("null-graph-history"));
            if (envelope.PayloadSchema == InstalledRegistration.Schema)
                return InspectInstalled(envelope);
            if (envelope.PayloadSchema == CommandRegistration.Schema)
                return InspectCommand(envelope);
            if (envelope.PayloadSchema == FactRegistration.Schema)
                return InspectFact(envelope);
            if (envelope.PayloadSchema.SchemaId == InstalledRegistration.Schema.SchemaId ||
                envelope.PayloadSchema.SchemaId == CommandRegistration.Schema.SchemaId ||
                envelope.PayloadSchema.SchemaId == FactRegistration.Schema.SchemaId)
                return new GraphReplacementJournalInspection.Invalid(envelope, Code("unknown-graph-replacement-version"));
            return new GraphReplacementJournalInspection.Other(envelope);
        }

        internal void Apply(GraphReplacementJournalInspection? inspected, CapacityGrantSnapshotV1? proof = null)
        {
            if (_invalid is not null) return;
            if (inspected is null || inspected switch { GraphReplacementJournalInspection.Other x => x.Envelope,
                    GraphReplacementJournalInspection.Installed x => x.Envelope,
                    GraphReplacementJournalInspection.Command x => x.Envelope,
                    GraphReplacementJournalInspection.Fact x => x.Envelope,
                    GraphReplacementJournalInspection.Invalid x => x.Envelope, _ => null } is not { } envelope ||
                envelope.Position.Session != _session || envelope.Position.Sequence !=
                    (_bufferedCommit is null ? _expectedPosition : checked(_expectedPosition + 1)))
            { Fail("noncontiguous-history"); return; }

            if (_runtimeReplaced is not null)
            {
                _invalid = Invalid("facts-after-runtime-replacement", _runtimeReplaced.LastPosition);
                return;
            }

            if (_bufferedCommit is not null)
            {
                ApplyCommitTransition(envelope, proof);
                return;
            }

            _vector.Apply(envelope);
            if (_vector.Complete() is AuthorityVectorReplayResultV1.InvalidHistory invalidVector)
            { _invalid = Invalid("invalid-authority-history", invalidVector.LastPosition); return; }
            if (_vector.Complete() is AuthorityVectorReplayResultV1.GenerationReplaced replacedVector)
            {
                _runtimeReplaced = new GraphReplacementJournalFoldResult.RuntimeReplaced(
                    replacedVector.ReplacedBy, replacedVector.LastPosition);
                _expectedPosition++;
                return;
            }

            try
            {
                switch (inspected)
                {
                    case GraphReplacementJournalInspection.Invalid invalid:
                        _invalid = Invalid(invalid.SafeCode.ToString(), _expectedPosition - 1); return;
                    case GraphReplacementJournalInspection.Installed installed:
                        ApplyInstalled(installed, proof); break;
                    case GraphReplacementJournalInspection.Command command:
                        ApplyCommand(command, proof); break;
                    case GraphReplacementJournalInspection.Fact fact:
                        ApplyFact(fact); break;
                    case GraphReplacementJournalInspection.Other:
                        if (IsUnpairedGraphTransition(envelope)) Fail("unpaired-graph-transition");
                        break;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
            { Fail("invalid-graph-replacement-history"); }
            if (_invalid is null && _bufferedCommit is null) _expectedPosition++;
        }

        internal GraphReplacementJournalFoldResult Complete()
        {
            if (_invalid is not null) return _invalid;
            if (_bufferedCommit is not null) return new GraphReplacementJournalFoldResult.AtomicCommitIncomplete(_expectedPosition - 1);
            if (_runtimeReplaced is not null) return _runtimeReplaced;
            var vector = _vector.Complete();
            if (vector is AuthorityVectorReplayResultV1.GenerationReplaced replaced)
                return new GraphReplacementJournalFoldResult.RuntimeReplaced(replaced.ReplacedBy, replaced.LastPosition);
            if (vector is not AuthorityVectorReplayResultV1.Current current)
                return Invalid("invalid-authority-history", _vector.LastVerifiedPosition);
            return new GraphReplacementJournalFoldResult.Current(_expectedPosition - 1, current.Snapshot, _state,
                Array.AsReadOnly(_commands.OrderBy(static x => x.Key).Select(static x => x.Value).ToArray()),
                _targetCommand, _targetResult, _installationFact, _wire, _targetTransition);
        }

        internal long LastVerifiedPosition => _expectedPosition - 1;
        internal GraphReplacementJournalFoldResult.InvalidHistory? Failure => _invalid;
        internal GraphReplacementJournalFoldResult.RuntimeReplaced? RuntimeReplacement => _runtimeReplaced;

        internal CapacityGrantId? CapacityGrantFor(GraphReplacementJournalInspection inspected) => inspected switch
        {
            GraphReplacementJournalInspection.Installed installed => installed.Body.Topology.CapacityGrantId,
            GraphReplacementJournalInspection.Command { Body: GraphReplacementJournalCommand.Prepare prepare } =>
                prepare.TargetTopology.CapacityGrantId,
            GraphReplacementJournalInspection.Command { Body: GraphReplacementJournalCommand.SettleSource } =>
                _state?.SourcePlan.CapacityGrantId,
            _ => null,
        };

        private void ApplyInstalled(GraphReplacementJournalInspection.Installed item, CapacityGrantSnapshotV1? proof)
        {
            if (_state is not null || _commands.Count != 0) { Fail("duplicate-graph-installation"); return; }
            if (!CurrentMatches(item.Outer.ExpectedAuthority) || item.Body.CurrentAuthority != item.Outer.ExpectedAuthority ||
                item.Body.TopologyFingerprint != item.Body.Topology.Fingerprint ||
                item.Envelope.FactId != GraphReplacementFactIds.Installed(_session, item.Body.TopologyFingerprint) ||
                proof?.CurrentFact != item.Body.ActiveSourceGrantFact ||
                item.Body.ActiveSourceGrantFact.Sequence >= item.Envelope.Position.Sequence ||
                !GraphReplacementReducer.GrantMatches(proof, item.Body.Topology, item.Body.CurrentAuthority))
            { Fail("invalid-graph-installation"); return; }
            _state = GraphReplacementState.Create(item.Body.Topology, proof, item.Body.CurrentAuthority, item.Envelope.Position);
            _installationFact = item.Envelope;
            _wire = new(GraphReplacementPhase.None, item.Body.Topology, item.Body.ActiveSourceGrantFact,
                null, item.Body.CurrentAuthority, item.Envelope.Position, null, null, null);
        }

        private void ApplyCommand(GraphReplacementJournalInspection.Command item, CapacityGrantSnapshotV1? proof)
        {
            if (_state is null) { Fail("graph-installation-missing"); return; }
            if (!CurrentMatches(item.Outer.ExpectedAuthority) ||
                item.Envelope.FactId != GraphReplacementFactIds.Command(_session, item.Body.OperationId, (ushort)item.Body.Kind) ||
                item.Body.ExpectedPredecessor.Session != _session ||
                item.Body is GraphReplacementJournalCommand.Prepare prepare && (proof?.CurrentFact != prepare.TargetGrantFact) ||
                item.Body is GraphReplacementJournalCommand.Prepare causalPrepare &&
                    causalPrepare.TargetGrantFact.Sequence >= item.Envelope.Position.Sequence ||
                item.Body is GraphReplacementJournalCommand.SettleSource settle && (proof?.CurrentFact != settle.SourceSettlementFact) ||
                item.Body is GraphReplacementJournalCommand.SettleSource causalSettle &&
                    causalSettle.SourceSettlementFact.Sequence >= item.Envelope.Position.Sequence)
            { Fail("invalid-graph-replacement-command"); return; }
            if (_commands.Values.Any(x => x.Envelope.FactId == item.Envelope.FactId) ||
                _targetCommandFactId == item.Envelope.FactId && _targetSeen)
            { Fail("duplicate-graph-replacement-command"); return; }
            if (_commands.Count == MaximumPendingCommands) { Fail("pending-command-bound"); return; }
            var pending = new PendingGraphReplacementCommand(item.Envelope, item.Outer, item.Body, proof);
            _commands.Add(item.Envelope.Position.Sequence, pending);
            if (_targetCommandFactId == item.Envelope.FactId) { _targetSeen = true; _targetCommand = item.Envelope; }
        }

        private void ApplyFact(GraphReplacementJournalInspection.Fact item)
        {
            if (_state is null) { Fail("graph-installation-missing"); return; }
            var body = item.Body;
            if (item.Envelope.FactId != GraphReplacementFactIds.Result(body.CommandFact) ||
                !_commands.Remove(body.CommandFact.Sequence, out var pending) || pending.Envelope.Position != body.CommandFact ||
                pending.Outer.ExpectedAuthority != item.Outer.ExpectedAuthority ||
                body.ExpectedPredecessor != pending.Command.ExpectedPredecessor || body.ActualPredecessor != _state.LastFact)
            { Fail("invalid-graph-replacement-fact"); return; }

            var command = ToReducerCommand(pending);
            GraphReplacementReductionResult reduction = CurrentMatches(pending.Outer.ExpectedAuthority)
                ? GraphReplacementReducer.Apply(_state, command, item.Envelope.Position)
                : new GraphReplacementReductionResult.Rejected(_state, Code("authority-vector-stale"));
            var successfulCommit = pending.Command is GraphReplacementJournalCommand.Commit &&
                reduction is GraphReplacementReductionResult.Applied;
            if (successfulCommit &&
                item.Envelope.Position.Sequence == long.MaxValue)
            { Fail("invalid-atomic-commit-position"); return; }
            var expected = Render(reduction, pending.Command.Kind, pending.Envelope.Position, item.Envelope.Position);
            if (expected is null) { Fail("graph-replacement-reduction-mismatch"); return; }
            var expectedValue = expected.Value;
            var expectedWire = RenderWire(_wire!, expectedValue.State, pending,
                item.Envelope.Position, reduction is GraphReplacementReductionResult.Applied);
            var expectedBody = new GraphReplacementFact(body.CommandFact,
                body.ExpectedPredecessor, body.ActualPredecessor, expectedValue.Outcome, expectedWire, expectedValue.SafeCode);
            if (!GraphReplacementCodecs.EncodeFact(expectedBody).AsSpan().SequenceEqual(item.Outer.Body.Span))
            { Fail("graph-replacement-reduction-mismatch"); return; }

            if (successfulCommit)
                _bufferedCommit = new BufferedCommit(item.Envelope, pending, expectedValue.State, expectedWire);
            else
            {
                _state = expectedValue.State; _wire = expectedWire;
                if (_targetCommandFactId == pending.Envelope.FactId) _targetResult = item.Envelope;
            }
        }

        private void ApplyCommitTransition(AuthorityFactEnvelopeV1 envelope, CapacityGrantSnapshotV1? proof)
        {
            var buffered = _bufferedCommit!;
            var decode = AuthorityGenerationTransitionCodecV1.Decode(envelope.PayloadSchema, envelope.Owner,
                _session, envelope.PayloadMemory, out var transition);
            if (proof is not null || envelope.Position.Sequence != _expectedPosition + 1 || decode != AuthorityGenerationTransitionDecodeV1.Valid ||
                transition.Axis != AuthorityAxisId.Graph || envelope.ThreadScope is not null ||
                envelope.FactId != GraphReplacementFactIds.Transition(buffered.Pending.Envelope.Position) ||
                envelope.PayloadHash != AuthorityPayloadHashV1.Compute(AuthorityGenerationTransitionCodecV1.SchemaTokenFor(AuthorityAxisId.Graph),
                    GraphTransitionSchema, envelope.PayloadBytes) ||
                GraphGenerationId.FromValue(transition.ExpectedPrevious) != buffered.State.SourcePlan.GraphGeneration ||
                GraphGenerationId.FromValue(transition.ProposedNext) != buffered.State.TargetPlan!.GraphGeneration)
            { Fail("invalid-atomic-commit-pair"); return; }
            _vector.Apply(envelope);
            if (_vector.Complete() is not AuthorityVectorReplayResultV1.Current current ||
                !SessionLifecycleJournalFoldV1.Matches(buffered.State.Authority, current.Snapshot))
            { Fail("invalid-atomic-commit-pair"); return; }
            _state = buffered.State; _wire = buffered.Wire; _bufferedCommit = null;
            if (_targetCommandFactId == buffered.Pending.Envelope.FactId) _targetResult = buffered.ResultEnvelope;
            if (_targetCommandFactId == buffered.Pending.Envelope.FactId) _targetTransition = envelope;
            _expectedPosition += 2;
        }

        private GraphReplacementJournalInspection InspectInstalled(AuthorityFactEnvelopeV1 envelope) =>
            ValidProtocolEnvelope(envelope, InstalledRegistration) &&
            GraphReplacementCodecs.TryDecodeOuter(envelope.PayloadMemory, out var outer) &&
            GraphReplacementCodecs.TryDecodeInstalled(outer!.Body, out var body)
                ? new GraphReplacementJournalInspection.Installed(envelope, outer, body!)
                : new GraphReplacementJournalInspection.Invalid(envelope, Code("invalid-graph-installation"));

        private GraphReplacementJournalInspection InspectCommand(AuthorityFactEnvelopeV1 envelope) =>
            ValidProtocolEnvelope(envelope, CommandRegistration) &&
            GraphReplacementCodecs.TryDecodeOuter(envelope.PayloadMemory, out var outer) &&
            GraphReplacementCodecs.TryDecodeCommand(outer!.Body, out var body)
                ? new GraphReplacementJournalInspection.Command(envelope, outer, body!)
                : new GraphReplacementJournalInspection.Invalid(envelope, Code("invalid-graph-replacement-command"));

        private GraphReplacementJournalInspection InspectFact(AuthorityFactEnvelopeV1 envelope) =>
            ValidProtocolEnvelope(envelope, FactRegistration) &&
            GraphReplacementCodecs.TryDecodeOuter(envelope.PayloadMemory, out var outer) &&
            GraphReplacementCodecs.TryDecodeFact(outer!.Body, out var body)
                ? new GraphReplacementJournalInspection.Fact(envelope, outer, body!)
                : new GraphReplacementJournalInspection.Invalid(envelope, Code("invalid-graph-replacement-fact"));

        private bool ValidProtocolEnvelope(AuthorityFactEnvelopeV1 envelope, AuthorityPayloadRegistrationV1 registration) =>
            envelope.Position.Session == _session && envelope.Owner == OwnerSliceId.S2 && envelope.ThreadScope is null &&
            envelope.PayloadHash == AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, envelope.PayloadBytes) &&
            registration.Validate(envelope.PayloadMemory, _session);

        private bool CurrentMatches(ExpectedAuthorityVectorV1 expected) =>
            _vector.Complete() is AuthorityVectorReplayResultV1.Current current &&
            SessionLifecycleJournalFoldV1.Matches(expected, current.Snapshot);

        private bool IsUnpairedGraphTransition(AuthorityFactEnvelopeV1 envelope) =>
            envelope.PayloadSchema == GraphTransitionSchema ||
            envelope.PayloadSchema.SchemaId == GraphTransitionSchema.SchemaId;

        private void Fail(string code) => _invalid = Invalid(code, _expectedPosition - 1);
    }

    private static GraphReplacementCommand ToReducerCommand(PendingGraphReplacementCommand pending) => pending.Command switch
    {
        GraphReplacementJournalCommand.Prepare x => new GraphReplacementCommand.Prepare(x.Operation, x.Predecessor,
            x.SourceFingerprint, x.TargetTopology, pending.ReferencedGrant!, x.CurrentAuthority, x.ObservedAt, x.OverlapDeadline),
        GraphReplacementJournalCommand.Commit x => new GraphReplacementCommand.Commit(x.Operation, x.Predecessor),
        GraphReplacementJournalCommand.SettleSource x => new GraphReplacementCommand.SettleSource(x.Operation, x.Predecessor, pending.ReferencedGrant!),
        _ => throw new InvalidOperationException(),
    };

    internal sealed record RenderedFact(GraphReplacementFact Body, bool RequiresGraphTransition,
        GraphGenerationId? PreviousGraph, GraphGenerationId? NextGraph);

    internal static RenderedFact? RenderFact(GraphReplacementJournalFoldResult.Current current,
        PendingGraphReplacementCommand pending, JournalPositionV1 resultPosition)
    {
        if (current.State is null || current.Wire is null || !resultPosition.IsValid ||
            resultPosition.Session != pending.Envelope.Position.Session || resultPosition.Sequence <= current.SnapshotThrough)
            return null;
        var reduction = SessionLifecycleJournalFoldV1.Matches(pending.Outer.ExpectedAuthority, current.Authority)
            ? GraphReplacementReducer.Apply(current.State, ToReducerCommand(pending), resultPosition)
            : new GraphReplacementReductionResult.Rejected(current.State, Code("authority-vector-stale"));
        var rendered = Render(reduction, pending.Command.Kind, pending.Envelope.Position, resultPosition);
        if (rendered is null) return null;
        var value = rendered.Value;
        var successfulCommit = pending.Command is GraphReplacementJournalCommand.Commit &&
            reduction is GraphReplacementReductionResult.Applied;
        if (successfulCommit && resultPosition.Sequence == long.MaxValue) return null;
        var wire = RenderWire(current.Wire, value.State, pending, resultPosition,
            reduction is GraphReplacementReductionResult.Applied);
        return new(new GraphReplacementFact(pending.Envelope.Position,
            pending.Command.ExpectedPredecessor, current.State.LastFact, value.Outcome, wire, value.SafeCode),
            successfulCommit, successfulCommit ? current.State.SourcePlan.GraphGeneration : null,
            successfulCommit ? current.State.TargetPlan!.GraphGeneration : null);
    }

    private static (GraphReplacementJournalOutcome Outcome, GraphReplacementState State, BoundedAscii? SafeCode)? Render(
        GraphReplacementReductionResult result, GraphReplacementJournalCommandKind kind,
        JournalPositionV1 commandFact, JournalPositionV1 resultFact) => result switch
    {
        GraphReplacementReductionResult.Applied x => (Success(kind), x.State, null),
        GraphReplacementReductionResult.Idempotent x => (Success(kind), x.State, null),
        GraphReplacementReductionResult.Rejected x => (GraphReplacementJournalOutcome.Rejected, x.State, x.SafeCode),
        GraphReplacementReductionResult.Conflict x => (GraphReplacementJournalOutcome.Conflict, x.State, Code("replacement-predecessor-conflict")),
        GraphReplacementReductionResult.GenerationReplaced x => (GraphReplacementJournalOutcome.GenerationReplaced, x.State, Code("generation-replaced")),
        _ => null,
    };

    private static GraphReplacementJournalOutcome Success(GraphReplacementJournalCommandKind kind) => kind switch
    {
        GraphReplacementJournalCommandKind.Prepare => GraphReplacementJournalOutcome.Prepared,
        GraphReplacementJournalCommandKind.Commit => GraphReplacementJournalOutcome.Committed,
        GraphReplacementJournalCommandKind.SettleSource => GraphReplacementJournalOutcome.SourceSettled,
        _ => throw new InvalidOperationException(),
    };

    private static GraphReplacementSnapshot RenderWire(GraphReplacementSnapshot prior,
        GraphReplacementState state, PendingGraphReplacementCommand pending,
        JournalPositionV1 resultFact, bool applied)
    {
        var target = prior.Target;
        var replacement = prior.Replacement;
        var commit = prior.Commit;
        var settlement = prior.Settlement;
        if (applied && pending.Command is GraphReplacementJournalCommand.Prepare prepare)
        {
            target = new(prepare.TargetTopology, prepare.TargetGrantFact);
            replacement = new(prepare.Operation, pending.Envelope.Position);
        }
        else if (applied && pending.Command is GraphReplacementJournalCommand.Commit)
            commit = new(pending.Envelope.Position, new JournalPositionV1(resultFact.Session, resultFact.Sequence + 1));
        else if (applied && pending.Command is GraphReplacementJournalCommand.SettleSource settle)
            settlement = new(pending.Envelope.Position, settle.SourceSettlementFact);
        return new(state.Phase, state.SourcePlan, state.SourceGrant.CurrentFact, target, state.Authority,
            state.LastFact, replacement, commit, settlement);
    }

    private static BoundedAscii Code(string value) => new(value);
    private static GraphReplacementJournalFoldResult.InvalidHistory Invalid(string code, long position) => new(Code(code), position);
    private sealed record BufferedCommit(AuthorityFactEnvelopeV1 ResultEnvelope,
        PendingGraphReplacementCommand Pending, GraphReplacementState State, GraphReplacementSnapshot Wire);
}
