using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;
using System.Formats.Cbor;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphRuntimeJournalFoldTests
{
    [Fact]
    public async Task PureRenderer_MapsCompletedAndRejectsWrongPositionOrMissingEffect()
    {
        var f=await GraphRuntimeReducerTests.Fixture.CreateAsync();var operation=OperationId.Create();
        var command=Activate(f,operation);var admitted=await f.AppendCommandAsync(command);
        var current=Assert.IsType<GraphRuntimeJournalFoldResult.Current>(GraphRuntimeJournalFold.Fold(
            f.Session,await Inputs(f,admitted,null,true)));var pending=Assert.IsType<PendingGraphRuntimeCommand>(current.Pending);
        var resultPosition=new JournalPositionV1(f.Session,current.SnapshotThrough+1);
        var rendered=Assert.IsType<GraphRuntimeRenderedFact>(GraphRuntimeJournalFold.RenderFact(current,pending,
            resultPosition,new GraphRuntimeEffectResolution.Completed(Hash(41))));
        Assert.Equal(GraphRuntimeOutcome.Activated,rendered.Body.Outcome);Assert.Equal(resultPosition,rendered.Body.ResultingSnapshot!.LastRuntimeFact);
        Assert.Null(GraphRuntimeJournalFold.RenderFact(current,pending,resultPosition));
        Assert.Null(GraphRuntimeJournalFold.RenderFact(current,pending,new JournalPositionV1(f.Session,resultPosition.Sequence+1),new GraphRuntimeEffectResolution.Completed(Hash(41))));
    }

    [Fact]
    public async Task PureRenderer_RuntimeTerminalForbidsF_AndAuthorityTerminalAllowsOnlyResolutionOrNotObserved()
    {
        var f=await GraphRuntimeReducerTests.Fixture.CreateAsync();var operation=OperationId.Create();var command=Activate(f,operation);
        var admitted=await f.AppendCommandAsync(command);var current=Assert.IsType<GraphRuntimeJournalFoldResult.Current>(
            GraphRuntimeJournalFold.Fold(f.Session,await Inputs(f,admitted,null,true)));var pending=current.Pending!;
        var nextPosition=new JournalPositionV1(f.Session,current.SnapshotThrough+1);Span<byte>nextBytes=stackalloc byte[16];RuntimeGenerationId.Create().TryWriteBytes(nextBytes);
        var runtime=new GraphRuntimeJournalFoldResult.RuntimeReplaced(RuntimeGenerationId.FromValue(StableId128.FromBytes(nextBytes)),current.SnapshotThrough,current.SnapshotThrough,current.Snapshot,pending,null);
        Assert.Null(GraphRuntimeJournalFold.RenderFact(runtime,pending,nextPosition,new GraphRuntimeEffectResolution.Completed(Hash(42))));
        Span<byte>axisBytes=stackalloc byte[16];ActivityGenerationId.Create().TryWriteBytes(axisBytes);
        var terminal=new GraphRuntimeJournalFoldResult.AuthorityGenerationReplaced(AuthorityAxisId.Activity,StableId128.FromBytes(axisBytes),current.SnapshotThrough,current.SnapshotThrough,current.Snapshot,pending,null);
        var replaced=Assert.IsType<GraphRuntimeRenderedFact>(GraphRuntimeJournalFold.RenderFact(terminal,pending,nextPosition,null,true));
        Assert.Equal(GraphRuntimeOutcome.GenerationReplaced,replaced.Body.Outcome);
        Assert.NotNull(GraphRuntimeJournalFold.RenderFact(terminal,pending,nextPosition,new GraphRuntimeEffectResolution.Refused(new BoundedAscii("refused"))));
    }

    [Fact]
    public async Task PureRenderer_RejectsCopiedResolvedMissingAndAlreadyTerminalPending()
    {
        var f=await GraphRuntimeReducerTests.Fixture.CreateAsync();var command=Activate(f,OperationId.Create());
        var admitted=await f.AppendCommandAsync(command);var current=Assert.IsType<GraphRuntimeJournalFoldResult.Current>(
            GraphRuntimeJournalFold.Fold(f.Session,await Inputs(f,admitted,null,true)));var pending=current.Pending!;
        var position=new JournalPositionV1(f.Session,current.SnapshotThrough+1);var effect=new GraphRuntimeEffectResolution.Completed(Hash(51));
        var copied=pending with{};Assert.Null(GraphRuntimeJournalFold.RenderFact(current,copied,position,effect));
        var missing=current with{Pending=null};Assert.Null(GraphRuntimeJournalFold.RenderFact(missing,pending,position,effect));
        var resultBearing=pending with{Operation=pending.Operation with{ResultEnvelope=admitted}};
        var bearingFold=current with{Pending=resultBearing};Assert.Null(GraphRuntimeJournalFold.RenderFact(bearingFold,resultBearing,position,effect));
        Span<byte>b=stackalloc byte[16];ActivityGenerationId.Create().TryWriteBytes(b);
        var terminal=new GraphRuntimeJournalFoldResult.AuthorityGenerationReplaced(AuthorityAxisId.Activity,
            StableId128.FromBytes(b),current.SnapshotThrough,current.SnapshotThrough,current.Snapshot,pending,admitted);
        Assert.Null(GraphRuntimeJournalFold.RenderFact(terminal,pending,position,effect));
        Assert.Null(GraphRuntimeJournalFold.RenderFact(current,pending,position,effect,true));
        Assert.Null(GraphRuntimeJournalFold.RenderFact(current,pending,position,null,true));
    }

    [Fact]
    public async Task PureRenderer_PreEffectClosedArmsMapExactly()
    {
        var f=await GraphRuntimeReducerTests.Fixture.CreateAsync();var template=Activate(f,OperationId.Create());
        var admitted=await f.AppendCommandAsync(template);var current=Assert.IsType<GraphRuntimeJournalFoldResult.Current>(
            GraphRuntimeJournalFold.Fold(f.Session,await Inputs(f,admitted,null,true)));var basis=current.Pending!;
        var position=new JournalPositionV1(f.Session,current.SnapshotThrough+1);
        GraphRuntimeRenderedFact Render(GraphRuntimeEvaluation evaluation)
        {var p=basis with{Evaluation=evaluation};var fold=current with{Pending=p};return Assert.IsType<GraphRuntimeRenderedFact>(GraphRuntimeJournalFold.RenderFact(fold,p,position));}
        Assert.Equal(GraphRuntimeOutcome.Rejected,Render(new GraphRuntimeEvaluation.Rejected(null,new BoundedAscii("no"))).Body.Outcome);
        Assert.Equal(GraphRuntimeOutcome.Conflict,Render(new GraphRuntimeEvaluation.Conflict(null,f.Installation.Position)).Body.Outcome);
        Assert.Equal(GraphRuntimeOutcome.GenerationReplaced,Render(new GraphRuntimeEvaluation.GenerationReplaced(null)).Body.Outcome);
    }
    [Fact]
    public async Task CompletedEffectAfterClaimedTransition_RetainsExactReceiptSnapshotAndRejectsMutation()
    {
        var f = await ClaimedFixture.CreateAsync();
        var operation = OperationId.Create();
        var command = new GraphRuntimeCommand.Activate(operation, f.Installation.Position, f.Installation.Position,
            f.Plan.Fingerprint, f.Graph, f.Grant.CurrentFact, GraphRuntimeEffectHashes.Activate(f.Session,
                operation, f.Installation.Position, f.Plan.Fingerprint, f.Graph, f.Grant.CurrentFact));
        var c = f.Command(command); var next = ActivityGenerationId.Create();
        var transition = f.Transition(next, c.Position.Sequence + 1);
        var resultPosition = new JournalPositionV1(f.Session, transition.Position.Sequence + 1);
        var receipt = Hash(77);
        var active = new GraphRuntimeSnapshot(GraphRuntimePhase.Active, f.Graph, f.Plan.Fingerprint,
            f.Grant.CurrentFact, f.Authority, operation, resultPosition, resultPosition, null);
        var body = new GraphRuntimeFact(c.Position, command.ExpectedPredecessor, f.Installation.Position,
            GraphRuntimeOutcome.Activated, active, receipt, null);
        var result = f.Fact(body, resultPosition.Sequence); var tail = f.Other(resultPosition.Sequence + 1);
        var inputs = f.Facts.Append(c).Append(transition).Append(result).Append(tail).Select(e => (e,
            e.Position == f.Installation.Position ? new GraphRuntimeJournalProof(f.Grant, null) :
            e.Position == c.Position ? new GraphRuntimeJournalProof(null, f.Grant) : null));
        var terminal = Assert.IsType<GraphRuntimeJournalFoldResult.AuthorityGenerationReplaced>(
            GraphRuntimeJournalFold.Fold(f.Session, inputs));
        Assert.Null(terminal.Pending); Assert.Equal(active, terminal.Snapshot);
        Assert.Equal(c, terminal.TerminalCommandFact); Assert.Equal(result, terminal.TerminalResultFact);
        Assert.Equal(tail.Position.Sequence, terminal.SnapshotThrough);

        var forgedSnapshot = new GraphRuntimeSnapshot(GraphRuntimePhase.Active, f.Graph, Hash(78),
            f.Grant.CurrentFact, f.Authority, operation, resultPosition, resultPosition, null);
        var mutated = f.Fact(new GraphRuntimeFact(c.Position, command.ExpectedPredecessor,
            f.Installation.Position, GraphRuntimeOutcome.Activated, forgedSnapshot, receipt, null), resultPosition.Sequence);
        var hostile = f.Facts.Append(c).Append(transition).Append(mutated).Select(e => (e,
            e.Position == f.Installation.Position ? new GraphRuntimeJournalProof(f.Grant, null) :
            e.Position == c.Position ? new GraphRuntimeJournalProof(null, f.Grant) : null));
        Assert.IsType<GraphRuntimeJournalFoldResult.InvalidHistory>(GraphRuntimeJournalFold.Fold(f.Session, hostile));
    }
    [Fact]
    public async Task UnclaimedOptionalAxisTransition_DoesNotTerminatePendingCommand()
    {
        var f=await GraphRuntimeReducerTests.Fixture.CreateAsync();var facts=(await ReadAll(f)).ToList();
        var activity=ActivityGenerationId.Create();facts.Add(Initialization(f,AuthorityAxisId.Activity,Stable(activity),5));
        var operation=OperationId.Create();var command=Activate(f,operation);var commandEnvelope=CommandEnvelope(f,command,f.Authority,6);facts.Add(commandEnvelope);
        facts.Add(Transition(f,AuthorityAxisId.Activity,Stable(activity),Stable(ActivityGenerationId.Create()),7));
        var raw=GraphRuntimeJournalFold.Fold(f.Session,facts.Select(e=>(e,e.Position==f.Installation.Position?new GraphRuntimeJournalProof(f.Grant,null):e.Position==commandEnvelope.Position?new GraphRuntimeJournalProof(null,f.Grant):null)));Assert.True(raw is GraphRuntimeJournalFoldResult.Current,raw.ToString());var folded=(GraphRuntimeJournalFoldResult.Current)raw;
        Assert.Equal(7,folded.SnapshotThrough);Assert.NotNull(folded.Pending);Assert.Null(folded.Snapshot);
    }

    [Fact]
    public async Task ClaimedAxisTransition_AcceptsSoleJoinedTerminalFactClearsPendingAndAllowsTail()
    {
        var f=await GraphRuntimeReducerTests.Fixture.CreateAsync();var facts=(await ReadAll(f)).ToList();
        var activity=ActivityGenerationId.Create();facts.Add(Initialization(f,AuthorityAxisId.Activity,Stable(activity),5));
        var claimed=ExpectedAuthorityVectorV1.Create(f.Session,[new AuthorityAxisValueV1.Graph(f.GraphGeneration),new AuthorityAxisValueV1.Activity(activity)]);
        var operation=OperationId.Create();var command=Activate(f,operation);var commandEnvelope=CommandEnvelope(f,command,claimed,6);facts.Add(commandEnvelope);
        var next=ActivityGenerationId.Create();facts.Add(Transition(f,AuthorityAxisId.Activity,Stable(activity),Stable(next),7));
        var terminalBody=new GraphRuntimeFact(commandEnvelope.Position,command.ExpectedPredecessor,f.Installation.Position,
            GraphRuntimeOutcome.GenerationReplaced,null,null,new BoundedAscii("generation-replaced"));
        var terminal=FactEnvelope(f,terminalBody,claimed,8);facts.Add(terminal);facts.Add(Unrelated(f,9));
        var raw=GraphRuntimeJournalFold.Fold(f.Session,facts.Select(e=>(e,e.Position==f.Installation.Position?new GraphRuntimeJournalProof(f.Grant,null):e.Position==commandEnvelope.Position?new GraphRuntimeJournalProof(null,f.Grant):null)));Assert.True(raw is GraphRuntimeJournalFoldResult.AuthorityGenerationReplaced,raw.ToString());var folded=(GraphRuntimeJournalFoldResult.AuthorityGenerationReplaced)raw;
        Assert.Equal(AuthorityAxisId.Activity,folded.Axis);Assert.Equal(9,folded.SnapshotThrough);Assert.Null(folded.Pending);
        Assert.Null(folded.Snapshot);Assert.Equal(commandEnvelope,folded.TerminalCommandFact);Assert.Equal(terminal,folded.TerminalResultFact);
    }
    [Fact]
    public async Task ExactActivateCommandAndResult_FoldToActiveCurrent()
    {
        var fixture = await GraphRuntimeReducerTests.Fixture.CreateAsync();
        var operation = OperationId.Create();
        var command = Activate(fixture, operation);
        var admitted = await fixture.AppendCommandAsync(command);
        var graph = Assert.IsType<GraphReplacementSnapshotReadResult.Verified>(
            await GraphReplacementSnapshotReader.ReadAsync(fixture.Journal, fixture.Session));
        var evidence = GraphRuntimeCurrentGraphEvidence.From(graph);
        var required = Assert.IsType<GraphRuntimeReducer.EffectRequired>(
            GraphRuntimeReducer.Evaluate(null, command, admitted.Position, fixture.Authority, evidence, fixture.Grant));
        var resultPosition = new JournalPositionV1(fixture.Session, admitted.Position.Sequence + 1);
        var active = Assert.IsType<GraphRuntimeResolution.Applied>(
            GraphRuntimeReducer.Resolve(required, new GraphRuntimeEffectResolution.Completed(Hash(9)), resultPosition));
        var result = await fixture.AppendFactAsync(new GraphRuntimeFact(admitted.Position,
            command.ExpectedPredecessor, fixture.Installation.Position, GraphRuntimeOutcome.Activated,
            active.Snapshot, Hash(9), null), admitted.Position.Sequence);

        var folded = Assert.IsType<GraphRuntimeJournalFoldResult.Current>(
            GraphRuntimeJournalFold.Fold(fixture.Session, await Inputs(fixture, admitted, result, includeRuntimeProof: true)));
        Assert.Equal(result.Position.Sequence, folded.SnapshotThrough);
        Assert.Equal(GraphRuntimePhase.Active, folded.Snapshot!.Phase);
        Assert.Null(folded.Pending);
        var operationRow = Assert.Single(folded.Operations);
        Assert.Equal(operation, operationRow.OperationId);
        Assert.Equal(result, operationRow.ResultEnvelope);
    }

    [Fact]
    public async Task ActivateWithoutHistoricalGrantProof_IsInvalidAtPriorPrefix()
    {
        var fixture = await GraphRuntimeReducerTests.Fixture.CreateAsync();
        var admitted = await fixture.AppendCommandAsync(Activate(fixture, OperationId.Create()));
        var invalid = Assert.IsType<GraphRuntimeJournalFoldResult.InvalidHistory>(
            GraphRuntimeJournalFold.Fold(fixture.Session,
                await Inputs(fixture, admitted, null, includeRuntimeProof: false)));
        Assert.Equal("runtime-activation-proof-missing", invalid.Code.ToString());
        Assert.Equal(admitted.Position.Sequence - 1, invalid.LastVerified);
    }

    [Fact]
    public async Task SecondPendingCommand_IsInvalidAndDoesNotReplaceFirst()
    {
        var fixture = await GraphRuntimeReducerTests.Fixture.CreateAsync();
        var first = await fixture.AppendCommandAsync(Activate(fixture, OperationId.Create()));
        var secondCommand = Activate(fixture, OperationId.Create());
        var second = await fixture.AppendCommandAsync(secondCommand, first.Position.Sequence);
        var inputs = await ReadAll(fixture);
        var folded = Assert.IsType<GraphRuntimeJournalFoldResult.InvalidHistory>(
            GraphRuntimeJournalFold.Fold(fixture.Session, inputs.Select(envelope =>
                (envelope, envelope.Position == fixture.Installation.Position
                    ? new GraphRuntimeJournalProof(fixture.Grant, null)
                    : envelope.Position == first.Position || envelope.Position == second.Position
                        ? new GraphRuntimeJournalProof(null, fixture.Grant)
                        : null))));
        Assert.Equal("second-pending-runtime-command", folded.Code.ToString());
        Assert.Equal(first.Position.Sequence, folded.LastVerified);
    }

    [Fact]
    public async Task ProofChannels_AreExactForGraphAndRuntimeProtocols()
    {
        var fixture = await GraphRuntimeReducerTests.Fixture.CreateAsync();
        var command = await fixture.AppendCommandAsync(Activate(fixture, OperationId.Create()));
        var facts = await ReadAll(fixture);

        var missingGraph = Assert.IsType<GraphRuntimeJournalFoldResult.InvalidHistory>(
            GraphRuntimeJournalFold.Fold(fixture.Session, facts.Select(envelope =>
                (envelope, envelope.Position == command.Position
                    ? new GraphRuntimeJournalProof(null, fixture.Grant) : null))));
        Assert.Equal("graph-proof-missing", missingGraph.Code.ToString());

        var first = facts[0];
        var extraneousGraph = Assert.IsType<GraphRuntimeJournalFoldResult.InvalidHistory>(
            GraphRuntimeJournalFold.Fold(fixture.Session, facts.Select(envelope =>
                (envelope, envelope.Position == first.Position
                    ? new GraphRuntimeJournalProof(fixture.Grant, null)
                    : envelope.Position == fixture.Installation.Position
                        ? new GraphRuntimeJournalProof(fixture.Grant, null)
                        : envelope.Position == command.Position
                            ? new GraphRuntimeJournalProof(null, fixture.Grant) : null))));
        Assert.Equal("unexpected-graph-proof", extraneousGraph.Code.ToString());

        var runtimeProofOnInstall = Assert.IsType<GraphRuntimeJournalFoldResult.InvalidHistory>(
            GraphRuntimeJournalFold.Fold(fixture.Session, facts.Select(envelope =>
                (envelope, envelope.Position == fixture.Installation.Position
                    ? new GraphRuntimeJournalProof(fixture.Grant, fixture.Grant)
                    : envelope.Position == command.Position
                        ? new GraphRuntimeJournalProof(null, fixture.Grant) : null))));
        Assert.Equal("unexpected-runtime-proof", runtimeProofOnInstall.Code.ToString());
    }

    [Fact]
    public async Task SuccessSnapshotCannotForgeItsEnclosingResultPosition()
    {
        var fixture = await GraphRuntimeReducerTests.Fixture.CreateAsync();
        var operation = OperationId.Create();
        var command = Activate(fixture, operation);
        var admitted = await fixture.AppendCommandAsync(command);
        var graph = Assert.IsType<GraphReplacementSnapshotReadResult.Verified>(
            await GraphReplacementSnapshotReader.ReadAsync(fixture.Journal, fixture.Session));
        var required = Assert.IsType<GraphRuntimeReducer.EffectRequired>(GraphRuntimeReducer.Evaluate(
            null, command, admitted.Position, fixture.Authority,
            GraphRuntimeCurrentGraphEvidence.From(graph), fixture.Grant));
        var resultPosition = new JournalPositionV1(fixture.Session, admitted.Position.Sequence + 1);
        var applied = Assert.IsType<GraphRuntimeResolution.Applied>(GraphRuntimeReducer.Resolve(
            required, new GraphRuntimeEffectResolution.Completed(Hash(9)), resultPosition));
        var admittedResult = await fixture.AppendFactAsync(new GraphRuntimeFact(admitted.Position,
            command.ExpectedPredecessor, fixture.Installation.Position, GraphRuntimeOutcome.Activated,
            applied.Snapshot, Hash(9), null), admitted.Position.Sequence);

        var forgedSnapshot = new GraphRuntimeSnapshot(GraphRuntimePhase.Active,
            applied.Snapshot.GraphGeneration, applied.Snapshot.TopologyFingerprint,
            applied.Snapshot.CapacityGrantFact, applied.Snapshot.CurrentAuthority,
            applied.Snapshot.ActivationOperationId, new JournalPositionV1(fixture.Session, resultPosition.Sequence + 1),
            new JournalPositionV1(fixture.Session, resultPosition.Sequence + 1), null);
        var forgedBody = GraphRuntimeCodecs.EncodeFact(new GraphRuntimeFact(admitted.Position,
            command.ExpectedPredecessor, fixture.Installation.Position, GraphRuntimeOutcome.Activated,
            forgedSnapshot, Hash(9), null));
        var forgedPayload = GraphRuntimeCodecs.EncodeOuter(new GraphRuntimeOwnerPayload(
            fixture.Session, fixture.Authority, forgedBody));
        var registration = GraphRuntimePayloadRegistrations.Fact;
        var forged = new AuthorityFactEnvelopeV1(admittedResult.FactId, admittedResult.Position, null,
            admittedResult.Owner, admittedResult.PayloadSchema, forgedPayload,
            AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, forgedPayload),
            admittedResult.Correlation, admittedResult.ObservedAt, admittedResult.AdmittedAt,
            admittedResult.Integrity);
        var facts = (await ReadAll(fixture)).Select(envelope => envelope.Position == admittedResult.Position ? forged : envelope);
        var invalid = Assert.IsType<GraphRuntimeJournalFoldResult.InvalidHistory>(
            GraphRuntimeJournalFold.Fold(fixture.Session, facts.Select(envelope =>
                (envelope, envelope.Position == fixture.Installation.Position
                    ? new GraphRuntimeJournalProof(fixture.Grant, null)
                    : envelope.Position == admitted.Position
                        ? new GraphRuntimeJournalProof(null, fixture.Grant) : null))));
        Assert.Equal("invalid-graph-runtime-fact", invalid.Code.ToString());
        Assert.Equal(admitted.Position.Sequence, invalid.LastVerified);
    }

    [Fact]
    public async Task ResultBeforeCommand_IsUnmatchedAtThePriorPrefix()
    {
        var f = await GraphRuntimeReducerTests.Fixture.CreateAsync();
        var facts = await ReadAll(f); var template = facts[^1];
        var referenced = new JournalPositionV1(f.Session, f.Installation.Position.Sequence - 1);
        var orphan = RuntimeFactEnvelope(f, template, template.Position.Sequence + 1,
            new GraphRuntimeFact(referenced, new(f.Session, 1), new(f.Session, 1),
                GraphRuntimeOutcome.Rejected, null, null, new BoundedAscii("runtime-not-active")));
        var invalid = Assert.IsType<GraphRuntimeJournalFoldResult.InvalidHistory>(
            GraphRuntimeJournalFold.Fold(f.Session, BaseInputs(f, facts).Append((orphan, null))));
        Assert.Equal("unmatched-runtime-result", invalid.Code.ToString());
        Assert.Equal(template.Position.Sequence, invalid.LastVerified);
    }

    [Theory]
    [InlineData(false, "duplicate-runtime-command")]
    [InlineData(true, "runtime-operation-identity-reuse")]
    public async Task ResolvedOperationCannotBeAdmittedAgain(bool changed, string code)
    {
        var f = await GraphRuntimeReducerTests.Fixture.CreateAsync();
        var facts = await ReadAll(f); var template = facts[^1]; var operation = OperationId.Create();
        var first = Retire(f, operation, f.Installation.Position);
        var c1 = RuntimeCommandEnvelope(f, template, template.Position.Sequence + 1, first);
        var f1 = RuntimeFactEnvelope(f, template, template.Position.Sequence + 2,
            Rejected(c1.Position, first.ExpectedPredecessor));
        var second = changed ? Retire(f, operation, new(f.Session, f.Installation.Position.Sequence - 1)) : first;
        var c2 = RuntimeCommandEnvelope(f, template, template.Position.Sequence + 3, second);
        var invalid = Assert.IsType<GraphRuntimeJournalFoldResult.InvalidHistory>(
            GraphRuntimeJournalFold.Fold(f.Session, BaseInputs(f, facts)
                .Append((c1, null)).Append((f1, null)).Append((c2, null))));
        Assert.Equal(code, invalid.Code.ToString());
        Assert.Equal(f1.Position.Sequence, invalid.LastVerified);
    }

    [Fact]
    public async Task TwoHundredFiftySeventhOperation_ExceedsExactBound()
    {
        var f = await GraphRuntimeReducerTests.Fixture.CreateAsync();
        var facts = await ReadAll(f); var template = facts[^1];
        var inputs = BaseInputs(f, facts).ToList(); long position = template.Position.Sequence;
        for (var index = 0; index < GraphRuntimeJournalFold.MaximumOperations; index++)
        {
            var command = Retire(f, OperationId.Create(), f.Installation.Position);
            var c = RuntimeCommandEnvelope(f, template, ++position, command);
            inputs.Add((c, null));
            inputs.Add((RuntimeFactEnvelope(f, template, ++position,
                Rejected(c.Position, command.ExpectedPredecessor)), null));
        }
        var overflow = Retire(f, OperationId.Create(), f.Installation.Position);
        inputs.Add((RuntimeCommandEnvelope(f, template, ++position, overflow), null));
        var invalid = Assert.IsType<GraphRuntimeJournalFoldResult.InvalidHistory>(
            GraphRuntimeJournalFold.Fold(f.Session, inputs));
        Assert.Equal("runtime-operation-bound", invalid.Code.ToString());
        Assert.Equal(position - 1, invalid.LastVerified);
    }

    [Fact]
    public async Task RuntimeReplacement_IsTerminalAndRejectsTail()
    {
        var f = await GraphRuntimeReducerTests.Fixture.CreateAsync();
        var facts = await ReadAll(f); var template = facts[^1]; var next = RuntimeGenerationId.Create();
        var transition = RuntimeTransitionEnvelope(f, template, template.Position.Sequence + 1, next);
        var terminal = Assert.IsType<GraphRuntimeJournalFoldResult.RuntimeReplaced>(
            GraphRuntimeJournalFold.Fold(f.Session, BaseInputs(f, facts).Append((transition, null))));
        Assert.Equal(next, terminal.Next);
        var tail = CloneAt(template, transition.Position.Sequence + 1);
        var invalid = Assert.IsType<GraphRuntimeJournalFoldResult.InvalidHistory>(
            GraphRuntimeJournalFold.Fold(f.Session, BaseInputs(f, facts)
                .Append((transition, null)).Append((tail, null))));
        Assert.Equal("facts-after-runtime-replacement", invalid.Code.ToString());
        Assert.Equal(transition.Position.Sequence, invalid.LastVerified);
    }

    private static GraphRuntimeCommand.Activate Activate(GraphRuntimeReducerTests.Fixture fixture,
        OperationId operation) => new(operation, fixture.Installation.Position, fixture.Installation.Position,
        fixture.Plan.Fingerprint, fixture.GraphGeneration, fixture.Grant.CurrentFact,
        GraphRuntimeEffectHashes.Activate(fixture.Session, operation, fixture.Installation.Position,
            fixture.Plan.Fingerprint, fixture.GraphGeneration, fixture.Grant.CurrentFact));

    private static GraphRuntimeCommand.Retire Retire(GraphRuntimeReducerTests.Fixture f,
        OperationId operation, JournalPositionV1 activeFact) => new(operation, f.Installation.Position,
        activeFact, GraphRuntimeEffectHashes.Retire(f.Session, operation, activeFact));

    private static GraphRuntimeFact Rejected(JournalPositionV1 command, JournalPositionV1 predecessor) =>
        new(command, predecessor, predecessor, GraphRuntimeOutcome.Rejected, null, null,
            new BoundedAscii("runtime-not-active"));

    private static IEnumerable<(AuthorityFactEnvelopeV1 Envelope, GraphRuntimeJournalProof? Proof)> BaseInputs(
        GraphRuntimeReducerTests.Fixture f, IEnumerable<AuthorityFactEnvelopeV1> facts) =>
        facts.Select(envelope => (envelope, envelope.Position == f.Installation.Position
            ? new GraphRuntimeJournalProof(f.Grant, null) : null));

    private static AuthorityFactEnvelopeV1 RuntimeCommandEnvelope(GraphRuntimeReducerTests.Fixture f,
        AuthorityFactEnvelopeV1 template, long position, GraphRuntimeCommand command)
    {
        var body = GraphRuntimeCodecs.EncodeCommand(command);
        var payload = GraphRuntimeCodecs.EncodeOuter(new GraphRuntimeOwnerPayload(f.Session, f.Authority, body));
        var registration = GraphRuntimePayloadRegistrations.Command;
        return Envelope(template, position, GraphRuntimeFactIds.Command(f.Session, command.OperationId, command.Kind),
            OwnerSliceId.S2, registration.Schema, payload,
            AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload));
    }

    private static AuthorityFactEnvelopeV1 RuntimeFactEnvelope(GraphRuntimeReducerTests.Fixture f,
        AuthorityFactEnvelopeV1 template, long position, GraphRuntimeFact fact)
    {
        var body = GraphRuntimeCodecs.EncodeFact(fact);
        var payload = GraphRuntimeCodecs.EncodeOuter(new GraphRuntimeOwnerPayload(f.Session, f.Authority, body));
        var registration = GraphRuntimePayloadRegistrations.Fact;
        return Envelope(template, position, GraphRuntimeFactIds.Result(fact.CommandFact), OwnerSliceId.S2,
            registration.Schema, payload,
            AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload));
    }

    private static AuthorityFactEnvelopeV1 RuntimeTransitionEnvelope(GraphRuntimeReducerTests.Fixture f,
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

    private static async Task<IReadOnlyList<(AuthorityFactEnvelopeV1 Envelope, GraphRuntimeJournalProof? Proof)>> Inputs(
        GraphRuntimeReducerTests.Fixture fixture, AuthorityFactEnvelopeV1 command,
        AuthorityFactEnvelopeV1? result, bool includeRuntimeProof)
    {
        var facts = await ReadAll(fixture);
        return facts.Select(envelope => (envelope,
            ProofFor(fixture, envelope, command, includeRuntimeProof))).ToArray();
    }

    private static GraphRuntimeJournalProof? ProofFor(GraphRuntimeReducerTests.Fixture fixture,
        AuthorityFactEnvelopeV1 envelope, AuthorityFactEnvelopeV1 command, bool includeRuntimeProof)
    {
        if (envelope.Position == fixture.Installation.Position)
            return new GraphRuntimeJournalProof(fixture.Grant, null);
        if (includeRuntimeProof && envelope.Position == command.Position)
            return new GraphRuntimeJournalProof(null, fixture.Grant);
        return null;
    }

    private static async Task<IReadOnlyList<AuthorityFactEnvelopeV1>> ReadAll(
        GraphRuntimeReducerTests.Fixture fixture)
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

    private static AuthorityFactEnvelopeV1 CommandEnvelope(GraphRuntimeReducerTests.Fixture f,GraphRuntimeCommand command,ExpectedAuthorityVectorV1 authority,long sequence){var body=GraphRuntimeCodecs.EncodeCommand(command);var payload=GraphRuntimeCodecs.EncodeOuter(new GraphRuntimeOwnerPayload(f.Session,authority,body));var r=GraphRuntimePayloadRegistrations.Command;return Envelope(f,GraphRuntimeFactIds.Command(f.Session,command.OperationId,command.Kind),new JournalPositionV1(f.Session,sequence),OwnerSliceId.S2,r.Schema,payload,AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,payload));}
    private static AuthorityFactEnvelopeV1 FactEnvelope(GraphRuntimeReducerTests.Fixture f,GraphRuntimeFact fact,ExpectedAuthorityVectorV1 authority,long sequence){var body=GraphRuntimeCodecs.EncodeFact(fact);var payload=GraphRuntimeCodecs.EncodeOuter(new GraphRuntimeOwnerPayload(f.Session,authority,body));var r=GraphRuntimePayloadRegistrations.Fact;return Envelope(f,GraphRuntimeFactIds.Result(fact.CommandFact),new JournalPositionV1(f.Session,sequence),OwnerSliceId.S2,r.Schema,payload,AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,payload));}
    private static AuthorityFactEnvelopeV1 Initialization(GraphRuntimeReducerTests.Fixture f,AuthorityAxisId axis,StableId128 generation,long sequence){var r=new AuthorityGenerationInitializationPayloadRegistrationV1(axis);var owner=AuthorityGenerationTransitionCodecV1.OwnerFor(axis);Span<byte>b=stackalloc byte[16];generation.TryWriteBytes(b);var w=new CborWriter(CborConformanceMode.Ctap2Canonical);w.WriteStartMap(3);w.WriteUInt64(1);SessionAuthorityStampV1Codec.Write(w,f.Session);w.WriteUInt64(2);w.WriteByteString(b);w.WriteUInt64(3);w.WriteUInt64((ushort)owner);w.WriteEndMap();var p=w.Encode();return Envelope(f,JournalFactId.Create(),new JournalPositionV1(f.Session,sequence),owner,r.Schema,p,AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,p));}
    private static AuthorityFactEnvelopeV1 Transition(GraphRuntimeReducerTests.Fixture f,AuthorityAxisId axis,StableId128 oldValue,StableId128 newValue,long sequence){var p=AuthorityGenerationTransitionCodecV1.Encode(f.Session,axis,oldValue,newValue);var r=new AuthorityGenerationTransitionPayloadRegistrationV1(axis);var owner=AuthorityGenerationTransitionCodecV1.OwnerFor(axis);return Envelope(f,JournalFactId.Create(),new JournalPositionV1(f.Session,sequence),owner,r.Schema,p,AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,p));}
    private static AuthorityFactEnvelopeV1 Unrelated(GraphRuntimeReducerTests.Fixture f,long sequence){var p=new byte[]{1};return Envelope(f,JournalFactId.Create(),new JournalPositionV1(f.Session,sequence),OwnerSliceId.S4,new SchemaReferenceV1(SchemaId.Create(),1,0),p,Hash256.Compute(p));}
    private static AuthorityFactEnvelopeV1 Envelope(GraphRuntimeReducerTests.Fixture f,JournalFactId id,JournalPositionV1 position,OwnerSliceId owner,SchemaReferenceV1 schema,byte[] payload,Hash256 hash)=>new(id,position,null,owner,schema,payload,hash,new CorrelationEnvelopeV1(TenantId.Create()),new UtcInstant(1),new UtcInstant(2),new IntegrityEnvelopeV1(1,1,Hash(30),[]));
    private static StableId128 Stable(ActivityGenerationId value){Span<byte>b=stackalloc byte[16];value.TryWriteBytes(b);return StableId128.FromBytes(b);}

    internal sealed class ClaimedFixture
    {
        internal SessionAuthorityStampV1 Session = new(RuntimeGenerationId.Create(), LiveSessionId.Create());
        internal GraphGenerationId Graph = GraphGenerationId.Create(); internal ActivityGenerationId Activity = ActivityGenerationId.Create();
        internal TurnGenerationId Turn = TurnGenerationId.Create();
        internal ExpectedAuthorityVectorV1 Authority = null!; internal CapacityGrantSnapshotV1 Grant = null!;
        internal GraphTopologyPlan Plan = null!; internal AuthorityFactEnvelopeV1 Installation = null!;
        internal InMemoryAuthorityJournalV1 Journal = null!;
        internal List<AuthorityFactEnvelopeV1> Facts = []; private readonly TenantId _tenant = TenantId.Create();
        private readonly ClockDomainId _clock = ClockDomainId.Create(); private readonly BootId _boot = BootId.Create();
        internal static async Task<ClaimedFixture> CreateAsync()
        {
            var f=new ClaimedFixture();f.Authority=ExpectedAuthorityVectorV1.Create(f.Session,[new AuthorityAxisValueV1.Graph(f.Graph),new AuthorityAxisValueV1.Activity(f.Activity)]);
            var registry=new AuthorityPayloadAdmissionRegistryV1([new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Graph),new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Activity),new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Turn),new AuthorityGenerationTransitionPayloadRegistrationV1(AuthorityAxisId.Graph),new AuthorityGenerationTransitionPayloadRegistrationV1(AuthorityAxisId.Activity),new AuthorityGenerationTransitionPayloadRegistrationV1(AuthorityAxisId.Turn),new CapacityReservationPayloadRegistrationV1(),new CapacitySettlementPayloadRegistrationV1(),GraphReplacementPayloadRegistrations.Installed,GraphRuntimePayloadRegistrations.Command,GraphRuntimePayloadRegistrations.Fact]);
            var journal=f.Journal=new InMemoryAuthorityJournalV1(registry,()=>new UtcInstant(100),new AuthorityJournalCapacityV1(2,32,4*1024*1024));
            await f.Init(journal,AuthorityAxisId.Graph,Stable(f.Graph));await f.Init(journal,AuthorityAxisId.Activity,Stable(f.Activity));await f.Init(journal,AuthorityAxisId.Turn,Stable(f.Turn));
            var op=OperationId.Create();var charge=new CapacityChargeV1(new CapacityDimensionId(3),new CapacityScopeV1(f._tenant,null,new CapacitySubjectV1.Operation(op)),1,CapacityPurposeId.Create(),new CapacityChargeWindowV1.NoWindow());
            var request=new CapacityRequestV1(op,f.Authority,[charge],new(f._clock,f._boot,100),CapacityPriorityV1.Normal);var reserved=Assert.IsType<CapacityAdmissionResultV1.Granted>(await CapacityAdmissionCoordinatorV1.ReserveAsync(journal,request,new CapacityGrantExpiryV1.NoExpiry(),new(f._tenant,operationId:op),new(f._clock,f._boot,90),new UtcInstant(2)));
            var settleOp=OperationId.Create();var settle=new CapacitySettlementFactBodyV1(reserved.Grant.GrantId,settleOp,reserved.Envelope.Position,CapacitySettlementKindV1.Activated,[new CapacitySettlementChargeV1(charge.DimensionId,charge.Scope,charge.Purpose,1)],new(f._clock,f._boot,91));f.Grant=Assert.IsType<CapacityAdmissionResultV1.Settled>(await CapacityAdmissionCoordinatorV1.SettleAsync(journal,f.Session,settle,new(f._tenant,operationId:settleOp),new UtcInstant(3))).Grant;
            f.Plan=new(f.Session,f.Graph,f.Grant.GrantId,[new GraphTopologyNode(new BoundedAscii("source"))],[],[new CapacityDimensionId(3)]);f.Installation=Assert.IsType<GraphTopologyInstallationAdmissionResult.Installed>(await GraphTopologyInstallationAdmission.InstallAsync(journal,new(f.Session,f.Plan,f.Grant.CurrentFact,f.Authority,new(f._tenant),new UtcInstant(4)))).Envelope;
            f.Facts=(await Read(journal,f.Session)).ToList();return f;
        }
        private async Task Init(InMemoryAuthorityJournalV1 journal,AuthorityAxisId axis,StableId128 value){var r=new AuthorityGenerationInitializationPayloadRegistrationV1(axis);Span<byte>b=stackalloc byte[16];value.TryWriteBytes(b);var w=new CborWriter(CborConformanceMode.Ctap2Canonical);w.WriteStartMap(3);w.WriteUInt64(1);SessionAuthorityStampV1Codec.Write(w,Session);w.WriteUInt64(2);w.WriteByteString(b);w.WriteUInt64(3);w.WriteUInt64((ushort)AuthorityGenerationTransitionCodecV1.OwnerFor(axis));w.WriteEndMap();var p=w.Encode();var proposal=new ProposedAuthorityFactV1(JournalFactId.Create(),null,AuthorityGenerationTransitionCodecV1.OwnerFor(axis),r.Schema,p,AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,p),new(_tenant),new UtcInstant(1));Assert.IsType<AppendAuthorityResultV1.Committed>(await journal.AppendAsync(new(Session,Facts.Count,[],[proposal],ProposedAuthorityFactV1.MaximumPayloadBytes)));Facts=(await Read(journal,Session)).ToList();}
        internal AuthorityFactEnvelopeV1 Command(GraphRuntimeCommand command)=>CommandEnvelopeLike(command,Facts[^1].Position.Sequence+1);
        private AuthorityFactEnvelopeV1 CommandEnvelopeLike(GraphRuntimeCommand command,long n){var b=GraphRuntimeCodecs.EncodeCommand(command);var p=GraphRuntimeCodecs.EncodeOuter(new(Session,Authority,b));var r=GraphRuntimePayloadRegistrations.Command;return Make(GraphRuntimeFactIds.Command(Session,command.OperationId,command.Kind),n,OwnerSliceId.S2,r.Schema,p,AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,p));}
        internal AuthorityFactEnvelopeV1 Fact(GraphRuntimeFact fact,long n){var b=GraphRuntimeCodecs.EncodeFact(fact);var p=GraphRuntimeCodecs.EncodeOuter(new(Session,Authority,b));var r=GraphRuntimePayloadRegistrations.Fact;return Make(GraphRuntimeFactIds.Result(fact.CommandFact),n,OwnerSliceId.S2,r.Schema,p,AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,p));}
        internal AuthorityFactEnvelopeV1 Transition(ActivityGenerationId next,long n){var p=AuthorityGenerationTransitionCodecV1.Encode(Session,AuthorityAxisId.Activity,Stable(Activity),Stable(next));var r=new AuthorityGenerationTransitionPayloadRegistrationV1(AuthorityAxisId.Activity);return Make(JournalFactId.Create(),n,OwnerSliceId.S3,r.Schema,p,AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,p));}
        internal async Task AppendTransitionAsync(AuthorityAxisId axis,StableId128 current,StableId128 next)
        {
            var payload=AuthorityGenerationTransitionCodecV1.Encode(Session,axis,current,next);var registration=new AuthorityGenerationTransitionPayloadRegistrationV1(axis);
            var proposal=new ProposedAuthorityFactV1(JournalFactId.Create(),null,AuthorityGenerationTransitionCodecV1.OwnerFor(axis),registration.Schema,payload,AuthorityPayloadHashV1.Compute(registration.SchemaToken,registration.Schema,payload),new(_tenant),new UtcInstant(5));
            var head=Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await Journal.ReadAsync(new(Session,0,long.MaxValue,256,1_048_576))).SnapshotThrough;
            Assert.IsType<AppendAuthorityResultV1.Committed>(await Journal.AppendAsync(new(Session,head,[],[proposal],ProposedAuthorityFactV1.MaximumPayloadBytes)));
        }
        internal AuthorityFactEnvelopeV1 Other(long n){var p=new byte[]{1};return Make(JournalFactId.Create(),n,OwnerSliceId.S4,new(SchemaId.Create(),1,0),p,Hash256.Compute(p));}
        private AuthorityFactEnvelopeV1 Make(JournalFactId id,long n,OwnerSliceId owner,SchemaReferenceV1 schema,byte[] p,Hash256 h)=>new(id,new(Session,n),null,owner,schema,p,h,new(_tenant),new UtcInstant(1),new UtcInstant(2),new IntegrityEnvelopeV1(1,1,Hash(30),[]));
        private static async Task<IReadOnlyList<AuthorityFactEnvelopeV1>> Read(InMemoryAuthorityJournalV1 j,SessionAuthorityStampV1 s)=>Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await j.ReadAsync(new(s,0,long.MaxValue,256,1_048_576))).Facts;
        private static StableId128 Stable(GraphGenerationId value){Span<byte>b=stackalloc byte[16];value.TryWriteBytes(b);return StableId128.FromBytes(b);}
        private static StableId128 Stable(ActivityGenerationId value){Span<byte>b=stackalloc byte[16];value.TryWriteBytes(b);return StableId128.FromBytes(b);}
        private static StableId128 Stable(TurnGenerationId value){Span<byte>b=stackalloc byte[16];value.TryWriteBytes(b);return StableId128.FromBytes(b);}
        internal static StableId128 StableValue(GraphGenerationId value)=>Stable(value);
        internal static StableId128 StableValue(ActivityGenerationId value)=>Stable(value);
        internal static StableId128 StableValue(TurnGenerationId value)=>Stable(value);
    }
}
