using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal sealed record GraphRuntimeJournalOperationV1(
    OperationId OperationId,
    GraphRuntimeCommandKindV1 Kind,
    Hash256 RequestHash,
    AuthorityFactEnvelopeV1 CommandEnvelope,
    GraphRuntimeOwnerPayloadV1 CommandOuter,
    GraphRuntimeCommandV1 Command,
    AuthorityFactEnvelopeV1? ResultEnvelope);

internal sealed record PendingGraphRuntimeCommandV1(
    GraphRuntimeJournalOperationV1 Operation,
    GraphRuntimeEvaluationV1 Evaluation,
    CapacityGrantSnapshotV1? ActivationGrant);

internal sealed record GraphRuntimeJournalProofV1(
    CapacityGrantSnapshotV1? GraphProof,
    CapacityGrantSnapshotV1? RuntimeActivationProof);

internal abstract record GraphRuntimeJournalFoldResultV1
{
    private GraphRuntimeJournalFoldResultV1() { }

    internal sealed record Current(long SnapshotThrough, CurrentAuthorityVectorSnapshotV1 Authority,
        GraphRuntimeSnapshotV1? Snapshot, PendingGraphRuntimeCommandV1? Pending,
        IReadOnlyList<GraphRuntimeJournalOperationV1> Operations) : GraphRuntimeJournalFoldResultV1;

    internal sealed record RuntimeReplaced(RuntimeGenerationId Next, long TerminatedAt, long SnapshotThrough,
        GraphRuntimeSnapshotV1? Snapshot, PendingGraphRuntimeCommandV1? Pending,
        AuthorityFactEnvelopeV1? TerminalResultFact) : GraphRuntimeJournalFoldResultV1;

    internal sealed record AuthorityGenerationReplaced(AuthorityAxisId Axis, StableId128 Next, long TerminatedAt,
        long SnapshotThrough, GraphRuntimeSnapshotV1? Snapshot, PendingGraphRuntimeCommandV1? Pending,
        AuthorityFactEnvelopeV1? TerminalResultFact) : GraphRuntimeJournalFoldResultV1;

    internal sealed record InvalidHistory(BoundedAscii Code, long LastVerified) : GraphRuntimeJournalFoldResultV1;
}

internal abstract record GraphRuntimeJournalInspectionV1
{
    private GraphRuntimeJournalInspectionV1() { }
    internal sealed record Other(AuthorityFactEnvelopeV1 Envelope) : GraphRuntimeJournalInspectionV1;
    internal sealed record Command(AuthorityFactEnvelopeV1 Envelope, GraphRuntimeOwnerPayloadV1 Outer,
        GraphRuntimeCommandV1 Body) : GraphRuntimeJournalInspectionV1;
    internal sealed record Fact(AuthorityFactEnvelopeV1 Envelope, GraphRuntimeOwnerPayloadV1 Outer,
        GraphRuntimeFactV1 Body) : GraphRuntimeJournalInspectionV1;
    internal sealed record Invalid(AuthorityFactEnvelopeV1? Envelope, BoundedAscii Code) : GraphRuntimeJournalInspectionV1;
}

internal static class GraphRuntimeJournalFoldV1
{
    internal const int MaximumFacts = 65_536;
    internal const int MaximumOperations = 256;
    internal const int MaximumPendingCommands = 1;

    private static readonly AuthorityPayloadRegistrationV1 CommandRegistration = GraphRuntimePayloadRegistrationsV1.Command;
    private static readonly AuthorityPayloadRegistrationV1 FactRegistration = GraphRuntimePayloadRegistrationsV1.Fact;

    internal static Accumulator CreateAccumulator(SessionAuthorityStampV1 session) => new(session);

    internal static GraphRuntimeJournalFoldResultV1 Fold(SessionAuthorityStampV1 session,
        IEnumerable<(AuthorityFactEnvelopeV1 Envelope, GraphRuntimeJournalProofV1? Proof)> facts)
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
        private readonly GraphReplacementJournalFoldV1.Accumulator _graph;
        private readonly Dictionary<OperationId, GraphRuntimeJournalOperationV1> _operations = [];
        private long _expectedPosition = 1;
        private int _factCount;
        private GraphRuntimeSnapshotV1? _snapshot;
        private PendingGraphRuntimeCommandV1? _pending;
        private Terminal? _terminal;
        private GraphRuntimeJournalFoldResultV1.InvalidHistory? _invalid;

        internal Accumulator(SessionAuthorityStampV1 session)
        {
            if (!session.IsValid) throw new ArgumentException("A valid session is required.", nameof(session));
            _session = session;
            _graph = GraphReplacementJournalFoldV1.CreateAccumulator(session);
        }

        internal GraphRuntimeJournalInspectionV1 Inspect(AuthorityFactEnvelopeV1? envelope)
        {
            if (envelope is null)
                return new GraphRuntimeJournalInspectionV1.Invalid(null, Code("null-runtime-history"));
            if (envelope.PayloadSchema == CommandRegistration.Schema)
                return InspectCommand(envelope);
            if (envelope.PayloadSchema == FactRegistration.Schema)
                return InspectFact(envelope);
            if (envelope.PayloadSchema.SchemaId == CommandRegistration.Schema.SchemaId ||
                envelope.PayloadSchema.SchemaId == FactRegistration.Schema.SchemaId)
                return new GraphRuntimeJournalInspectionV1.Invalid(envelope, Code("unknown-graph-runtime-version"));
            return new GraphRuntimeJournalInspectionV1.Other(envelope);
        }

        internal void Apply(GraphRuntimeJournalInspectionV1? inspected, GraphRuntimeJournalProofV1? proof = null)
        {
            if (_invalid is not null) return;
            var envelope = EnvelopeOf(inspected);
            if (envelope is null || envelope.Position.Session != _session || envelope.Position.Sequence != _expectedPosition)
            { Fail("noncontiguous-history"); return; }
            if (_factCount == MaximumFacts) { Fail("runtime-fact-bound"); return; }

            if (_terminal is { Runtime: true })
            { Fail("facts-after-runtime-replacement"); return; }

            if (_terminal is not null && inspected is GraphRuntimeJournalInspectionV1.Command)
            { Fail("runtime-command-after-generation-replacement"); return; }

            var transitionDecode = AuthorityGenerationTransitionCodecV1.Decode(envelope.PayloadSchema, envelope.Owner,
                _session, envelope.PayloadMemory, out var transition);

            var graphInspection = _graph.Inspect(envelope);
            var graphProofRequired = graphInspection.CapacityReference is not null;
            if (graphProofRequired != (proof?.GraphProof is not null))
            { Fail(graphProofRequired ? "graph-proof-missing" : "unexpected-graph-proof"); return; }
            _graph.Apply(graphInspection, proof?.GraphProof);
            if (_graph.Failure is { } graphFailure)
            { _invalid = new(Code("invalid-graph-history"), graphFailure.LastVerifiedPosition); return; }

            try
            {
                switch (inspected)
                {
                    case GraphRuntimeJournalInspectionV1.Invalid invalid:
                        Fail(invalid.Code.ToString()); return;
                    case GraphRuntimeJournalInspectionV1.Command command:
                        if (proof?.GraphProof is not null ||
                            (proof?.RuntimeActivationProof is not null && command.Body is not GraphRuntimeCommandV1.Activate))
                        { Fail("unexpected-runtime-proof"); return; }
                        ApplyCommand(command, proof?.RuntimeActivationProof); break;
                    case GraphRuntimeJournalInspectionV1.Fact fact:
                        if (proof?.RuntimeActivationProof is not null) { Fail("unexpected-runtime-proof"); return; }
                        ApplyFact(fact); break;
                    case GraphRuntimeJournalInspectionV1.Other:
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

        internal GraphRuntimeJournalFoldResultV1 Complete()
        {
            if (_invalid is not null) return _invalid;
            var graph = _graph.Complete();
            if (graph is GraphReplacementJournalFoldResultV1.InvalidHistory invalid)
                return new GraphRuntimeJournalFoldResultV1.InvalidHistory(Code("invalid-graph-history"), invalid.LastVerifiedPosition);
            if (_terminal is { } terminal)
                return terminal.Runtime
                    ? new GraphRuntimeJournalFoldResultV1.RuntimeReplaced(RuntimeGenerationId.FromValue(terminal.Next), terminal.At,
                        _expectedPosition - 1, _snapshot, _pending, terminal.Result)
                    : new GraphRuntimeJournalFoldResultV1.AuthorityGenerationReplaced(terminal.Axis, terminal.Next, terminal.At,
                        _expectedPosition - 1, _snapshot, _pending, terminal.Result);
            if (graph is GraphReplacementJournalFoldResultV1.RuntimeReplaced replaced)
                return new GraphRuntimeJournalFoldResultV1.RuntimeReplaced(replaced.Replacement, replaced.LastPosition,
                    _expectedPosition - 1, _snapshot, _pending, null);
            if (graph is not GraphReplacementJournalFoldResultV1.Current current)
                return Invalid("invalid-graph-history", _expectedPosition - 1);
            return new GraphRuntimeJournalFoldResultV1.Current(_expectedPosition - 1, current.Authority, _snapshot, _pending,
                Array.AsReadOnly(_operations.Values.OrderBy(static x => x.CommandEnvelope.Position.Sequence).ToArray()));
        }

        internal long LastVerifiedPosition => _expectedPosition - 1;

        private void ApplyCommand(GraphRuntimeJournalInspectionV1.Command item, CapacityGrantSnapshotV1? proof)
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
            if (item.Body is GraphRuntimeCommandV1.Activate && proof is null)
            { Fail("runtime-activation-proof-missing"); return; }

            if (_graph.Complete() is not GraphReplacementJournalFoldResultV1.Current graphCurrent ||
                graphCurrent.SnapshotThrough != item.Envelope.Position.Sequence)
            { Fail("runtime-graph-evidence-invalid"); return; }
            var verified = new GraphReplacementSnapshotReadResultV1.Verified(graphCurrent, graphCurrent.SnapshotThrough);
            var evidence = GraphRuntimeCurrentGraphEvidenceV1.From(verified);
            var evaluation = GraphRuntimeReducerV1.Evaluate(_snapshot, item.Body, item.Envelope.Position,
                item.Outer.ExpectedAuthority, evidence, proof);
            var operation = new GraphRuntimeJournalOperationV1(item.Body.OperationId, item.Body.Kind,
                item.Body.EffectRequestHash, item.Envelope, item.Outer, item.Body, null);
            _operations.Add(item.Body.OperationId, operation);
            _pending = new(operation, evaluation, proof);
        }

        private void ApplyFact(GraphRuntimeJournalInspectionV1.Fact item)
        {
            if (_pending is null || item.Body.CommandFact != _pending.Operation.CommandEnvelope.Position ||
                item.Envelope.FactId != GraphRuntimeFactIdsV1.Result(_pending.Operation.CommandEnvelope.Position))
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

        private bool ApplyNormalResult(GraphRuntimeFactV1 fact, JournalPositionV1 resultPosition,
            PendingGraphRuntimeCommandV1 pending)
        {
            switch (pending.Evaluation)
            {
                case GraphRuntimeReducerV1.EffectRequired required:
                    GraphRuntimeResolutionV1 resolution = fact.Outcome is GraphRuntimeOutcomeV1.Activated or GraphRuntimeOutcomeV1.Retired
                        ? fact.EffectReceiptHash is { } receipt
                            ? GraphRuntimeReducerV1.Resolve(required, new GraphRuntimeEffectResolutionV1.Completed(receipt), resultPosition)
                            : new GraphRuntimeResolutionV1.Rejected(required.Prior, Code("missing-runtime-receipt"))
                        : GraphRuntimeReducerV1.Resolve(required, new GraphRuntimeEffectResolutionV1.Refused(fact.SafeCode!.Value),
                            resultPosition);
                    return MatchesResolution(fact, resolution);
                case GraphRuntimeEvaluationV1.Rejected rejected:
                    return fact.Outcome == GraphRuntimeOutcomeV1.Rejected && fact.ResultingSnapshot == rejected.Snapshot &&
                        fact.EffectReceiptHash is null && fact.SafeCode == rejected.SafeCode;
                case GraphRuntimeEvaluationV1.Conflict conflict:
                    return fact.Outcome == GraphRuntimeOutcomeV1.Conflict && fact.ResultingSnapshot == conflict.Snapshot &&
                        fact.EffectReceiptHash is null && fact.SafeCode?.ToString() == "runtime-predecessor-conflict";
                case GraphRuntimeEvaluationV1.GenerationReplaced replaced:
                    return fact.Outcome == GraphRuntimeOutcomeV1.GenerationReplaced && fact.ResultingSnapshot == replaced.Snapshot &&
                        fact.EffectReceiptHash is null && fact.SafeCode?.ToString() == "generation-replaced";
                default: return false;
            }
        }

        private bool MatchesResolution(GraphRuntimeFactV1 fact, GraphRuntimeResolutionV1 resolution) => resolution switch
        {
            GraphRuntimeResolutionV1.Applied applied => fact.Outcome == applied.Outcome &&
                fact.ResultingSnapshot == applied.Snapshot && fact.EffectReceiptHash == applied.ReceiptHash && fact.SafeCode is null,
            GraphRuntimeResolutionV1.Rejected rejected => fact.Outcome == GraphRuntimeOutcomeV1.Rejected &&
                fact.ResultingSnapshot == rejected.Snapshot && fact.EffectReceiptHash is null && fact.SafeCode == rejected.SafeCode,
            _ => false,
        };

        private bool MatchesTerminalResult(GraphRuntimeFactV1 fact, JournalPositionV1 resultPosition,
            PendingGraphRuntimeCommandV1 pending, Terminal terminal)
        {
            if (terminal.Runtime) return false;
            if (fact.Outcome == GraphRuntimeOutcomeV1.GenerationReplaced)
                return fact.ResultingSnapshot == _snapshot && fact.EffectReceiptHash is null &&
                    fact.SafeCode?.ToString() == "generation-replaced";
            return ApplyNormalResult(fact, resultPosition, pending);
        }

        private void CompleteOperation(AuthorityFactEnvelopeV1 result, bool keepPending)
        {
            var operation = _pending!.Operation with { ResultEnvelope = result };
            _operations[operation.OperationId] = operation;
            if (result.PayloadSchema == FactRegistration.Schema && GraphRuntimeCodecsV1.TryDecodeOuter(result.PayloadMemory, out var outer) &&
                GraphRuntimeCodecsV1.TryDecodeFact(outer!.Body, out var body) && body!.ResultingSnapshot is { } resulting)
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

        private static JournalPositionV1 ActualPredecessor(PendingGraphRuntimeCommandV1 pending) =>
            pending.Evaluation switch
            {
                GraphRuntimeReducerV1.EffectRequired required => required.ActualPredecessor,
                GraphRuntimeEvaluationV1.Conflict conflict => conflict.ActualPredecessor,
                _ when pending.Operation.Command is GraphRuntimeCommandV1.Activate activate => activate.GraphAuthorityFact,
                _ when pending.Operation.Command is GraphRuntimeCommandV1.Retire && pending.Evaluation is GraphRuntimeEvaluationV1.Rejected rejected && rejected.Snapshot is { } prior => prior.LastRuntimeFact,
                _ => pending.Operation.Command.ExpectedPredecessor,
            };

        private GraphRuntimeJournalInspectionV1 InspectCommand(AuthorityFactEnvelopeV1 envelope) =>
            GraphRuntimePayloadRegistrationsV1.ValidateCommandEnvelope(envelope) &&
            GraphRuntimeCodecsV1.TryDecodeOuter(envelope.PayloadMemory, out var outer) &&
            GraphRuntimeCodecsV1.TryDecodeCommand(outer!.Body, out var body)
                ? new GraphRuntimeJournalInspectionV1.Command(envelope, outer, body!)
                : new GraphRuntimeJournalInspectionV1.Invalid(envelope, Code("invalid-graph-runtime-command"));

        private GraphRuntimeJournalInspectionV1 InspectFact(AuthorityFactEnvelopeV1 envelope) =>
            GraphRuntimePayloadRegistrationsV1.ValidateFactEnvelope(envelope) &&
            GraphRuntimeCodecsV1.TryDecodeOuter(envelope.PayloadMemory, out var outer) &&
            GraphRuntimeCodecsV1.TryDecodeFact(outer!.Body, out var body)
                ? new GraphRuntimeJournalInspectionV1.Fact(envelope, outer, body!)
                : new GraphRuntimeJournalInspectionV1.Invalid(envelope, Code("invalid-graph-runtime-fact"));

        private static AuthorityFactEnvelopeV1? EnvelopeOf(GraphRuntimeJournalInspectionV1? inspected) => inspected switch
        {
            GraphRuntimeJournalInspectionV1.Other x => x.Envelope,
            GraphRuntimeJournalInspectionV1.Command x => x.Envelope,
            GraphRuntimeJournalInspectionV1.Fact x => x.Envelope,
            GraphRuntimeJournalInspectionV1.Invalid x => x.Envelope,
            _ => null,
        };

        private static bool SameAuthority(ExpectedAuthorityVectorV1 left, ExpectedAuthorityVectorV1 right) => left == right;
        private void Fail(string code) => _invalid = Invalid(code, _expectedPosition - 1);
    }

    private sealed record Terminal(AuthorityAxisId Axis, StableId128 Next, long At, bool Runtime,
        AuthorityFactEnvelopeV1? Result);
    private static BoundedAscii Code(string value) => new(value);
    private static GraphRuntimeJournalFoldResultV1.InvalidHistory Invalid(string code, long lastVerified) =>
        new(Code(code), lastVerified);
}
