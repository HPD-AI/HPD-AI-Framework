using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal sealed record GraphRuntimeJournalOperation(
    OperationId OperationId,
    GraphRuntimeCommandKind Kind,
    Hash256 RequestHash,
    AuthorityFactEnvelopeV1 CommandEnvelope,
    GraphRuntimeOwnerPayload CommandOuter,
    GraphRuntimeCommand Command,
    AuthorityFactEnvelopeV1? ResultEnvelope);

internal sealed record PendingGraphRuntimeCommand(
    GraphRuntimeJournalOperation Operation,
    GraphRuntimeEvaluation Evaluation,
    CapacityGrantSnapshotV1? ActivationGrant);

internal sealed record GraphRuntimeJournalProof(
    CapacityGrantSnapshotV1? GraphProof,
    CapacityGrantSnapshotV1? RuntimeActivationProof);

internal sealed record GraphRuntimeRenderedFact(GraphRuntimeFact Body);

internal abstract record GraphRuntimeJournalFoldResult
{
    private GraphRuntimeJournalFoldResult() { }

    internal sealed record Current(long SnapshotThrough, CurrentAuthorityVectorSnapshotV1 Authority,
        GraphRuntimeSnapshot? Snapshot, PendingGraphRuntimeCommand? Pending,
        IReadOnlyList<GraphRuntimeJournalOperation> Operations,
        GraphReplacementJournalFoldResult.Current? Graph = null) : GraphRuntimeJournalFoldResult;

    internal sealed record RuntimeReplaced(RuntimeGenerationId Next, long TerminatedAt, long SnapshotThrough,
        GraphRuntimeSnapshot? Snapshot, PendingGraphRuntimeCommand? Pending,
        AuthorityFactEnvelopeV1? TerminalResultFact,
        AuthorityFactEnvelopeV1? TerminalCommandFact = null) : GraphRuntimeJournalFoldResult;

    internal sealed record AuthorityGenerationReplaced(AuthorityAxisId Axis, StableId128 Next, long TerminatedAt,
        long SnapshotThrough, GraphRuntimeSnapshot? Snapshot, PendingGraphRuntimeCommand? Pending,
        AuthorityFactEnvelopeV1? TerminalResultFact,
        AuthorityFactEnvelopeV1? TerminalCommandFact = null) : GraphRuntimeJournalFoldResult;

    internal sealed record InvalidHistory(BoundedAscii Code, long LastVerified) : GraphRuntimeJournalFoldResult;
}

internal abstract record GraphRuntimeJournalInspection
{
    private GraphRuntimeJournalInspection() { }
    internal sealed record Other(AuthorityFactEnvelopeV1 Envelope) : GraphRuntimeJournalInspection;
    internal sealed record Command(AuthorityFactEnvelopeV1 Envelope, GraphRuntimeOwnerPayload Outer,
        GraphRuntimeCommand Body) : GraphRuntimeJournalInspection;
    internal sealed record Fact(AuthorityFactEnvelopeV1 Envelope, GraphRuntimeOwnerPayload Outer,
        GraphRuntimeFact Body) : GraphRuntimeJournalInspection;
    internal sealed record Invalid(AuthorityFactEnvelopeV1? Envelope, BoundedAscii Code) : GraphRuntimeJournalInspection;
}

internal static class GraphRuntimeJournalFold
{
    internal const int MaximumFacts = 65_536;
    internal const int MaximumOperations = 256;
    internal const int MaximumPendingCommands = 1;

    private static readonly AuthorityPayloadRegistrationV1 CommandRegistration = GraphRuntimePayloadRegistrations.Command;
    private static readonly AuthorityPayloadRegistrationV1 FactRegistration = GraphRuntimePayloadRegistrations.Fact;

    internal static Accumulator CreateAccumulator(SessionAuthorityStampV1 session) => new(session);

    internal static GraphRuntimeRenderedFact? RenderFact(GraphRuntimeJournalFoldResult fold,
        PendingGraphRuntimeCommand pending, JournalPositionV1 proposedResult,
        GraphRuntimeEffectResolution? effect = null, bool linearizableNotObserved = false)
    {
        ArgumentNullException.ThrowIfNull(fold); ArgumentNullException.ThrowIfNull(pending);
        if (!proposedResult.IsValid || effect is not null && linearizableNotObserved) return null;
        var context = fold switch
        {
            GraphRuntimeJournalFoldResult.Current current =>
                (current.SnapshotThrough, current.Snapshot, current.Pending, false, false),
            GraphRuntimeJournalFoldResult.AuthorityGenerationReplaced terminal when terminal.TerminalResultFact is null =>
                (terminal.SnapshotThrough, terminal.Snapshot, terminal.Pending, true, false),
            GraphRuntimeJournalFoldResult.RuntimeReplaced terminal =>
                (terminal.SnapshotThrough, terminal.Snapshot, terminal.Pending, true, true),
            _ => (-1L, null, null, false, false),
        };
        if (context.Item1 < 0 || context.Item5 || context.Item3 is null ||
            !ReferenceEquals(context.Item3, pending) || pending.Operation.ResultEnvelope is not null ||
            proposedResult.Session != pending.Operation.CommandEnvelope.Position.Session ||
            proposedResult.Sequence != context.Item1 + 1)
            return null;

        var actual = ActualForRender(pending);
        GraphRuntimeOutcome outcome; GraphRuntimeSnapshot? snapshot; Hash256? receipt = null; BoundedAscii? code = null;
        if (pending.Evaluation is GraphRuntimeReducer.EffectRequired capability)
        {
            if (linearizableNotObserved)
            {
                if (!context.Item4) return null;
                outcome = GraphRuntimeOutcome.GenerationReplaced; snapshot = context.Item2; code = Code("generation-replaced");
            }
            else if (effect is not null)
            {
                var resolved = GraphRuntimeReducer.Resolve(capability, effect, proposedResult);
                if (resolved is GraphRuntimeResolution.Applied applied)
                { outcome=applied.Outcome;snapshot=applied.Snapshot;receipt=applied.ReceiptHash; }
                else if (resolved is GraphRuntimeResolution.Rejected rejected)
                { outcome=GraphRuntimeOutcome.Rejected;snapshot=rejected.Snapshot;code=rejected.SafeCode; }
                else return null;
            }
            else return null;
        }
        else
        {
            if (effect is not null || linearizableNotObserved) return null;
            switch (pending.Evaluation)
            {
                case GraphRuntimeEvaluation.Rejected rejected:
                    outcome=GraphRuntimeOutcome.Rejected;snapshot=rejected.Snapshot;code=rejected.SafeCode;break;
                case GraphRuntimeEvaluation.Conflict conflict:
                    outcome=GraphRuntimeOutcome.Conflict;snapshot=conflict.Snapshot;actual=conflict.ActualPredecessor;
                    code=Code("runtime-predecessor-conflict");break;
                case GraphRuntimeEvaluation.GenerationReplaced replaced:
                    outcome=GraphRuntimeOutcome.GenerationReplaced;snapshot=replaced.Snapshot;code=Code("generation-replaced");break;
                default:return null;
            }
        }
        try
        {
            return new(new GraphRuntimeFact(pending.Operation.CommandEnvelope.Position,
                pending.Operation.Command.ExpectedPredecessor, actual, outcome, snapshot, receipt, code));
        }
        catch (ArgumentException) { return null; }
    }

    internal static GraphRuntimeJournalFoldResult Fold(SessionAuthorityStampV1 session,
        IEnumerable<(AuthorityFactEnvelopeV1 Envelope, GraphRuntimeJournalProof? Proof)> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var accumulator = new Accumulator(session);
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
        private readonly GraphReplacementJournalFold.Accumulator _graph;
        private readonly Dictionary<OperationId, GraphRuntimeJournalOperation> _operations = [];
        private long _expectedPosition = 1;
        private int _factCount;
        private GraphRuntimeSnapshot? _snapshot;
        private PendingGraphRuntimeCommand? _pending;
        private Terminal? _terminal;
        private GraphRuntimeJournalFoldResult.InvalidHistory? _invalid;

        internal Accumulator(SessionAuthorityStampV1 session)
        {
            if (!session.IsValid) throw new ArgumentException("A valid session is required.", nameof(session));
            _session = session;
            _graph = GraphReplacementJournalFold.CreateAccumulator(session);
        }

        internal GraphRuntimeJournalInspection Inspect(AuthorityFactEnvelopeV1? envelope)
        {
            if (envelope is null)
                return new GraphRuntimeJournalInspection.Invalid(null, Code("null-runtime-history"));
            if (envelope.PayloadSchema == CommandRegistration.Schema)
                return InspectCommand(envelope);
            if (envelope.PayloadSchema == FactRegistration.Schema)
                return InspectFact(envelope);
            if (envelope.PayloadSchema.SchemaId == CommandRegistration.Schema.SchemaId ||
                envelope.PayloadSchema.SchemaId == FactRegistration.Schema.SchemaId)
                return new GraphRuntimeJournalInspection.Invalid(envelope, Code("unknown-graph-runtime-version"));
            return new GraphRuntimeJournalInspection.Other(envelope);
        }

        internal void Apply(GraphRuntimeJournalInspection? inspected, GraphRuntimeJournalProof? proof = null)
        {
            if (_invalid is not null) return;
            var envelope = EnvelopeOf(inspected);
            if (envelope is null || envelope.Position.Session != _session || envelope.Position.Sequence != _expectedPosition)
            { Fail("noncontiguous-history"); return; }
            if (_factCount == MaximumFacts) { Fail("runtime-fact-bound"); return; }

            if (_terminal is { Runtime: true })
            { Fail("facts-after-runtime-replacement"); return; }

            if (_terminal is not null && inspected is GraphRuntimeJournalInspection.Command)
            { Fail("runtime-command-after-generation-replacement"); return; }

            // A claimed-axis replacement terminates ordinary traversal, but Decision 0023
            // permits the one exact result that resolves the retained runtime command.
            // Do not feed that result into the replacement accumulator: its own protocol
            // correctly rejects every fact after replacement, whereas this composite owns
            // the narrower cross-protocol receipt exception.
            var transitionDecode = AuthorityGenerationTransitionCodecV1.Decode(envelope.PayloadSchema, envelope.Owner,
                _session, envelope.PayloadMemory, out var transition);

            if (_terminal is null)
            {
                var graphInspection = _graph.Inspect(envelope);
                var graphProofRequired = graphInspection.CapacityReference is not null;
                if (graphProofRequired != (proof?.GraphProof is not null))
                { Fail(graphProofRequired ? "graph-proof-missing" : "unexpected-graph-proof"); return; }
                _graph.Apply(graphInspection, proof?.GraphProof);
                if (_graph.Failure is { } graphFailure)
                { _invalid = new(Code("invalid-graph-history"), graphFailure.LastVerifiedPosition); return; }
            }
            else if (proof?.GraphProof is not null)
            { Fail("unexpected-graph-proof"); return; }

            try
            {
                switch (inspected)
                {
                    case GraphRuntimeJournalInspection.Invalid invalid:
                        Fail(invalid.Code.ToString()); return;
                    case GraphRuntimeJournalInspection.Command command:
                        if (proof?.GraphProof is not null ||
                            (proof?.RuntimeActivationProof is not null && command.Body is not GraphRuntimeCommand.Activate))
                        { Fail("unexpected-runtime-proof"); return; }
                        ApplyCommand(command, proof?.RuntimeActivationProof); break;
                    case GraphRuntimeJournalInspection.Fact fact:
                        if (proof?.RuntimeActivationProof is not null) { Fail("unexpected-runtime-proof"); return; }
                        ApplyFact(fact); break;
                    case GraphRuntimeJournalInspection.Other:
                        if (proof?.RuntimeActivationProof is not null) { Fail("unexpected-runtime-proof"); return; }
                        break;
                }
                if (_invalid is not null) return;
                if (transitionDecode == AuthorityGenerationTransitionDecodeV1.Valid)
                    ApplyTransition(transition.Axis, transition.ProposedNext, envelope.Position.Sequence);
                else if (transitionDecode == AuthorityGenerationTransitionDecodeV1.Invalid)
                { Fail("invalid-authority-transition"); return; }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
            { Fail("invalid-graph-runtime-history"); return; }

            if (_invalid is null) { _expectedPosition++; _factCount++; }
        }

        internal GraphRuntimeJournalFoldResult Complete()
        {
            if (_invalid is not null) return _invalid;
            var graph = _graph.Complete();
            if (graph is GraphReplacementJournalFoldResult.InvalidHistory invalid)
                return new GraphRuntimeJournalFoldResult.InvalidHistory(Code("invalid-graph-history"), invalid.LastVerifiedPosition);
            if (_terminal is { } terminal)
                return terminal.Runtime
                    ? new GraphRuntimeJournalFoldResult.RuntimeReplaced(RuntimeGenerationId.FromValue(terminal.Next), terminal.At,
                        _expectedPosition - 1, _snapshot, _pending, terminal.Result, TerminalCommand(terminal))
                    : new GraphRuntimeJournalFoldResult.AuthorityGenerationReplaced(terminal.Axis, terminal.Next, terminal.At,
                        _expectedPosition - 1, _snapshot, _pending, terminal.Result, TerminalCommand(terminal));
            if (graph is GraphReplacementJournalFoldResult.RuntimeReplaced replaced)
                return new GraphRuntimeJournalFoldResult.RuntimeReplaced(replaced.Replacement, replaced.LastPosition,
                    _expectedPosition - 1, _snapshot, _pending, null);
            if (graph is not GraphReplacementJournalFoldResult.Current current)
                return Invalid("invalid-graph-history", _expectedPosition - 1);
            return new GraphRuntimeJournalFoldResult.Current(_expectedPosition - 1, current.Authority, _snapshot, _pending,
                Array.AsReadOnly(_operations.Values.OrderBy(static x => x.CommandEnvelope.Position.Sequence).ToArray()), current);
        }

        internal long LastVerifiedPosition => _expectedPosition - 1;

        /// <summary>
        /// Gets a proven failure without requiring an in-progress atomic pair to be complete.
        /// </summary>
        internal GraphRuntimeJournalFoldResult.InvalidHistory? Failure => _invalid ??
            (_graph.Failure is { } graphFailure
                ? new GraphRuntimeJournalFoldResult.InvalidHistory(
                    Code("invalid-graph-history"), graphFailure.LastVerifiedPosition)
                : null);

        internal (CapacityGrantId GrantId, JournalPositionV1 Through)? GraphCapacityReference(
            GraphRuntimeJournalInspection inspected)
        {
            var graph = _graph.Inspect(EnvelopeOf(inspected));
            return graph.CapacityReference is { } through && _graph.CapacityGrantFor(graph) is { } grant
                ? (grant, through) : null;
        }

        internal CapacityGrantId? InstalledCapacityGrantId => _graph.Complete() is
            GraphReplacementJournalFoldResult.Current { State: { } state }
                ? state.SourcePlan.CapacityGrantId : null;

        private void ApplyCommand(GraphRuntimeJournalInspection.Command item, CapacityGrantSnapshotV1? proof)
        {
            if (_terminal is not null) { Fail("runtime-command-after-generation-replacement"); return; }
            if (_pending is not null) { Fail("second-pending-runtime-command"); return; }
            if (_operations.TryGetValue(item.Body.OperationId, out var prior))
            {
                Fail(prior.Kind == item.Body.Kind && prior.RequestHash == item.Body.EffectRequestHash
                    ? "duplicate-runtime-command" : "runtime-operation-identity-reuse");
                return;
            }
            if (_operations.Count == MaximumOperations) { Fail("runtime-operation-bound"); return; }
            if (item.Body is GraphRuntimeCommand.Activate && proof is null)
            { Fail("runtime-activation-proof-missing"); return; }

            if (_graph.Complete() is not GraphReplacementJournalFoldResult.Current graphCurrent ||
                graphCurrent.SnapshotThrough != item.Envelope.Position.Sequence)
            { Fail("runtime-graph-evidence-invalid"); return; }
            var verified = new GraphReplacementSnapshotReadResult.Verified(graphCurrent, graphCurrent.SnapshotThrough);
            var evidence = GraphRuntimeCurrentGraphEvidence.From(verified);
            var evaluation = GraphRuntimeReducer.Evaluate(_snapshot, item.Body, item.Envelope.Position,
                item.Outer.ExpectedAuthority, evidence, proof);
            var operation = new GraphRuntimeJournalOperation(item.Body.OperationId, item.Body.Kind,
                item.Body.EffectRequestHash, item.Envelope, item.Outer, item.Body, null);
            _operations.Add(item.Body.OperationId, operation);
            _pending = new(operation, evaluation, proof);
        }

        private void ApplyFact(GraphRuntimeJournalInspection.Fact item)
        {
            if (_pending is null || item.Body.CommandFact != _pending.Operation.CommandEnvelope.Position ||
                item.Envelope.FactId != GraphRuntimeFactIds.Result(_pending.Operation.CommandEnvelope.Position))
            { Fail(_terminal is null ? "unmatched-runtime-result" : "runtime-fact-after-generation-replacement"); return; }
            if (_terminal is { Runtime: true }) { Fail("facts-after-runtime-replacement"); return; }
            if (_terminal is not null && _terminal.Result is not null)
            { Fail("duplicate-terminal-runtime-result"); return; }
            if (!SameAuthority(_pending.Operation.CommandOuter.ExpectedAuthority, item.Outer.ExpectedAuthority) ||
                item.Body.ExpectedPredecessor != _pending.Operation.Command.ExpectedPredecessor ||
                item.Body.ActualPredecessor != ActualPredecessor(_pending))
            { Fail("invalid-runtime-result-join"); return; }

            if (_terminal is not null)
            {
                if (!MatchesTerminalResult(item.Body, item.Envelope.Position, _pending, _terminal))
                { Fail("invalid-terminal-runtime-result"); return; }
                CompleteOperation(item.Envelope, keepPending: false);
                _terminal = _terminal with { Result = item.Envelope };
                return;
            }

            if (!ApplyNormalResult(item.Body, item.Envelope.Position, _pending))
            { Fail("runtime-reduction-mismatch"); return; }
            CompleteOperation(item.Envelope, keepPending: false);
        }

        private bool ApplyNormalResult(GraphRuntimeFact fact, JournalPositionV1 resultPosition,
            PendingGraphRuntimeCommand pending)
        {
            switch (pending.Evaluation)
            {
                case GraphRuntimeReducer.EffectRequired required:
                    GraphRuntimeResolution resolution = fact.Outcome is GraphRuntimeOutcome.Activated or GraphRuntimeOutcome.Retired
                        ? fact.EffectReceiptHash is { } receipt
                            ? GraphRuntimeReducer.Resolve(required, new GraphRuntimeEffectResolution.Completed(receipt), resultPosition)
                            : new GraphRuntimeResolution.Rejected(required.Prior, Code("missing-runtime-receipt"))
                        : GraphRuntimeReducer.Resolve(required, new GraphRuntimeEffectResolution.Refused(fact.SafeCode!.Value),
                            resultPosition);
                    return MatchesResolution(fact, resolution);
                case GraphRuntimeEvaluation.Rejected rejected:
                    return fact.Outcome == GraphRuntimeOutcome.Rejected && fact.ResultingSnapshot == rejected.Snapshot &&
                        fact.EffectReceiptHash is null && fact.SafeCode == rejected.SafeCode;
                case GraphRuntimeEvaluation.Conflict conflict:
                    return fact.Outcome == GraphRuntimeOutcome.Conflict && fact.ResultingSnapshot == conflict.Snapshot &&
                        fact.EffectReceiptHash is null && fact.SafeCode?.ToString() == "runtime-predecessor-conflict";
                case GraphRuntimeEvaluation.GenerationReplaced replaced:
                    return fact.Outcome == GraphRuntimeOutcome.GenerationReplaced && fact.ResultingSnapshot == replaced.Snapshot &&
                        fact.EffectReceiptHash is null && fact.SafeCode?.ToString() == "generation-replaced";
                default: return false;
            }
        }

        private bool MatchesResolution(GraphRuntimeFact fact, GraphRuntimeResolution resolution) => resolution switch
        {
            GraphRuntimeResolution.Applied applied => fact.Outcome == applied.Outcome &&
                fact.ResultingSnapshot == applied.Snapshot && fact.EffectReceiptHash == applied.ReceiptHash && fact.SafeCode is null,
            GraphRuntimeResolution.Rejected rejected => fact.Outcome == GraphRuntimeOutcome.Rejected &&
                fact.ResultingSnapshot == rejected.Snapshot && fact.EffectReceiptHash is null && fact.SafeCode == rejected.SafeCode,
            _ => false,
        };

        private bool MatchesTerminalResult(GraphRuntimeFact fact, JournalPositionV1 resultPosition,
            PendingGraphRuntimeCommand pending, Terminal terminal)
        {
            if (terminal.Runtime) return false;
            if (fact.Outcome == GraphRuntimeOutcome.GenerationReplaced)
                return fact.ResultingSnapshot == _snapshot && fact.EffectReceiptHash is null &&
                    fact.SafeCode?.ToString() == "generation-replaced";
            return ApplyNormalResult(fact, resultPosition, pending);
        }

        private void CompleteOperation(AuthorityFactEnvelopeV1 result, bool keepPending)
        {
            var operation = _pending!.Operation with { ResultEnvelope = result };
            _operations[operation.OperationId] = operation;
            if (result.PayloadSchema == FactRegistration.Schema && GraphRuntimeCodecs.TryDecodeOuter(result.PayloadMemory, out var outer) &&
                GraphRuntimeCodecs.TryDecodeFact(outer!.Body, out var body) && body!.ResultingSnapshot is { } resulting)
                _snapshot = resulting;
            _pending = keepPending ? _pending with { Operation = operation } : null;
        }

        private void ApplyTransition(AuthorityAxisId axis, StableId128 next, long position)
        {
            if (_terminal is not null) return;
            var runtime = axis == AuthorityAxisId.Runtime;
            var claimed = axis == AuthorityAxisId.Graph ||
                _snapshot?.CurrentAuthority.Axes.Any(entry => entry.AxisId == axis) == true ||
                _pending?.Operation.CommandOuter.ExpectedAuthority.Axes.Any(entry => entry.AxisId == axis) == true;
            if (!runtime && !claimed) return;
            _terminal = new(axis, next, position, runtime, null);
        }

        private static JournalPositionV1 ActualPredecessor(PendingGraphRuntimeCommand pending) =>
            pending.Evaluation switch
            {
                GraphRuntimeReducer.EffectRequired required => required.ActualPredecessor,
                GraphRuntimeEvaluation.Conflict conflict => conflict.ActualPredecessor,
                _ when pending.Operation.Command is GraphRuntimeCommand.Activate activate => activate.GraphAuthorityFact,
                _ when pending.Operation.Command is GraphRuntimeCommand.Retire && pending.Evaluation is GraphRuntimeEvaluation.Rejected rejected && rejected.Snapshot is { } prior => prior.LastRuntimeFact,
                _ => pending.Operation.Command.ExpectedPredecessor,
            };

        private GraphRuntimeJournalInspection InspectCommand(AuthorityFactEnvelopeV1 envelope) =>
            GraphRuntimePayloadRegistrations.ValidateCommandEnvelope(envelope) &&
            GraphRuntimeCodecs.TryDecodeOuter(envelope.PayloadMemory, out var outer) &&
            GraphRuntimeCodecs.TryDecodeCommand(outer!.Body, out var body)
                ? new GraphRuntimeJournalInspection.Command(envelope, outer, body!)
                : new GraphRuntimeJournalInspection.Invalid(envelope, Code("invalid-graph-runtime-command"));

        private GraphRuntimeJournalInspection InspectFact(AuthorityFactEnvelopeV1 envelope) =>
            GraphRuntimePayloadRegistrations.ValidateFactEnvelope(envelope) &&
            GraphRuntimeCodecs.TryDecodeOuter(envelope.PayloadMemory, out var outer) &&
            GraphRuntimeCodecs.TryDecodeFact(outer!.Body, out var body)
                ? new GraphRuntimeJournalInspection.Fact(envelope, outer, body!)
                : new GraphRuntimeJournalInspection.Invalid(envelope, Code("invalid-graph-runtime-fact"));

        private static AuthorityFactEnvelopeV1? EnvelopeOf(GraphRuntimeJournalInspection? inspected) => inspected switch
        {
            GraphRuntimeJournalInspection.Other x => x.Envelope,
            GraphRuntimeJournalInspection.Command x => x.Envelope,
            GraphRuntimeJournalInspection.Fact x => x.Envelope,
            GraphRuntimeJournalInspection.Invalid x => x.Envelope,
            _ => null,
        };

        private static bool SameAuthority(ExpectedAuthorityVectorV1 left, ExpectedAuthorityVectorV1 right) => left == right;
        private AuthorityFactEnvelopeV1? TerminalCommand(Terminal terminal) => terminal.Result is null
            ? null
            : _operations.Values.SingleOrDefault(x => x.ResultEnvelope == terminal.Result)?.CommandEnvelope;
        private void Fail(string code) => _invalid = Invalid(code, _expectedPosition - 1);
    }

    private sealed record Terminal(AuthorityAxisId Axis, StableId128 Next, long At, bool Runtime,
        AuthorityFactEnvelopeV1? Result);
    private static JournalPositionV1 ActualForRender(PendingGraphRuntimeCommand pending) => pending.Evaluation switch
    {
        GraphRuntimeReducer.EffectRequired required => required.ActualPredecessor,
        GraphRuntimeEvaluation.Conflict conflict => conflict.ActualPredecessor,
        _ when pending.Operation.Command is GraphRuntimeCommand.Activate activate => activate.GraphAuthorityFact,
        _ when pending.Operation.Command is GraphRuntimeCommand.Retire && pending.Evaluation is GraphRuntimeEvaluation.Rejected rejected && rejected.Snapshot is { } prior => prior.LastRuntimeFact,
        _ => pending.Operation.Command.ExpectedPredecessor,
    };
    private static BoundedAscii Code(string value) => new(value);
    private static GraphRuntimeJournalFoldResult.InvalidHistory Invalid(string code, long lastVerified) =>
        new(Code(code), lastVerified);
}
