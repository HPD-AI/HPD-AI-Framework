using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.Graph.Runtime;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphMediaWorkExecutionCoordinatorV1Tests
{
    [Fact]
    public async Task Durable_command_precedes_effect_and_exact_retry_does_not_execute_twice()
    {
        var f = Fixture(); var effects = new ScriptedEffects { ExecuteResult = new GraphMediaWorkEffectResultV1.Completed(Hash(90)) };
        var coordinator = new GraphMediaWorkExecutionCoordinatorV1(f.Journal, effects, f.Registry);
        var first = Assert.IsType<GraphMediaWorkExecutionResultV1.Completed>(await coordinator.ExecuteAsync(f.Request, default));
        Assert.Equal(1, effects.ExecuteCalls); Assert.Equal(0, effects.QueryCalls);
        Assert.Equal(GraphMediaWorkStateV1.Terminal, first.Ledger.Work[f.Registration.WorkId].State);
        Assert.Equal(2, first.Fact.Position.Sequence); Assert.Equal(1, first.Command.Position.Sequence);

        var retry = Assert.IsType<GraphMediaWorkExecutionResultV1.Completed>(await coordinator.ExecuteAsync(f.Request, default));
        Assert.Equal(first.Fact.FactId, retry.Fact.FactId); Assert.Equal(1, effects.ExecuteCalls); Assert.Equal(0, effects.QueryCalls);
        var projectedRetry = new GraphMediaWorkExecutionRequestV1(f.Request.OperationId, f.Request.Registration,
            f.Request.Residences, f.Request.Ownership, first.Ledger, f.Request.ExpectedAuthority,
            f.Request.Correlation, f.Request.EffectObservedAt, f.Request.ObservedAt);
        Assert.IsType<GraphMediaWorkExecutionResultV1.Completed>(await coordinator.ExecuteAsync(projectedRetry, default));
        Assert.Equal(1, effects.ExecuteCalls);
    }

    [Fact]
    public async Task Command_only_restart_queries_and_NotObserved_executes_once()
    {
        var f = Fixture(); await AppendCommandOnly(f);
        var effects = new ScriptedEffects
        {
            QueryResult = new GraphMediaWorkEffectQueryResultV1.NotObserved(),
            ExecuteResult = new GraphMediaWorkEffectResultV1.Completed(Hash(91))
        };
        var restarted = new GraphMediaWorkExecutionCoordinatorV1(f.Journal, effects, f.Registry);
        Assert.IsType<GraphMediaWorkExecutionResultV1.Completed>(await restarted.ExecuteAsync(f.Request, default));
        Assert.Equal(1, effects.QueryCalls); Assert.Equal(1, effects.ExecuteCalls);
    }

    [Fact]
    public async Task Query_and_execute_terminal_arms_project_into_D25C()
    {
        foreach (var result in new GraphMediaWorkEffectResultV1[]
        {
            new GraphMediaWorkEffectResultV1.Unknown(),
            new GraphMediaWorkEffectResultV1.Rejected(new("work-effect-rejected"))
        })
        {
            var f = Fixture(); var effects = new ScriptedEffects { ExecuteResult = result };
            var value = await new GraphMediaWorkExecutionCoordinatorV1(f.Journal, effects, f.Registry).ExecuteAsync(f.Request, default);
            if (result is GraphMediaWorkEffectResultV1.Unknown)
            {
                var unknown = Assert.IsType<GraphMediaWorkExecutionResultV1.Unknown>(value);
                Assert.Equal(GraphMediaWorkStateV1.Unknown, unknown.Ledger.Work[f.Registration.WorkId].State);
            }
            else
            {
                var rejected = Assert.IsType<GraphMediaWorkExecutionResultV1.Rejected>(value);
                Assert.Equal(GraphMediaWorkStateV1.Registered, rejected.Ledger.Work[f.Registration.WorkId].State);
            }
        }
    }

    [Fact]
    public async Task Invalid_authority_and_preinvoke_cancellation_make_no_journal_or_effect_mutation()
    {
        var f = Fixture(); var effects = new ScriptedEffects();
        var coordinator = new GraphMediaWorkExecutionCoordinatorV1(f.Journal, effects, f.Registry);
        var stale = new GraphMediaWorkExecutionRequestV1(f.Request.OperationId, f.Request.Registration,
            f.Request.Residences, f.Request.Ownership, f.Request.Work,
            ExpectedAuthorityVectorV1.Create(f.Session,
                [new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(Id(99)))]),
            f.Request.Correlation, f.Request.EffectObservedAt, f.Request.ObservedAt);
        Assert.IsType<GraphMediaWorkExecutionResultV1.Quarantined>(await coordinator.ExecuteAsync(stale, default));
        Assert.Equal(0, effects.ExecuteCalls); Assert.Equal(0, effects.QueryCalls);
        var read = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await f.Journal.ReadAsync(new(f.Session, 0, long.MaxValue, 256, 1_048_576)));
        Assert.Empty(read.Facts);

        using var source = new CancellationTokenSource(); source.Cancel();
        var canceled = await Assert.ThrowsAsync<OperationCanceledException>(async () => await coordinator.ExecuteAsync(f.Request, source.Token));
        Assert.Equal(source.Token, canceled.CancellationToken); Assert.Equal(0, effects.ExecuteCalls);
    }

    [Fact]
    public async Task Command_and_fact_lost_acknowledgements_reconcile_without_duplicate_execution()
    {
        foreach (var lostAppend in new[] { 1, 2 })
        {
            var f = Fixture(); f.Journal.LoseAcknowledgementOnAppend = lostAppend;
            var effects = new ScriptedEffects { ExecuteResult = new GraphMediaWorkEffectResultV1.Completed(Hash(92)) };
            var value = await new GraphMediaWorkExecutionCoordinatorV1(f.Journal, effects, f.Registry).ExecuteAsync(f.Request, default);
            Assert.IsType<GraphMediaWorkExecutionResultV1.Completed>(value);
            Assert.Equal(1, effects.ExecuteCalls); Assert.Equal(2, f.Journal.InnerCommittedCount);
            Assert.True(f.Journal.ReadCalls >= 3);
        }
    }

    [Fact]
    public async Task Thrown_and_postinvocation_canceled_effects_query_uncancelled_and_reconcile()
    {
        foreach (var cancel in new[] { false, true })
        {
            var f = Fixture(); using var source = new CancellationTokenSource();
            var effects = new ScriptedEffects
            {
                ThrowOnExecute = true,
                CancelOnExecute = cancel ? source : null,
                QueryResult = new GraphMediaWorkEffectQueryResultV1.Completed(Hash(93))
            };
            var value = await new GraphMediaWorkExecutionCoordinatorV1(f.Journal, effects, f.Registry)
                .ExecuteAsync(f.Request, source.Token);
            Assert.IsType<GraphMediaWorkExecutionResultV1.Completed>(value);
            Assert.Equal(1, effects.ExecuteCalls); Assert.Equal(1, effects.QueryCalls);
            Assert.False(effects.LastQueryToken.CanBeCanceled);
        }
    }

    [Fact]
    public async Task Bounded_command_CAS_exhaustion_never_invokes_the_effect()
    {
        var f = Fixture(); f.Journal.ForceSessionConflict = true; var effects = new ScriptedEffects();
        var result = await new GraphMediaWorkExecutionCoordinatorV1(f.Journal, effects, f.Registry).ExecuteAsync(f.Request, default);
        Assert.IsType<GraphMediaWorkExecutionResultV1.RetryRequired>(result);
        Assert.Equal(3, f.Journal.AppendCalls); Assert.Equal(0, effects.ExecuteCalls); Assert.Equal(0, effects.QueryCalls);
    }

    [Fact]
    public async Task Changed_duplicate_command_is_quarantined_without_effect()
    {
        var f = Fixture(); await AppendCommandOnly(f); var effects = new ScriptedEffects();
        var changed = new GraphMediaWorkExecutionRequestV1(f.Request.OperationId, f.Request.Registration,
            f.Request.Residences, f.Request.Ownership, f.Request.Work, f.Request.ExpectedAuthority,
            f.Request.Correlation, Stamp(99), f.Request.ObservedAt);
        var result = await new GraphMediaWorkExecutionCoordinatorV1(f.Journal, effects, f.Registry).ExecuteAsync(changed, default);
        Assert.IsType<GraphMediaWorkExecutionResultV1.Quarantined>(result);
        Assert.Equal(0, effects.ExecuteCalls); Assert.Equal(0, effects.QueryCalls);
    }

    [Fact]
    public async Task Command_only_query_terminal_arms_are_closed_without_reexecution()
    {
        foreach (var query in new GraphMediaWorkEffectQueryResultV1[]
        {
            new GraphMediaWorkEffectQueryResultV1.Completed(Hash(94)),
            new GraphMediaWorkEffectQueryResultV1.Rejected(new("work-effect-rejected")),
            new GraphMediaWorkEffectQueryResultV1.Unknown()
        })
        {
            var f = Fixture(); await AppendCommandOnly(f); var effects = new ScriptedEffects { QueryResult = query };
            var result = await new GraphMediaWorkExecutionCoordinatorV1(f.Journal, effects, f.Registry).ExecuteAsync(f.Request, default);
            Assert.Equal(query switch
            {
                GraphMediaWorkEffectQueryResultV1.Completed => typeof(GraphMediaWorkExecutionResultV1.Completed),
                GraphMediaWorkEffectQueryResultV1.Rejected => typeof(GraphMediaWorkExecutionResultV1.Rejected),
                _ => typeof(GraphMediaWorkExecutionResultV1.Unknown)
            }, result.GetType());
            Assert.Equal(1, effects.QueryCalls); Assert.Equal(0, effects.ExecuteCalls);
        }
    }

    [Fact]
    public async Task Fact_CAS_and_replay_bounds_fail_without_a_second_effect()
    {
        var f = Fixture(); f.Journal.ConflictStartingAtAppend = 2;
        var effects = new ScriptedEffects { ExecuteResult = new GraphMediaWorkEffectResultV1.Completed(Hash(95)) };
        var retry = await new GraphMediaWorkExecutionCoordinatorV1(f.Journal, effects, f.Registry).ExecuteAsync(f.Request, default);
        Assert.IsType<GraphMediaWorkExecutionResultV1.RetryRequired>(retry);
        Assert.Equal(4, f.Journal.AppendCalls); Assert.Equal(1, effects.ExecuteCalls);

        f = Fixture(); effects = new ScriptedEffects { ExecuteResult = new GraphMediaWorkEffectResultV1.Completed(Hash(96)) };
        var coordinator = new GraphMediaWorkExecutionCoordinatorV1(f.Journal, effects, f.Registry);
        Assert.IsType<GraphMediaWorkExecutionResultV1.Completed>(await coordinator.ExecuteAsync(f.Request, default));
        var bounded = new GraphMediaWorkExecutionRequestV1(f.Request.OperationId, f.Request.Registration,
            f.Request.Residences, f.Request.Ownership, f.Request.Work, f.Request.ExpectedAuthority,
            f.Request.Correlation, f.Request.EffectObservedAt, f.Request.ObservedAt, maximumSessionRecords: 1);
        Assert.IsType<GraphMediaWorkExecutionResultV1.Quarantined>(await coordinator.ExecuteAsync(bounded, default));
        Assert.Equal(1, effects.ExecuteCalls);
    }

    private static async ValueTask AppendCommandOnly(F f)
    {
        var registration = f.Work.Register(f.Registration, f.Residences, f.Ownership).Ledger;
        var row = registration.Work[f.Registration.WorkId];
        var body = new GraphMediaWorkExecutionCommandBodyV1(f.Request.OperationId, GraphMediaWorkAuthorityV1.FromRecord(row),
            f.Registration.Cleanups, null, f.Request.EffectObservedAt);
        var payload = GraphMediaWorkExecutionCodecsV1.EncodeOuter(new(f.Session, f.Request.ExpectedAuthority,
            GraphMediaWorkExecutionCodecsV1.EncodeCommandBody(body)));
        var registered = GraphMediaWorkExecutionPayloadRegistrationsV1.Command;
        var proposal = new ProposedAuthorityFactV1(GraphMediaWorkExecutionFactIdsV1.Command(f.Session, f.Request.OperationId),
            null, OwnerSliceId.S1, registered.Schema, payload,
            AuthorityPayloadHashV1.Compute(registered.SchemaToken, registered.Schema, payload), f.Request.Correlation, f.Request.ObservedAt);
        Assert.IsType<AppendAuthorityResultV1.Committed>(await f.Journal.AppendAsync(new(f.Session, 0, [], [proposal], 1_048_576)));
    }

    private static F Fixture()
    {
        var session = Session(); var graph = Graph(); var sourceId = Id(10); var destinationId = Id(11);
        Assert.True(GraphMediaBindingV1.TryCreate(0, 1_000, Id(12), 1, 48_000, 2, 2, Id(13), 1, 0,
            GraphMediaDiscontinuityKindV1.ResetBefore, 400, 100, null, out var media));
        var ownership = GraphMediaOwnershipLedgerV1.Create(new(session, graph, Id(14)), sourceId, media!);
        var source = ownership.Owners[sourceId]; var operation = Operation(15); var participant = ParticipantId.FromValue(Id(16));
        var authority = ExpectedAuthorityVectorV1.Create(session, [new AuthorityAxisValueV1.Graph(graph)]);
        var scope = new CapacityScopeV1(TenantId.FromValue(Id(17)), SessionId.FromValue(Id(18)), new CapacitySubjectV1.Participant(participant));
        var charges = new[] { Charge(1, 400, 19, scope), Charge(4, 200, 20, scope), Charge(5, 1_000, 21, scope) };
        var capacity = new CapacityRequestV1(operation, authority, charges, Stamp(30), CapacityPriorityV1.Normal);
        var preGrant = new GraphParticipantPreGrantPlanV2(participant, operation, Position(1), Position(2), graph,
            Hash(22), new("factory"), [1], Hash(23), [new("node")], [2], Hash(24), capacity);
        var grantId = CapacityGrantId.FromValue(Id(25));
        var topology = new GraphTopologyPlanV1(session, graph, grantId, [new(new("node"))], [], [new(1), new(4), new(5)]);
        var catalog = Assert.IsType<GraphRuntimeExecutableCatalogResultV1.Created>(
            GraphRuntimeExecutableFactoryCatalogV1.FromGeneratedApplicationManifest([new(new("node"), "tests:node@1", 1)]));
        var executable = Assert.IsType<GraphRuntimeExecutableCompileResultV1.Compiled>(
            GraphRuntimeExecutablePlanV1.Compile(topology, topology.Fingerprint, catalog, charges)).Plan;
        var evidence = new GraphParticipantBindingPlanEvidenceV2(preGrant, grantId, Position(30), Position(31),
            new CapacityGrantExpiryV1.NoExpiry(), [3], Hash(26), topology, executable, topology.Fingerprint, executable.Fingerprint);
        var reservation = new GraphParticipantReservationV1(participant, new("factory"), [new("node")]);
        var reservationApplied = new GraphParticipantReservationFoldV2.AppliedReservation(
            Envelope(Position(1), [1]), Envelope(Position(2), [2]), reservation);
        var proof = new CapacityGrantBindingProofV1(grantId, evidence.GrantedAt, evidence.CurrentFact, 3, evidence.CoverageHashV2);
        var binding = new GraphParticipantBindingV1(participant, new("factory"), [new("node")]);
        var bindingBody = new GraphParticipantBindingCommandBodyV1(operation, Position(2), null, graph,
            session.RuntimeGenerationId, preGrant.ParticipantPlanFingerprint, evidence.TopologyFingerprint,
            evidence.ExecutableFingerprint, proof, Stamp(40));
        var bindingPayload = GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(session,
            authority, GraphParticipantBindingCodecsV1.Encode(bindingBody)));
        var command = Envelope(Position(3), bindingPayload, GraphParticipantBindingPayloadRegistrationsV1.BindingCommand);
        var fact = Envelope(Position(4), [9], GraphParticipantBindingPayloadRegistrationsV1.BindingFact);
        var bound = new GraphParticipantBindingResultV2.Bound(command.Position, fact.Position, fact.PayloadMemory, binding, proof);
        var fold = new GraphParticipantBindingFoldQueryResultV2.Bound(reservationApplied, command, fact, binding, proof);
        var residenceRequest = new GraphMediaControlledResidenceRequestV1(operation, Hash(27), Id(28), sourceId,
            destinationId, new("node"), GraphMediaRepresentationArmV1.ResidentBytes, bound, fold, evidence);
        var assignment = new GraphMediaCapacityAssignmentV1(charges[0], residenceRequest.Arm);
        residenceRequest = residenceRequest with { RequestHash = GraphMediaResidenceLedgerV1.ResidenceHash(residenceRequest, source, assignment) };
        var residences = GraphMediaResidenceLedgerV1.Create(session, graph).PrepareControlled(residenceRequest, ownership).Ledger;
        ownership = ownership.CopyOwners(session, graph, sourceId, [destinationId]).Ledger;
        residences = residences.MakeVisible(operation, residenceRequest.RequestHash, ownership).Ledger;
        var workId = Id(70); var cleanups = new GraphMediaCleanupRegistrationV1[] { new(Id(71), Hash(71)), new(Id(72), Hash(72)) };
        var workRequest = new GraphMediaWorkRegistrationV1(workId, Hash(1), residenceRequest.ResidenceId, cleanups);
        workRequest = workRequest with { RequestHash = GraphMediaWorkLedgerV1.RegistrationHash(workRequest, residences.Residences[residenceRequest.ResidenceId]) };
        var work = GraphMediaWorkLedgerV1.Create(session, graph);
        var registry = new AuthorityPayloadAdmissionRegistryV1([
            GraphMediaWorkExecutionPayloadRegistrationsV1.Command, GraphMediaWorkExecutionPayloadRegistrationsV1.Fact]);
        var journal = new ScriptedJournal(new InMemoryAuthorityJournalV1(registry, () => new UtcInstant(2), new(4, 64, 16_000_000)));
        var request = new GraphMediaWorkExecutionRequestV1(Operation(80), workRequest, residences, ownership, work,
            authority, new CorrelationEnvelopeV1(TenantId.FromValue(Id(81)), sessionId: SessionId.FromValue(Id(82)), operationId: Operation(80)),
            Stamp(83), new UtcInstant(1));
        return new(session, workRequest, residences, ownership, work, registry, journal, request);
    }

    private sealed class ScriptedEffects : IGraphMediaWorkExecutionPortV1
    {
        internal int ExecuteCalls, QueryCalls;
        internal bool ThrowOnExecute;
        internal CancellationTokenSource? CancelOnExecute;
        internal CancellationToken LastQueryToken;
        internal GraphMediaWorkEffectResultV1 ExecuteResult = new GraphMediaWorkEffectResultV1.Unknown();
        internal GraphMediaWorkEffectQueryResultV1 QueryResult = new GraphMediaWorkEffectQueryResultV1.Unknown();
        public ValueTask<GraphMediaWorkEffectResultV1> ExecuteAsync(GraphMediaWorkEffectRequestV1 request, CancellationToken cancellationToken)
        {
            ExecuteCalls++; CancelOnExecute?.Cancel();
            if (ThrowOnExecute) throw new OperationCanceledException(CancelOnExecute?.Token ?? cancellationToken);
            return ValueTask.FromResult(ExecuteResult);
        }
        public ValueTask<GraphMediaWorkEffectQueryResultV1> QueryAsync(GraphMediaWorkEffectRequestV1 request, CancellationToken cancellationToken)
        { QueryCalls++; LastQueryToken = cancellationToken; return ValueTask.FromResult(QueryResult); }
    }

    private sealed class ScriptedJournal(InMemoryAuthorityJournalV1 inner) : IAuthorityJournalV1
    {
        internal int AppendCalls, ReadCalls, InnerCommittedCount;
        internal int LoseAcknowledgementOnAppend;
        internal bool ForceSessionConflict;
        internal int ConflictStartingAtAppend;
        public async ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request,
            CancellationToken cancellationToken = default)
        {
            AppendCalls++;
            if (ForceSessionConflict || ConflictStartingAtAppend > 0 && AppendCalls >= ConflictStartingAtAppend)
                return new AppendAuthorityResultV1.SessionConflict(request.ExpectedSessionHead, request.ExpectedSessionHead);
            var result = await inner.AppendAsync(request, cancellationToken);
            if (result is AppendAuthorityResultV1.Committed) InnerCommittedCount++;
            return AppendCalls == LoseAcknowledgementOnAppend
                ? new AppendAuthorityResultV1.OutcomeUnknown(request.Facts[0].Correlation.OperationId!.Value)
                : result;
        }
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,
            CancellationToken cancellationToken = default)
        { ReadCalls++; return inner.ReadAsync(request, cancellationToken); }
    }

    private sealed record F(SessionAuthorityStampV1 Session, GraphMediaWorkRegistrationV1 Registration,
        GraphMediaResidenceLedgerV1 Residences, GraphMediaOwnershipLedgerV1 Ownership,
        GraphMediaWorkLedgerV1 Work, AuthorityPayloadAdmissionRegistryV1 Registry,
        ScriptedJournal Journal, GraphMediaWorkExecutionRequestV1 Request);
    private static SessionAuthorityStampV1 Session() => new(RuntimeGenerationId.FromValue(Id(1)), LiveSessionId.FromValue(Id(2)));
    private static GraphGenerationId Graph() => GraphGenerationId.FromValue(Id(3));
    private static JournalPositionV1 Position(long sequence) => new(Session(), sequence);
    private static MonotonicStampV1 Stamp(byte value) => new(ClockDomainId.FromValue(Id(4)), BootId.FromValue(Id(5)), value);
    private static StableId128 Id(byte value) { var bytes = new byte[16]; bytes[^1] = value; return StableId128.FromBytes(bytes); }
    private static OperationId Operation(byte value) => OperationId.FromValue(Id(value));
    private static Hash256 Hash(byte value) { var bytes = new byte[32]; bytes[^1] = value; return Hash256.FromBytes(bytes); }
    private static CapacityChargeV1 Charge(ushort dimension, long amount, byte purpose, CapacityScopeV1 scope) =>
        new(new(dimension), scope, amount, CapacityPurposeId.FromValue(Id(purpose)), new CapacityChargeWindowV1.NoWindow());
    private static AuthorityFactEnvelopeV1 Envelope(JournalPositionV1 position, byte[] payload,
        AuthorityPayloadRegistrationV1? registration = null)
    {
        registration ??= GraphParticipantReservationPayloadRegistrationsV2.ReservationCommand;
        var correlation = new CorrelationEnvelopeV1(TenantId.FromValue(Id(60)), sessionId: SessionId.FromValue(Id(61)), operationId: Operation(15));
        return new(JournalFactId.Create(), position, null, OwnerSliceId.S1, registration.Schema, payload,
            AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload), correlation,
            new UtcInstant(1), new UtcInstant(1), new IntegrityEnvelopeV1(1, 1, Hash(62), []));
    }
}
