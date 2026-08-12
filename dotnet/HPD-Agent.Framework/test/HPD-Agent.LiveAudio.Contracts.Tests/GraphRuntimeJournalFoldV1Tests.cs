using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;
using System.Formats.Cbor;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphRuntimeJournalFoldV1Tests
{
    [Fact]
    public async Task CompletedEffectAfterClaimedTransition_RetainsExactReceiptSnapshotAndRejectsMutation()
    {
        var f = await ClaimedFixture.CreateAsync();
        var operation = OperationId.Create();
        var command = new GraphRuntimeCommandV1.Activate(operation, f.Installation.Position, f.Installation.Position,
            f.Plan.Fingerprint, f.Graph, f.Grant.CurrentFact, GraphRuntimeEffectHashesV1.Activate(f.Session,
                operation, f.Installation.Position, f.Plan.Fingerprint, f.Graph, f.Grant.CurrentFact));
        var c = f.Command(command); var next = ActivityGenerationId.Create();
        var transition = f.Transition(next, c.Position.Sequence + 1);
        var resultPosition = new JournalPositionV1(f.Session, transition.Position.Sequence + 1);
        var receipt = Hash(77);
        var active = new GraphRuntimeSnapshotV1(GraphRuntimePhaseV1.Active, f.Graph, f.Plan.Fingerprint,
            f.Grant.CurrentFact, f.Authority, operation, resultPosition, resultPosition, null);
        var body = new GraphRuntimeFactV1(c.Position, command.ExpectedPredecessor, f.Installation.Position,
            GraphRuntimeOutcomeV1.Activated, active, receipt, null);
        var result = f.Fact(body, resultPosition.Sequence); var tail = f.Other(resultPosition.Sequence + 1);
        var inputs = f.Facts.Append(c).Append(transition).Append(result).Append(tail).Select(e => (e,
            e.Position == f.Installation.Position ? new GraphRuntimeJournalProofV1(f.Grant, null) :
            e.Position == c.Position ? new GraphRuntimeJournalProofV1(null, f.Grant) : null));
        var terminal = Assert.IsType<GraphRuntimeJournalFoldResultV1.AuthorityGenerationReplaced>(
            GraphRuntimeJournalFoldV1.Fold(f.Session, inputs));
        Assert.Null(terminal.Pending); Assert.Equal(active, terminal.Snapshot);
        Assert.Equal(result, terminal.TerminalResultFact); Assert.Equal(tail.Position.Sequence, terminal.SnapshotThrough);

        var forgedSnapshot = new GraphRuntimeSnapshotV1(GraphRuntimePhaseV1.Active, f.Graph, Hash(78),
            f.Grant.CurrentFact, f.Authority, operation, resultPosition, resultPosition, null);
        var mutated = f.Fact(new GraphRuntimeFactV1(c.Position, command.ExpectedPredecessor,
            f.Installation.Position, GraphRuntimeOutcomeV1.Activated, forgedSnapshot, receipt, null), resultPosition.Sequence);
        var hostile = f.Facts.Append(c).Append(transition).Append(mutated).Select(e => (e,
            e.Position == f.Installation.Position ? new GraphRuntimeJournalProofV1(f.Grant, null) :
            e.Position == c.Position ? new GraphRuntimeJournalProofV1(null, f.Grant) : null));
        Assert.IsType<GraphRuntimeJournalFoldResultV1.InvalidHistory>(GraphRuntimeJournalFoldV1.Fold(f.Session, hostile));
    }
    [Fact]
    public async Task UnclaimedOptionalAxisTransition_DoesNotTerminatePendingCommand()
    {
        var f=await GraphRuntimeReducerV1Tests.Fixture.CreateAsync();var facts=(await ReadAll(f)).ToList();
        var activity=ActivityGenerationId.Create();facts.Add(Initialization(f,AuthorityAxisId.Activity,Stable(activity),5));
        var operation=OperationId.Create();var command=Activate(f,operation);var commandEnvelope=CommandEnvelope(f,command,f.Authority,6);facts.Add(commandEnvelope);
        facts.Add(Transition(f,AuthorityAxisId.Activity,Stable(activity),Stable(ActivityGenerationId.Create()),7));
        var raw=GraphRuntimeJournalFoldV1.Fold(f.Session,facts.Select(e=>(e,e.Position==f.Installation.Position?new GraphRuntimeJournalProofV1(f.Grant,null):e.Position==commandEnvelope.Position?new GraphRuntimeJournalProofV1(null,f.Grant):null)));Assert.True(raw is GraphRuntimeJournalFoldResultV1.Current,raw.ToString());var folded=(GraphRuntimeJournalFoldResultV1.Current)raw;
        Assert.Equal(7,folded.SnapshotThrough);Assert.NotNull(folded.Pending);Assert.Null(folded.Snapshot);
    }

    [Fact]
    public async Task ClaimedAxisTransition_AcceptsSoleJoinedTerminalFactClearsPendingAndAllowsTail()
    {
        var f=await GraphRuntimeReducerV1Tests.Fixture.CreateAsync();var facts=(await ReadAll(f)).ToList();
        var activity=ActivityGenerationId.Create();facts.Add(Initialization(f,AuthorityAxisId.Activity,Stable(activity),5));
        var claimed=ExpectedAuthorityVectorV1.Create(f.Session,[new AuthorityAxisValueV1.Graph(f.GraphGeneration),new AuthorityAxisValueV1.Activity(activity)]);
        var operation=OperationId.Create();var command=Activate(f,operation);var commandEnvelope=CommandEnvelope(f,command,claimed,6);facts.Add(commandEnvelope);
        var next=ActivityGenerationId.Create();facts.Add(Transition(f,AuthorityAxisId.Activity,Stable(activity),Stable(next),7));
        var terminalBody=new GraphRuntimeFactV1(commandEnvelope.Position,command.ExpectedPredecessor,f.Installation.Position,
            GraphRuntimeOutcomeV1.GenerationReplaced,null,null,new BoundedAscii("generation-replaced"));
        var terminal=FactEnvelope(f,terminalBody,claimed,8);facts.Add(terminal);facts.Add(Unrelated(f,9));
        var raw=GraphRuntimeJournalFoldV1.Fold(f.Session,facts.Select(e=>(e,e.Position==f.Installation.Position?new GraphRuntimeJournalProofV1(f.Grant,null):e.Position==commandEnvelope.Position?new GraphRuntimeJournalProofV1(null,f.Grant):null)));Assert.True(raw is GraphRuntimeJournalFoldResultV1.AuthorityGenerationReplaced,raw.ToString());var folded=(GraphRuntimeJournalFoldResultV1.AuthorityGenerationReplaced)raw;
        Assert.Equal(AuthorityAxisId.Activity,folded.Axis);Assert.Equal(9,folded.SnapshotThrough);Assert.Null(folded.Pending);
        Assert.Null(folded.Snapshot);Assert.Equal(terminal,folded.TerminalResultFact);
    }
    [Fact]
    public async Task ExactActivateCommandAndResult_FoldToActiveCurrent()
    {
        var fixture = await GraphRuntimeReducerV1Tests.Fixture.CreateAsync();
        var operation = OperationId.Create();
        var command = Activate(fixture, operation);
        var admitted = await fixture.AppendCommandAsync(command);
        var graph = Assert.IsType<GraphReplacementSnapshotReadResultV1.Verified>(
            await GraphReplacementSnapshotReaderV1.ReadAsync(fixture.Journal, fixture.Session));
        var evidence = GraphRuntimeCurrentGraphEvidenceV1.From(graph);
        var required = Assert.IsType<GraphRuntimeReducerV1.EffectRequired>(
            GraphRuntimeReducerV1.Evaluate(null, command, admitted.Position, fixture.Authority, evidence, fixture.Grant));
        var resultPosition = new JournalPositionV1(fixture.Session, admitted.Position.Sequence + 1);
        var active = Assert.IsType<GraphRuntimeResolutionV1.Applied>(
            GraphRuntimeReducerV1.Resolve(required, new GraphRuntimeEffectResolutionV1.Completed(Hash(9)), resultPosition));
        var result = await fixture.AppendFactAsync(new GraphRuntimeFactV1(admitted.Position,
            command.ExpectedPredecessor, fixture.Installation.Position, GraphRuntimeOutcomeV1.Activated,
            active.Snapshot, Hash(9), null), admitted.Position.Sequence);

        var folded = Assert.IsType<GraphRuntimeJournalFoldResultV1.Current>(
            GraphRuntimeJournalFoldV1.Fold(fixture.Session, await Inputs(fixture, admitted, result, includeRuntimeProof: true)));
        Assert.Equal(result.Position.Sequence, folded.SnapshotThrough);
        Assert.Equal(GraphRuntimePhaseV1.Active, folded.Snapshot!.Phase);
        Assert.Null(folded.Pending);
        var operationRow = Assert.Single(folded.Operations);
        Assert.Equal(operation, operationRow.OperationId);
        Assert.Equal(result, operationRow.ResultEnvelope);
    }

    [Fact]
    public async Task ActivateWithoutHistoricalGrantProof_IsInvalidAtPriorPrefix()
    {
        var fixture = await GraphRuntimeReducerV1Tests.Fixture.CreateAsync();
        var admitted = await fixture.AppendCommandAsync(Activate(fixture, OperationId.Create()));
        var invalid = Assert.IsType<GraphRuntimeJournalFoldResultV1.InvalidHistory>(
            GraphRuntimeJournalFoldV1.Fold(fixture.Session,
                await Inputs(fixture, admitted, null, includeRuntimeProof: false)));
        Assert.Equal("runtime-activation-proof-missing", invalid.Code.ToString());
        Assert.Equal(admitted.Position.Sequence - 1, invalid.LastVerified);
    }

    [Fact]
    public async Task SecondPendingCommand_IsInvalidAndDoesNotReplaceFirst()
    {
        var fixture = await GraphRuntimeReducerV1Tests.Fixture.CreateAsync();
        var first = await fixture.AppendCommandAsync(Activate(fixture, OperationId.Create()));
        var secondCommand = Activate(fixture, OperationId.Create());
        var second = await fixture.AppendCommandAsync(secondCommand, first.Position.Sequence);
        var inputs = await ReadAll(fixture);
        var folded = Assert.IsType<GraphRuntimeJournalFoldResultV1.InvalidHistory>(
            GraphRuntimeJournalFoldV1.Fold(fixture.Session, inputs.Select(envelope =>
                (envelope, envelope.Position == fixture.Installation.Position
                    ? new GraphRuntimeJournalProofV1(fixture.Grant, null)
                    : envelope.Position == first.Position || envelope.Position == second.Position
                        ? new GraphRuntimeJournalProofV1(null, fixture.Grant)
                        : null))));
        Assert.Equal("second-pending-runtime-command", folded.Code.ToString());
        Assert.Equal(first.Position.Sequence, folded.LastVerified);
    }

    [Fact]
    public async Task ProofChannels_AreExactForGraphAndRuntimeProtocols()
    {
        var fixture = await GraphRuntimeReducerV1Tests.Fixture.CreateAsync();
        var command = await fixture.AppendCommandAsync(Activate(fixture, OperationId.Create()));
        var facts = await ReadAll(fixture);

        var missingGraph = Assert.IsType<GraphRuntimeJournalFoldResultV1.InvalidHistory>(
            GraphRuntimeJournalFoldV1.Fold(fixture.Session, facts.Select(envelope =>
                (envelope, envelope.Position == command.Position
                    ? new GraphRuntimeJournalProofV1(null, fixture.Grant) : null))));
        Assert.Equal("graph-proof-missing", missingGraph.Code.ToString());

        var first = facts[0];
        var extraneousGraph = Assert.IsType<GraphRuntimeJournalFoldResultV1.InvalidHistory>(
            GraphRuntimeJournalFoldV1.Fold(fixture.Session, facts.Select(envelope =>
                (envelope, envelope.Position == first.Position
                    ? new GraphRuntimeJournalProofV1(fixture.Grant, null)
                    : envelope.Position == fixture.Installation.Position
                        ? new GraphRuntimeJournalProofV1(fixture.Grant, null)
                        : envelope.Position == command.Position
                            ? new GraphRuntimeJournalProofV1(null, fixture.Grant) : null))));
        Assert.Equal("unexpected-graph-proof", extraneousGraph.Code.ToString());

        var runtimeProofOnInstall = Assert.IsType<GraphRuntimeJournalFoldResultV1.InvalidHistory>(
            GraphRuntimeJournalFoldV1.Fold(fixture.Session, facts.Select(envelope =>
                (envelope, envelope.Position == fixture.Installation.Position
                    ? new GraphRuntimeJournalProofV1(fixture.Grant, fixture.Grant)
                    : envelope.Position == command.Position
                        ? new GraphRuntimeJournalProofV1(null, fixture.Grant) : null))));
        Assert.Equal("unexpected-runtime-proof", runtimeProofOnInstall.Code.ToString());
    }

    [Fact]
    public async Task SuccessSnapshotCannotForgeItsEnclosingResultPosition()
    {
        var fixture = await GraphRuntimeReducerV1Tests.Fixture.CreateAsync();
        var operation = OperationId.Create();
        var command = Activate(fixture, operation);
        var admitted = await fixture.AppendCommandAsync(command);
        var graph = Assert.IsType<GraphReplacementSnapshotReadResultV1.Verified>(
            await GraphReplacementSnapshotReaderV1.ReadAsync(fixture.Journal, fixture.Session));
        var required = Assert.IsType<GraphRuntimeReducerV1.EffectRequired>(GraphRuntimeReducerV1.Evaluate(
            null, command, admitted.Position, fixture.Authority,
            GraphRuntimeCurrentGraphEvidenceV1.From(graph), fixture.Grant));
        var resultPosition = new JournalPositionV1(fixture.Session, admitted.Position.Sequence + 1);
        var applied = Assert.IsType<GraphRuntimeResolutionV1.Applied>(GraphRuntimeReducerV1.Resolve(
            required, new GraphRuntimeEffectResolutionV1.Completed(Hash(9)), resultPosition));
        var admittedResult = await fixture.AppendFactAsync(new GraphRuntimeFactV1(admitted.Position,
            command.ExpectedPredecessor, fixture.Installation.Position, GraphRuntimeOutcomeV1.Activated,
            applied.Snapshot, Hash(9), null), admitted.Position.Sequence);

        var forgedSnapshot = new GraphRuntimeSnapshotV1(GraphRuntimePhaseV1.Active,
            applied.Snapshot.GraphGeneration, applied.Snapshot.TopologyFingerprint,
            applied.Snapshot.CapacityGrantFact, applied.Snapshot.CurrentAuthority,
            applied.Snapshot.ActivationOperationId, new JournalPositionV1(fixture.Session, resultPosition.Sequence + 1),
            new JournalPositionV1(fixture.Session, resultPosition.Sequence + 1), null);
        var forgedBody = GraphRuntimeCodecsV1.EncodeFact(new GraphRuntimeFactV1(admitted.Position,
            command.ExpectedPredecessor, fixture.Installation.Position, GraphRuntimeOutcomeV1.Activated,
            forgedSnapshot, Hash(9), null));
        var forgedPayload = GraphRuntimeCodecsV1.EncodeOuter(new GraphRuntimeOwnerPayloadV1(
            fixture.Session, fixture.Authority, forgedBody));
        var registration = GraphRuntimePayloadRegistrationsV1.Fact;
        var forged = new AuthorityFactEnvelopeV1(admittedResult.FactId, admittedResult.Position, null,
            admittedResult.Owner, admittedResult.PayloadSchema, forgedPayload,
            AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, forgedPayload),
            admittedResult.Correlation, admittedResult.ObservedAt, admittedResult.AdmittedAt,
            admittedResult.Integrity);
        var facts = (await ReadAll(fixture)).Select(envelope => envelope.Position == admittedResult.Position ? forged : envelope);
        var invalid = Assert.IsType<GraphRuntimeJournalFoldResultV1.InvalidHistory>(
            GraphRuntimeJournalFoldV1.Fold(fixture.Session, facts.Select(envelope =>
                (envelope, envelope.Position == fixture.Installation.Position
                    ? new GraphRuntimeJournalProofV1(fixture.Grant, null)
                    : envelope.Position == admitted.Position
                        ? new GraphRuntimeJournalProofV1(null, fixture.Grant) : null))));
        Assert.Equal("invalid-graph-runtime-fact", invalid.Code.ToString());
        Assert.Equal(admitted.Position.Sequence, invalid.LastVerified);
    }

    [Fact]
    public async Task ResultBeforeCommand_IsUnmatchedAtThePriorPrefix()
    {
        var f = await GraphRuntimeReducerV1Tests.Fixture.CreateAsync();
        var facts = await ReadAll(f); var template = facts[^1];
        var referenced = new JournalPositionV1(f.Session, f.Installation.Position.Sequence - 1);
        var orphan = RuntimeFactEnvelope(f, template, template.Position.Sequence + 1,
            new GraphRuntimeFactV1(referenced, new(f.Session, 1), new(f.Session, 1),
                GraphRuntimeOutcomeV1.Rejected, null, null, new BoundedAscii("runtime-not-active")));
        var invalid = Assert.IsType<GraphRuntimeJournalFoldResultV1.InvalidHistory>(
            GraphRuntimeJournalFoldV1.Fold(f.Session, BaseInputs(f, facts).Append((orphan, null))));
        Assert.Equal("unmatched-runtime-result", invalid.Code.ToString());
        Assert.Equal(template.Position.Sequence, invalid.LastVerified);
    }

    [Theory]
    [InlineData(false, "duplicate-runtime-command")]
    [InlineData(true, "runtime-operation-identity-reuse")]
    public async Task ResolvedOperationCannotBeAdmittedAgain(bool changed, string code)
    {
        var f = await GraphRuntimeReducerV1Tests.Fixture.CreateAsync();
        var facts = await ReadAll(f); var template = facts[^1]; var operation = OperationId.Create();
        var first = Retire(f, operation, f.Installation.Position);
        var c1 = RuntimeCommandEnvelope(f, template, template.Position.Sequence + 1, first);
        var f1 = RuntimeFactEnvelope(f, template, template.Position.Sequence + 2,
            Rejected(c1.Position, first.ExpectedPredecessor));
        var second = changed ? Retire(f, operation, new(f.Session, f.Installation.Position.Sequence - 1)) : first;
        var c2 = RuntimeCommandEnvelope(f, template, template.Position.Sequence + 3, second);
        var invalid = Assert.IsType<GraphRuntimeJournalFoldResultV1.InvalidHistory>(
            GraphRuntimeJournalFoldV1.Fold(f.Session, BaseInputs(f, facts)
                .Append((c1, null)).Append((f1, null)).Append((c2, null))));
        Assert.Equal(code, invalid.Code.ToString());
        Assert.Equal(f1.Position.Sequence, invalid.LastVerified);
    }

    [Fact]
    public async Task TwoHundredFiftySeventhOperation_ExceedsExactBound()
    {
        var f = await GraphRuntimeReducerV1Tests.Fixture.CreateAsync();
        var facts = await ReadAll(f); var template = facts[^1];
        var inputs = BaseInputs(f, facts).ToList(); long position = template.Position.Sequence;
        for (var index = 0; index < GraphRuntimeJournalFoldV1.MaximumOperations; index++)
        {
            var command = Retire(f, OperationId.Create(), f.Installation.Position);
            var c = RuntimeCommandEnvelope(f, template, ++position, command);
            inputs.Add((c, null));
            inputs.Add((RuntimeFactEnvelope(f, template, ++position,
                Rejected(c.Position, command.ExpectedPredecessor)), null));
        }
        var overflow = Retire(f, OperationId.Create(), f.Installation.Position);
        inputs.Add((RuntimeCommandEnvelope(f, template, ++position, overflow), null));
        var invalid = Assert.IsType<GraphRuntimeJournalFoldResultV1.InvalidHistory>(
            GraphRuntimeJournalFoldV1.Fold(f.Session, inputs));
        Assert.Equal("runtime-operation-bound", invalid.Code.ToString());
        Assert.Equal(position - 1, invalid.LastVerified);
    }

    [Fact]
    public async Task RuntimeReplacement_IsTerminalAndRejectsTail()
    {
        var f = await GraphRuntimeReducerV1Tests.Fixture.CreateAsync();
        var facts = await ReadAll(f); var template = facts[^1]; var next = RuntimeGenerationId.Create();
        var transition = RuntimeTransitionEnvelope(f, template, template.Position.Sequence + 1, next);
        var terminal = Assert.IsType<GraphRuntimeJournalFoldResultV1.RuntimeReplaced>(
            GraphRuntimeJournalFoldV1.Fold(f.Session, BaseInputs(f, facts).Append((transition, null))));
        Assert.Equal(next, terminal.Next);
        var tail = CloneAt(template, transition.Position.Sequence + 1);
        var invalid = Assert.IsType<GraphRuntimeJournalFoldResultV1.InvalidHistory>(
            GraphRuntimeJournalFoldV1.Fold(f.Session, BaseInputs(f, facts)
                .Append((transition, null)).Append((tail, null))));
        Assert.Equal("facts-after-runtime-replacement", invalid.Code.ToString());
        Assert.Equal(transition.Position.Sequence, invalid.LastVerified);
    }

    private static GraphRuntimeCommandV1.Activate Activate(GraphRuntimeReducerV1Tests.Fixture fixture,
        OperationId operation) => new(operation, fixture.Installation.Position, fixture.Installation.Position,
        fixture.Plan.Fingerprint, fixture.GraphGeneration, fixture.Grant.CurrentFact,
        GraphRuntimeEffectHashesV1.Activate(fixture.Session, operation, fixture.Installation.Position,
            fixture.Plan.Fingerprint, fixture.GraphGeneration, fixture.Grant.CurrentFact));

    private static GraphRuntimeCommandV1.Retire Retire(GraphRuntimeReducerV1Tests.Fixture f,
        OperationId operation, JournalPositionV1 activeFact) => new(operation, f.Installation.Position,
        activeFact, GraphRuntimeEffectHashesV1.Retire(f.Session, operation, activeFact));

    private static GraphRuntimeFactV1 Rejected(JournalPositionV1 command, JournalPositionV1 predecessor) =>
        new(command, predecessor, predecessor, GraphRuntimeOutcomeV1.Rejected, null, null,
            new BoundedAscii("runtime-not-active"));

    private static IEnumerable<(AuthorityFactEnvelopeV1 Envelope, GraphRuntimeJournalProofV1? Proof)> BaseInputs(
        GraphRuntimeReducerV1Tests.Fixture f, IEnumerable<AuthorityFactEnvelopeV1> facts) =>
        facts.Select(envelope => (envelope, envelope.Position == f.Installation.Position
            ? new GraphRuntimeJournalProofV1(f.Grant, null) : null));

    private static AuthorityFactEnvelopeV1 RuntimeCommandEnvelope(GraphRuntimeReducerV1Tests.Fixture f,
        AuthorityFactEnvelopeV1 template, long position, GraphRuntimeCommandV1 command)
    {
        var body = GraphRuntimeCodecsV1.EncodeCommand(command);
        var payload = GraphRuntimeCodecsV1.EncodeOuter(new GraphRuntimeOwnerPayloadV1(f.Session, f.Authority, body));
        var registration = GraphRuntimePayloadRegistrationsV1.Command;
        return Envelope(template, position, GraphRuntimeFactIdsV1.Command(f.Session, command.OperationId, command.Kind),
            OwnerSliceId.S2, registration.Schema, payload,
            AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload));
    }

    private static AuthorityFactEnvelopeV1 RuntimeFactEnvelope(GraphRuntimeReducerV1Tests.Fixture f,
        AuthorityFactEnvelopeV1 template, long position, GraphRuntimeFactV1 fact)
    {
        var body = GraphRuntimeCodecsV1.EncodeFact(fact);
        var payload = GraphRuntimeCodecsV1.EncodeOuter(new GraphRuntimeOwnerPayloadV1(f.Session, f.Authority, body));
        var registration = GraphRuntimePayloadRegistrationsV1.Fact;
        return Envelope(template, position, GraphRuntimeFactIdsV1.Result(fact.CommandFact), OwnerSliceId.S2,
            registration.Schema, payload,
            AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload));
    }

    private static AuthorityFactEnvelopeV1 RuntimeTransitionEnvelope(GraphRuntimeReducerV1Tests.Fixture f,
        AuthorityFactEnvelopeV1 template, long position, RuntimeGenerationId next)
    {
        Span<byte> expected = stackalloc byte[16]; Span<byte> proposed = stackalloc byte[16];
        Assert.True(f.Session.RuntimeGenerationId.TryWriteBytes(expected)); Assert.True(next.TryWriteBytes(proposed));
        var payload = AuthorityGenerationTransitionCodecV1.Encode(f.Session, AuthorityAxisId.Runtime,
            StableId128.FromBytes(expected), StableId128.FromBytes(proposed));
        var schema = AuthorityGenerationTransitionCodecV1.SchemaFor(AuthorityAxisId.Runtime);
        var token = AuthorityGenerationTransitionCodecV1.SchemaTokenFor(AuthorityAxisId.Runtime);
        return Envelope(template, position, JournalFactId.Create(), OwnerSliceId.S1, schema, payload,
            AuthorityPayloadHashV1.Compute(token, schema, payload));
    }

    private static AuthorityFactEnvelopeV1 CloneAt(AuthorityFactEnvelopeV1 template, long position) =>
        Envelope(template, position, template.FactId, template.Owner, template.PayloadSchema,
            template.PayloadMemory.ToArray(), template.PayloadHash);

    private static AuthorityFactEnvelopeV1 Envelope(AuthorityFactEnvelopeV1 template, long position,
        JournalFactId id, OwnerSliceId owner, SchemaReferenceV1 schema, byte[] payload, Hash256 hash) =>
        new(id, new JournalPositionV1(template.Position.Session, position), null, owner, schema, payload, hash,
            template.Correlation, template.ObservedAt, template.AdmittedAt, template.Integrity);

    private static async Task<IReadOnlyList<(AuthorityFactEnvelopeV1 Envelope, GraphRuntimeJournalProofV1? Proof)>> Inputs(
        GraphRuntimeReducerV1Tests.Fixture fixture, AuthorityFactEnvelopeV1 command,
        AuthorityFactEnvelopeV1? result, bool includeRuntimeProof)
    {
        var facts = await ReadAll(fixture);
        return facts.Select(envelope => (envelope,
            ProofFor(fixture, envelope, command, includeRuntimeProof))).ToArray();
    }

    private static GraphRuntimeJournalProofV1? ProofFor(GraphRuntimeReducerV1Tests.Fixture fixture,
        AuthorityFactEnvelopeV1 envelope, AuthorityFactEnvelopeV1 command, bool includeRuntimeProof)
    {
        if (envelope.Position == fixture.Installation.Position)
            return new GraphRuntimeJournalProofV1(fixture.Grant, null);
        if (includeRuntimeProof && envelope.Position == command.Position)
            return new GraphRuntimeJournalProofV1(null, fixture.Grant);
        return null;
    }

    private static async Task<IReadOnlyList<AuthorityFactEnvelopeV1>> ReadAll(
        GraphRuntimeReducerV1Tests.Fixture fixture)
    {
        var batch = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await fixture.Journal.ReadAsync(
            new ReadAuthorityRangeV1(fixture.Session, 0, long.MaxValue, 256,
                ProposedAuthorityFactV1.MaximumPayloadBytes)));
        return batch.Facts;
    }

    private static Hash256 Hash(byte value)
    {
        Hash256.TryCreate(Enumerable.Repeat(value, 32).ToArray(), out var hash);
        return hash;
    }

    private static AuthorityFactEnvelopeV1 CommandEnvelope(GraphRuntimeReducerV1Tests.Fixture f,GraphRuntimeCommandV1 command,ExpectedAuthorityVectorV1 authority,long sequence){var body=GraphRuntimeCodecsV1.EncodeCommand(command);var payload=GraphRuntimeCodecsV1.EncodeOuter(new GraphRuntimeOwnerPayloadV1(f.Session,authority,body));var r=GraphRuntimePayloadRegistrationsV1.Command;return Envelope(f,GraphRuntimeFactIdsV1.Command(f.Session,command.OperationId,command.Kind),new JournalPositionV1(f.Session,sequence),OwnerSliceId.S2,r.Schema,payload,AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,payload));}
    private static AuthorityFactEnvelopeV1 FactEnvelope(GraphRuntimeReducerV1Tests.Fixture f,GraphRuntimeFactV1 fact,ExpectedAuthorityVectorV1 authority,long sequence){var body=GraphRuntimeCodecsV1.EncodeFact(fact);var payload=GraphRuntimeCodecsV1.EncodeOuter(new GraphRuntimeOwnerPayloadV1(f.Session,authority,body));var r=GraphRuntimePayloadRegistrationsV1.Fact;return Envelope(f,GraphRuntimeFactIdsV1.Result(fact.CommandFact),new JournalPositionV1(f.Session,sequence),OwnerSliceId.S2,r.Schema,payload,AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,payload));}
    private static AuthorityFactEnvelopeV1 Initialization(GraphRuntimeReducerV1Tests.Fixture f,AuthorityAxisId axis,StableId128 generation,long sequence){var r=new AuthorityGenerationInitializationPayloadRegistrationV1(axis);var owner=AuthorityGenerationTransitionCodecV1.OwnerFor(axis);Span<byte>b=stackalloc byte[16];generation.TryWriteBytes(b);var w=new CborWriter(CborConformanceMode.Ctap2Canonical);w.WriteStartMap(3);w.WriteUInt64(1);SessionAuthorityStampV1Codec.Write(w,f.Session);w.WriteUInt64(2);w.WriteByteString(b);w.WriteUInt64(3);w.WriteUInt64((ushort)owner);w.WriteEndMap();var p=w.Encode();return Envelope(f,JournalFactId.Create(),new JournalPositionV1(f.Session,sequence),owner,r.Schema,p,AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,p));}
    private static AuthorityFactEnvelopeV1 Transition(GraphRuntimeReducerV1Tests.Fixture f,AuthorityAxisId axis,StableId128 oldValue,StableId128 newValue,long sequence){var p=AuthorityGenerationTransitionCodecV1.Encode(f.Session,axis,oldValue,newValue);var r=new AuthorityGenerationTransitionPayloadRegistrationV1(axis);var owner=AuthorityGenerationTransitionCodecV1.OwnerFor(axis);return Envelope(f,JournalFactId.Create(),new JournalPositionV1(f.Session,sequence),owner,r.Schema,p,AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,p));}
    private static AuthorityFactEnvelopeV1 Unrelated(GraphRuntimeReducerV1Tests.Fixture f,long sequence){var p=new byte[]{1};return Envelope(f,JournalFactId.Create(),new JournalPositionV1(f.Session,sequence),OwnerSliceId.S4,new SchemaReferenceV1(SchemaId.Create(),1,0),p,Hash256.Compute(p));}
    private static AuthorityFactEnvelopeV1 Envelope(GraphRuntimeReducerV1Tests.Fixture f,JournalFactId id,JournalPositionV1 position,OwnerSliceId owner,SchemaReferenceV1 schema,byte[] payload,Hash256 hash)=>new(id,position,null,owner,schema,payload,hash,new CorrelationEnvelopeV1(TenantId.Create()),new UtcInstant(1),new UtcInstant(2),new IntegrityEnvelopeV1(1,1,Hash(30),[]));
    private static StableId128 Stable(ActivityGenerationId value){Span<byte>b=stackalloc byte[16];value.TryWriteBytes(b);return StableId128.FromBytes(b);}

    internal sealed class ClaimedFixture
    {
        internal SessionAuthorityStampV1 Session = new(RuntimeGenerationId.Create(), LiveSessionId.Create());
        internal GraphGenerationId Graph = GraphGenerationId.Create(); internal ActivityGenerationId Activity = ActivityGenerationId.Create();
        internal ExpectedAuthorityVectorV1 Authority = null!; internal CapacityGrantSnapshotV1 Grant = null!;
        internal GraphTopologyPlanV1 Plan = null!; internal AuthorityFactEnvelopeV1 Installation = null!;
        internal List<AuthorityFactEnvelopeV1> Facts = []; private readonly TenantId _tenant = TenantId.Create();
        private readonly ClockDomainId _clock = ClockDomainId.Create(); private readonly BootId _boot = BootId.Create();
        internal static async Task<ClaimedFixture> CreateAsync()
        {
            var f=new ClaimedFixture();f.Authority=ExpectedAuthorityVectorV1.Create(f.Session,[new AuthorityAxisValueV1.Graph(f.Graph),new AuthorityAxisValueV1.Activity(f.Activity)]);
            var registry=new AuthorityPayloadAdmissionRegistryV1([new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Graph),new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Activity),new AuthorityGenerationTransitionPayloadRegistrationV1(AuthorityAxisId.Activity),new CapacityReservationPayloadRegistrationV1(),new CapacitySettlementPayloadRegistrationV1(),GraphReplacementPayloadRegistrationsV1.Installed,GraphRuntimePayloadRegistrationsV1.Command,GraphRuntimePayloadRegistrationsV1.Fact]);
            var journal=new InMemoryAuthorityJournalV1(registry,()=>new UtcInstant(100),new AuthorityJournalCapacityV1(2,32,4*1024*1024));
            await f.Init(journal,AuthorityAxisId.Graph,Stable(f.Graph));await f.Init(journal,AuthorityAxisId.Activity,Stable(f.Activity));
            var op=OperationId.Create();var charge=new CapacityChargeV1(new CapacityDimensionId(3),new CapacityScopeV1(f._tenant,null,new CapacitySubjectV1.Operation(op)),1,CapacityPurposeId.Create(),new CapacityChargeWindowV1.NoWindow());
            var request=new CapacityRequestV1(op,f.Authority,[charge],new(f._clock,f._boot,100),CapacityPriorityV1.Normal);var reserved=Assert.IsType<CapacityAdmissionResultV1.Granted>(await CapacityAdmissionCoordinatorV1.ReserveAsync(journal,request,new CapacityGrantExpiryV1.NoExpiry(),new(f._tenant,operationId:op),new(f._clock,f._boot,90),new UtcInstant(2)));
            var settleOp=OperationId.Create();var settle=new CapacitySettlementFactBodyV1(reserved.Grant.GrantId,settleOp,reserved.Envelope.Position,CapacitySettlementKindV1.Activated,[new CapacitySettlementChargeV1(charge.DimensionId,charge.Scope,charge.Purpose,1)],new(f._clock,f._boot,91));f.Grant=Assert.IsType<CapacityAdmissionResultV1.Settled>(await CapacityAdmissionCoordinatorV1.SettleAsync(journal,f.Session,settle,new(f._tenant,operationId:settleOp),new UtcInstant(3))).Grant;
            f.Plan=new(f.Session,f.Graph,f.Grant.GrantId,[new GraphTopologyNodeV1(new BoundedAscii("source"))],[],[new CapacityDimensionId(3)]);f.Installation=Assert.IsType<GraphTopologyInstallationAdmissionResultV1.Installed>(await GraphTopologyInstallationAdmissionV1.InstallAsync(journal,new(f.Session,f.Plan,f.Grant.CurrentFact,f.Authority,new(f._tenant),new UtcInstant(4)))).Envelope;
            f.Facts=(await Read(journal,f.Session)).ToList();return f;
        }
        private async Task Init(InMemoryAuthorityJournalV1 journal,AuthorityAxisId axis,StableId128 value){var r=new AuthorityGenerationInitializationPayloadRegistrationV1(axis);Span<byte>b=stackalloc byte[16];value.TryWriteBytes(b);var w=new CborWriter(CborConformanceMode.Ctap2Canonical);w.WriteStartMap(3);w.WriteUInt64(1);SessionAuthorityStampV1Codec.Write(w,Session);w.WriteUInt64(2);w.WriteByteString(b);w.WriteUInt64(3);w.WriteUInt64((ushort)AuthorityGenerationTransitionCodecV1.OwnerFor(axis));w.WriteEndMap();var p=w.Encode();var proposal=new ProposedAuthorityFactV1(JournalFactId.Create(),null,AuthorityGenerationTransitionCodecV1.OwnerFor(axis),r.Schema,p,AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,p),new(_tenant),new UtcInstant(1));Assert.IsType<AppendAuthorityResultV1.Committed>(await journal.AppendAsync(new(Session,Facts.Count,[],[proposal],ProposedAuthorityFactV1.MaximumPayloadBytes)));Facts=(await Read(journal,Session)).ToList();}
        internal AuthorityFactEnvelopeV1 Command(GraphRuntimeCommandV1 command)=>CommandEnvelopeLike(command,Facts[^1].Position.Sequence+1);
        private AuthorityFactEnvelopeV1 CommandEnvelopeLike(GraphRuntimeCommandV1 command,long n){var b=GraphRuntimeCodecsV1.EncodeCommand(command);var p=GraphRuntimeCodecsV1.EncodeOuter(new(Session,Authority,b));var r=GraphRuntimePayloadRegistrationsV1.Command;return Make(GraphRuntimeFactIdsV1.Command(Session,command.OperationId,command.Kind),n,OwnerSliceId.S2,r.Schema,p,AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,p));}
        internal AuthorityFactEnvelopeV1 Fact(GraphRuntimeFactV1 fact,long n){var b=GraphRuntimeCodecsV1.EncodeFact(fact);var p=GraphRuntimeCodecsV1.EncodeOuter(new(Session,Authority,b));var r=GraphRuntimePayloadRegistrationsV1.Fact;return Make(GraphRuntimeFactIdsV1.Result(fact.CommandFact),n,OwnerSliceId.S2,r.Schema,p,AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,p));}
        internal AuthorityFactEnvelopeV1 Transition(ActivityGenerationId next,long n){var p=AuthorityGenerationTransitionCodecV1.Encode(Session,AuthorityAxisId.Activity,Stable(Activity),Stable(next));var r=new AuthorityGenerationTransitionPayloadRegistrationV1(AuthorityAxisId.Activity);return Make(JournalFactId.Create(),n,OwnerSliceId.S3,r.Schema,p,AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,p));}
        internal AuthorityFactEnvelopeV1 Other(long n){var p=new byte[]{1};return Make(JournalFactId.Create(),n,OwnerSliceId.S4,new(SchemaId.Create(),1,0),p,Hash256.Compute(p));}
        private AuthorityFactEnvelopeV1 Make(JournalFactId id,long n,OwnerSliceId owner,SchemaReferenceV1 schema,byte[] p,Hash256 h)=>new(id,new(Session,n),null,owner,schema,p,h,new(_tenant),new UtcInstant(1),new UtcInstant(2),new IntegrityEnvelopeV1(1,1,Hash(30),[]));
        private static async Task<IReadOnlyList<AuthorityFactEnvelopeV1>> Read(InMemoryAuthorityJournalV1 j,SessionAuthorityStampV1 s)=>Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await j.ReadAsync(new(s,0,long.MaxValue,256,1_048_576))).Facts;
        private static StableId128 Stable(GraphGenerationId value){Span<byte>b=stackalloc byte[16];value.TryWriteBytes(b);return StableId128.FromBytes(b);}
        private static StableId128 Stable(ActivityGenerationId value){Span<byte>b=stackalloc byte[16];value.TryWriteBytes(b);return StableId128.FromBytes(b);}
    }
}
