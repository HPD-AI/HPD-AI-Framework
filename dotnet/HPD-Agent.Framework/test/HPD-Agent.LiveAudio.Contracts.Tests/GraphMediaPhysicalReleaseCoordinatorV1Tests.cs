using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.Graph.Runtime;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphMediaPhysicalReleaseCoordinatorV1Tests
{
    [Fact]
    public async Task Command_is_durable_before_effect_and_retry_does_not_repeat_release()
    {
        var fixture = CreateFixture();
        var registry = new AuthorityPayloadAdmissionRegistryV1([
            GraphMediaPhysicalReleasePayloadRegistrationsV1.Command,
            GraphMediaPhysicalReleasePayloadRegistrationsV1.Fact]);
        var journal = new InMemoryAuthorityJournalV1(registry, () => new UtcInstant(100),
            new AuthorityJournalCapacityV1(1, 64, 8_000_000));
        var port = new InspectingReleasePort(journal, fixture.Session, Hash(90));
        var coordinator = new GraphMediaPhysicalReleaseCoordinatorV1(journal, port, registry);

        var first = await coordinator.ReleaseAsync(fixture.Request, CancellationToken.None);
        Assert.True(first is GraphMediaPhysicalReleaseResultV1.Released,
            (first as GraphMediaPhysicalReleaseResultV1.Quarantined)?.SafeCode.ToString());
        var released = (GraphMediaPhysicalReleaseResultV1.Released)first;
        Assert.Equal(Hash(90), released.EvidenceHash);
        Assert.Equal(1, port.ReleaseCalls);
        Assert.Equal(0, port.QueryCalls);

        var retry = Assert.IsType<GraphMediaPhysicalReleaseResultV1.Released>(
            await coordinator.ReleaseAsync(fixture.Request, CancellationToken.None));
        Assert.Equal(released.Command.Position, retry.Command.Position);
        Assert.Equal(released.Fact.Position, retry.Fact.Position);
        Assert.Equal(1, port.ReleaseCalls);
        Assert.Equal(0, port.QueryCalls);

        var history = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await journal.ReadAsync(
            new(fixture.Session, 0, long.MaxValue, 256, 1_048_576)));
        Assert.Equal(2, history.Facts.Count);
    }

    [Fact]
    public async Task Encumbered_work_fails_before_journal_or_effect()
    {
        var fixture = CreateFixture(false);
        var registry = new AuthorityPayloadAdmissionRegistryV1([
            GraphMediaPhysicalReleasePayloadRegistrationsV1.Command,
            GraphMediaPhysicalReleasePayloadRegistrationsV1.Fact]);
        var journal = new InMemoryAuthorityJournalV1(registry, () => new UtcInstant(100),
            new AuthorityJournalCapacityV1(1, 64, 8_000_000));
        var port = new InspectingReleasePort(journal, fixture.Session, Hash(90));
        var coordinator = new GraphMediaPhysicalReleaseCoordinatorV1(journal, port, registry);

        var result = Assert.IsType<GraphMediaPhysicalReleaseResultV1.Quarantined>(
            await coordinator.ReleaseAsync(fixture.Request, CancellationToken.None));
        Assert.Equal("work-encumbered", result.SafeCode.ToString());
        Assert.Equal(0, port.ReleaseCalls);
        Assert.Equal(0, port.QueryCalls);
        var history = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await journal.ReadAsync(
            new(fixture.Session, 0, long.MaxValue, 256, 1_048_576)));
        Assert.Empty(history.Facts);
    }

    [Fact]
    public async Task Post_invocation_cancellation_queries_uncancelled_and_records_released()
    {
        var fixture = CreateFixture();
        var registry = Registry();
        var journal = new InMemoryAuthorityJournalV1(registry, () => new UtcInstant(100),
            new AuthorityJournalCapacityV1(1, 64, 8_000_000));
        var port = new InspectingReleasePort(journal, fixture.Session, Hash(90)) { ThrowAfterInvocation = true };
        var result = Assert.IsType<GraphMediaPhysicalReleaseResultV1.Released>(await
            new GraphMediaPhysicalReleaseCoordinatorV1(journal, port, registry)
                .ReleaseAsync(fixture.Request, CancellationToken.None));
        Assert.Equal(Hash(90), result.EvidenceHash);
        Assert.Equal(1, port.ReleaseCalls);
        Assert.Equal(1, port.QueryCalls);
        Assert.True(port.LastQueryWasUncancelled);
    }

    [Fact]
    public async Task Cancellation_after_effect_result_cannot_prevent_fact_admission()
    {
        var fixture = CreateFixture(); var registry = Registry();
        var journal = new InMemoryAuthorityJournalV1(registry, () => new UtcInstant(100),
            new AuthorityJournalCapacityV1(1, 64, 8_000_000));
        using var cancellation = new CancellationTokenSource();
        var port = new InspectingReleasePort(journal, fixture.Session, Hash(90))
            { CancelSourceAfterRelease = cancellation };
        Assert.IsType<GraphMediaPhysicalReleaseResultV1.Released>(await
            new GraphMediaPhysicalReleaseCoordinatorV1(journal, port, registry)
                .ReleaseAsync(fixture.Request, cancellation.Token));
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, port.ReleaseCalls);
        var history = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await journal.ReadAsync(
            new(fixture.Session, 0, long.MaxValue, 256, 1_048_576)));
        Assert.Equal(2, history.Facts.Count);
    }

    [Fact]
    public async Task Unknown_is_terminal_and_exact_retry_never_releases_again()
    {
        var fixture = CreateFixture();
        var registry = Registry();
        var journal = new InMemoryAuthorityJournalV1(registry, () => new UtcInstant(100),
            new AuthorityJournalCapacityV1(1, 64, 8_000_000));
        var port = new InspectingReleasePort(journal, fixture.Session, Hash(90)) { ReturnUnknown = true };
        var coordinator = new GraphMediaPhysicalReleaseCoordinatorV1(journal, port, registry);
        Assert.IsType<GraphMediaPhysicalReleaseResultV1.Unknown>(
            await coordinator.ReleaseAsync(fixture.Request, CancellationToken.None));
        Assert.IsType<GraphMediaPhysicalReleaseResultV1.Unknown>(
            await coordinator.ReleaseAsync(fixture.Request, CancellationToken.None));
        Assert.Equal(1, port.ReleaseCalls);
        Assert.Equal(0, port.QueryCalls);
    }

    [Fact]
    public async Task Cancellation_after_command_commit_leaves_command_only_and_restart_queries_without_release()
    {
        var fixture = CreateFixture(); var registry = Registry();
        var inner = new InMemoryAuthorityJournalV1(registry, () => new UtcInstant(100),
            new AuthorityJournalCapacityV1(1, 64, 8_000_000));
        using var cancellation = new CancellationTokenSource();
        var journal = new ScriptedJournal(inner) { CancelSource = cancellation, CancelAfterCommittedAppend = 1 };
        var firstPort = new InspectingReleasePort(journal, fixture.Session, Hash(90));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await
            new GraphMediaPhysicalReleaseCoordinatorV1(journal, firstPort, registry)
                .ReleaseAsync(fixture.Request, cancellation.Token));
        Assert.Equal(0, firstPort.ReleaseCalls);
        Assert.Equal(0, firstPort.QueryCalls);

        var restartPort = new InspectingReleasePort(journal, fixture.Session, Hash(90));
        var restarted = Assert.IsType<GraphMediaPhysicalReleaseResultV1.Released>(await
            new GraphMediaPhysicalReleaseCoordinatorV1(journal, restartPort, registry)
                .ReleaseAsync(fixture.Request, CancellationToken.None));
        Assert.Equal(Hash(90), restarted.EvidenceHash);
        Assert.Equal(0, restartPort.ReleaseCalls);
        Assert.Equal(1, restartPort.QueryCalls);
        Assert.True(restartPort.LastQueryWasUncancelled);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Lost_acknowledgement_is_reconciled_without_repeating_effect(int appendCall)
    {
        var fixture = CreateFixture(); var registry = Registry();
        var inner = new InMemoryAuthorityJournalV1(registry, () => new UtcInstant(100),
            new AuthorityJournalCapacityV1(1, 64, 8_000_000));
        var journal = new ScriptedJournal(inner) { OutcomeUnknownAfterCommittedAppend = appendCall };
        var port = new InspectingReleasePort(journal, fixture.Session, Hash(90));
        Assert.IsType<GraphMediaPhysicalReleaseResultV1.Released>(await
            new GraphMediaPhysicalReleaseCoordinatorV1(journal, port, registry)
                .ReleaseAsync(fixture.Request, CancellationToken.None));
        Assert.Equal(1, port.ReleaseCalls);
        Assert.InRange(journal.AppendCalls, 2, 3);
        var history = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await inner.ReadAsync(
            new(fixture.Session, 0, long.MaxValue, 256, 1_048_576)));
        Assert.Equal(2, history.Facts.Count);
    }

    [Fact]
    public async Task Durable_rejection_allows_one_predecessor_fenced_successor()
    {
        var fixture = CreateFixture(); var registry = Registry();
        var journal = new InMemoryAuthorityJournalV1(registry, () => new UtcInstant(100),
            new AuthorityJournalCapacityV1(1, 64, 8_000_000));
        var rejectedPort = new InspectingReleasePort(journal, fixture.Session, Hash(90)) { Reject = true };
        var rejected = Assert.IsType<GraphMediaPhysicalReleaseResultV1.Rejected>(await
            new GraphMediaPhysicalReleaseCoordinatorV1(journal, rejectedPort, registry)
                .ReleaseAsync(fixture.Request, CancellationToken.None));
        Assert.Equal("release-authority-stale", rejected.SafeCode.ToString());

        var nextOperation = Operation(91);
        var nextCorrelation = new CorrelationEnvelopeV1(TenantId.FromValue(Id(81)),
            sessionId: SessionId.FromValue(Id(82)), operationId: nextOperation);
        var nextRequest = new GraphMediaPhysicalReleaseRequestV1(nextOperation, fixture.Request.ResidenceId,
            fixture.Request.Residences, fixture.Request.Ownership, fixture.Request.Work,
            fixture.Request.FanoutOperationId, fixture.Request.ExpectedAuthority, nextCorrelation,
            Stamp(92), new UtcInstant(93));
        var successPort = new InspectingReleasePort(journal, fixture.Session, Hash(94));
        var released = Assert.IsType<GraphMediaPhysicalReleaseResultV1.Released>(await
            new GraphMediaPhysicalReleaseCoordinatorV1(journal, successPort, registry)
                .ReleaseAsync(nextRequest, CancellationToken.None));
        Assert.Equal(Hash(94), released.EvidenceHash);
        Assert.Equal(1, successPort.ReleaseCalls);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("authority")]
    [InlineData("fanout")]
    public async Task Retained_authority_mutations_fail_before_command_and_effect(string mutation)
    {
        var fixture = CreateFixture(ownerTerminal: mutation != "owner");
        var request = fixture.Request;
        if (mutation == "authority")
        {
            var changedGraph = GraphGenerationId.FromValue(Id(99));
            request = new(request.OperationId, request.ResidenceId, request.Residences, request.Ownership,
                request.Work, request.FanoutOperationId,
                ExpectedAuthorityVectorV1.Create(fixture.Session, [new AuthorityAxisValueV1.Graph(changedGraph)]),
                request.Correlation, request.EffectObservedAt, request.ObservedAt);
        }
        if (mutation == "fanout")
            request = new(request.OperationId, request.ResidenceId, request.Residences, request.Ownership,
                request.Work, Operation(98), request.ExpectedAuthority, request.Correlation,
                request.EffectObservedAt, request.ObservedAt);
        var registry = Registry();
        var journal = new InMemoryAuthorityJournalV1(registry, () => new UtcInstant(100),
            new AuthorityJournalCapacityV1(1, 64, 8_000_000));
        var port = new InspectingReleasePort(journal, fixture.Session, Hash(90));
        Assert.IsType<GraphMediaPhysicalReleaseResultV1.Quarantined>(await
            new GraphMediaPhysicalReleaseCoordinatorV1(journal, port, registry)
                .ReleaseAsync(request, CancellationToken.None));
        Assert.Equal(0, port.ReleaseCalls);
        var history = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await journal.ReadAsync(
            new(fixture.Session, 0, long.MaxValue, 256, 1_048_576)));
        Assert.Empty(history.Facts);
    }

    [Fact]
    public async Task Cross_instance_command_race_never_appends_a_follower_or_repeats_effect()
    {
        var fixture = CreateFixture(); var registry = Registry();
        var inner = new InMemoryAuthorityJournalV1(registry, () => new UtcInstant(100),
            new AuthorityJournalCapacityV1(1, 64, 8_000_000));
        var journal = new ScriptedJournal(inner) { BarrierOnFirstTwoAppends = new CountdownEvent(2) };
        var secondOperation = Operation(95);
        var secondRequest = new GraphMediaPhysicalReleaseRequestV1(secondOperation, fixture.Request.ResidenceId,
            fixture.Request.Residences, fixture.Request.Ownership, fixture.Request.Work, null,
            fixture.Request.ExpectedAuthority, new(TenantId.FromValue(Id(81)),
                sessionId: SessionId.FromValue(Id(82)), operationId: secondOperation), Stamp(96), new UtcInstant(97));
        var firstPort = new InspectingReleasePort(journal, fixture.Session, Hash(90));
        var secondPort = new InspectingReleasePort(journal, fixture.Session, Hash(91));
        var firstTask = Task.Run(async () => await new GraphMediaPhysicalReleaseCoordinatorV1(journal, firstPort, registry)
            .ReleaseAsync(fixture.Request, CancellationToken.None));
        var secondTask = Task.Run(async () => await new GraphMediaPhysicalReleaseCoordinatorV1(journal, secondPort, registry)
            .ReleaseAsync(secondRequest, CancellationToken.None));
        var results = await Task.WhenAll(firstTask, secondTask);
        Assert.Single(results.OfType<GraphMediaPhysicalReleaseResultV1.Released>());
        Assert.Single(results.OfType<GraphMediaPhysicalReleaseResultV1.RetryRequired>());
        Assert.Equal(1, firstPort.ReleaseCalls + secondPort.ReleaseCalls);
        var history = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await inner.ReadAsync(
            new(fixture.Session, 0, long.MaxValue, 256, 1_048_576)));
        Assert.Equal(2, history.Facts.Count);
    }

    [Fact]
    public async Task Restart_query_not_found_closes_unknown_without_reissuing_release()
    {
        var fixture = CreateFixture(); var registry = Registry();
        var inner = new InMemoryAuthorityJournalV1(registry, () => new UtcInstant(100),
            new AuthorityJournalCapacityV1(1, 64, 8_000_000));
        using var cancellation = new CancellationTokenSource();
        var journal = new ScriptedJournal(inner) { CancelSource = cancellation, CancelAfterCommittedAppend = 1 };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await
            new GraphMediaPhysicalReleaseCoordinatorV1(journal,
                new InspectingReleasePort(journal, fixture.Session, Hash(90)), registry)
                .ReleaseAsync(fixture.Request, cancellation.Token));
        var port = new InspectingReleasePort(journal, fixture.Session, Hash(90)) { QueryNotFound = true };
        var coordinator = new GraphMediaPhysicalReleaseCoordinatorV1(journal, port, registry);
        Assert.IsType<GraphMediaPhysicalReleaseResultV1.Unknown>(
            await coordinator.ReleaseAsync(fixture.Request, CancellationToken.None));
        Assert.IsType<GraphMediaPhysicalReleaseResultV1.Unknown>(
            await coordinator.ReleaseAsync(fixture.Request, CancellationToken.None));
        Assert.Equal(0, port.ReleaseCalls);
        Assert.Equal(1, port.QueryCalls);
    }

    private static ReleaseFixture CreateFixture(bool finishCleanup = true, bool ownerTerminal = true)
    {
        var session = Session(); var graph = Graph(); var sourceId = Id(10); var destinationId = Id(11);
        Assert.True(GraphMediaBindingV1.TryCreate(0, 1_000, Id(12), 1, 48_000, 2, 2, Id(13), 1, 0,
            GraphMediaDiscontinuityKindV1.ResetBefore, 400, 100, null, out var media));
        var ownership = GraphMediaOwnershipLedgerV1.Create(new(session, graph, Id(14)), sourceId, media!);
        ownership = Assert.IsType<GraphMediaOwnershipBatchCopyTransitionV1>(ownership.CopyOwners(session, graph,
            sourceId, [destinationId])).Ledger;
        var operation = Operation(15); var participant = ParticipantId.FromValue(Id(16));
        var authority = ExpectedAuthorityVectorV1.Create(session, [new AuthorityAxisValueV1.Graph(graph)]);
        var scope = new CapacityScopeV1(TenantId.FromValue(Id(17)), SessionId.FromValue(Id(18)),
            new CapacitySubjectV1.Participant(participant));
        var charges = new[] { Charge(1, 400, 19, scope), Charge(4, 200, 20, scope), Charge(5, 1_000, 21, scope) };
        var capacityRequest = new CapacityRequestV1(operation, authority, charges, Stamp(30), CapacityPriorityV1.Normal);
        var reservationCommand = Position(1); var reservationFact = Position(2);
        var preGrant = new GraphParticipantPreGrantPlanV2(participant, operation, reservationCommand, reservationFact,
            graph, Hash(22), new("factory"), [1], Hash(23), [new("node")], [2], Hash(24), capacityRequest);
        var grantId = CapacityGrantId.FromValue(Id(25));
        var topology = new GraphTopologyPlanV1(session, graph, grantId, [new(new("node"))], [], [new(1), new(4), new(5)]);
        var catalog = Assert.IsType<GraphRuntimeExecutableCatalogResultV1.Created>(
            GraphRuntimeExecutableFactoryCatalogV1.FromGeneratedApplicationManifest([new(new("node"), "tests:node@1", 1)]));
        var executable = Assert.IsType<GraphRuntimeExecutableCompileResultV1.Compiled>(
            GraphRuntimeExecutablePlanV1.Compile(topology, topology.Fingerprint, catalog, charges)).Plan;
        var evidence = new GraphParticipantBindingPlanEvidenceV2(preGrant, grantId, Position(30), Position(31),
            new CapacityGrantExpiryV1.NoExpiry(), [3], Hash(26), topology, executable,
            topology.Fingerprint, executable.Fingerprint);
        var reservation = new GraphParticipantReservationV1(participant, new("factory"), [new("node")]);
        var reservationApplied = new GraphParticipantReservationFoldV2.AppliedReservation(
            Envelope(reservationCommand, [1]), Envelope(reservationFact, [2]), reservation);
        var proof = new CapacityGrantBindingProofV1(grantId, evidence.GrantedAt, evidence.CurrentFact, 3, evidence.CoverageHashV2);
        var binding = new GraphParticipantBindingV1(participant, new("factory"), [new("node")]);
        var commandBody = new GraphParticipantBindingCommandBodyV1(operation, reservationFact, null, graph,
            session.RuntimeGenerationId, preGrant.ParticipantPlanFingerprint, evidence.TopologyFingerprint,
            evidence.ExecutableFingerprint, proof, Stamp(40));
        var commandPayload = GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(session,
            authority, GraphParticipantBindingCodecsV1.Encode(commandBody)));
        var bindingCommand = Envelope(Position(3), commandPayload, GraphParticipantBindingPayloadRegistrationsV1.BindingCommand);
        var bindingFact = Envelope(Position(4), [9], GraphParticipantBindingPayloadRegistrationsV1.BindingFact);
        var bound = new GraphParticipantBindingResultV2.Bound(bindingCommand.Position, bindingFact.Position,
            bindingFact.PayloadMemory, binding, proof);
        var foldBound = new GraphParticipantBindingFoldQueryResultV2.Bound(reservationApplied, bindingCommand,
            bindingFact, binding, proof);
        var residenceRequest = new GraphMediaControlledResidenceRequestV1(operation, Hash(27), Id(28), sourceId,
            destinationId, new("node"), GraphMediaRepresentationArmV1.ResidentBytes, bound, foldBound, evidence);
        var assignment = new GraphMediaCapacityAssignmentV1(charges[0], residenceRequest.Arm);
        residenceRequest = residenceRequest with { RequestHash = GraphMediaResidenceLedgerV1.ResidenceHash(
            residenceRequest, ownership.Owners[sourceId], assignment) };
        var residence = GraphMediaResidenceLedgerV1.Create(session, graph).PrepareControlled(residenceRequest, ownership).Ledger;
        residence = residence.MakeVisible(operation, residenceRequest.RequestHash, ownership).Ledger;

        var workId = Id(70); var cleanupId = Id(71); var cleanupHash = Hash(73);
        var registration = new GraphMediaWorkRegistrationV1(workId, Hash(72), residenceRequest.ResidenceId,
            [new(cleanupId, cleanupHash)]);
        var workHash = GraphMediaWorkLedgerV1.RegistrationHash(registration,
            residence.Residences[residenceRequest.ResidenceId]);
        registration = registration with { RequestHash = workHash };
        var work = GraphMediaWorkLedgerV1.Create(session, graph);
        work = work.Register(registration, residence, ownership).Ledger;
        work = work.StartWork(workId, workHash).Ledger;
        work = work.FinishWork(workId, workHash, Hash(74)).Ledger;
        work = work.ClaimCleanup(workId, cleanupId, cleanupHash).Ledger;
        if (finishCleanup) work = work.FinishCleanup(cleanupId, cleanupHash, Hash(75)).Ledger;

        var dispose = Operation(76);
        var disposeHash = GraphMediaOwnershipCodecV1.OwnerTransition(dispose, GraphMediaOwnerActionV1.Dispose,
            destinationId, null, ownership.Owners[destinationId].Key, ownership.Owners[destinationId].Media, 1, out _);
        if (ownerTerminal)
            ownership = ownership.Transition(session, graph, dispose, GraphMediaOwnerActionV1.Dispose,
                destinationId, null, 1, disposeHash).Ledger;
        var releaseOperation = Operation(80);
        var correlation = new CorrelationEnvelopeV1(TenantId.FromValue(Id(81)),
            sessionId: SessionId.FromValue(Id(82)), operationId: releaseOperation);
        var request = new GraphMediaPhysicalReleaseRequestV1(releaseOperation, residenceRequest.ResidenceId,
            residence, ownership, work, null, authority, correlation, Stamp(83), new UtcInstant(84));
        return new(session, request);
    }

    private sealed class InspectingReleasePort : IGraphMediaPhysicalReleasePortV1
    {
        private readonly IAuthorityJournalV1 _journal; private readonly SessionAuthorityStampV1 _session;
        private readonly Hash256 _evidence;
        internal InspectingReleasePort(IAuthorityJournalV1 journal, SessionAuthorityStampV1 session, Hash256 evidence)
        { _journal = journal; _session = session; _evidence = evidence; }
        internal int ReleaseCalls { get; private set; }
        internal int QueryCalls { get; private set; }
        internal bool ThrowAfterInvocation { get; init; }
        internal bool ReturnUnknown { get; init; }
        internal bool Reject { get; init; }
        internal bool QueryNotFound { get; init; }
        internal CancellationTokenSource? CancelSourceAfterRelease { get; init; }
        internal bool LastQueryWasUncancelled { get; private set; }
        public async ValueTask<GraphMediaPhysicalReleaseEffectResultV1> ReleaseAsync(
            GraphMediaPhysicalReleaseEffectRequestV1 request, CancellationToken cancellationToken)
        {
            ReleaseCalls++;
            var history = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await _journal.ReadAsync(
                new(_session, 0, long.MaxValue, 256, 1_048_576), cancellationToken));
            Assert.Single(history.Facts);
            Assert.Equal(GraphMediaPhysicalReleasePayloadRegistrationsV1.Command.Schema, history.Facts[0].PayloadSchema);
            if (ThrowAfterInvocation) throw new OperationCanceledException(cancellationToken);
            if (ReturnUnknown) return new GraphMediaPhysicalReleaseEffectResultV1.Unknown();
            if (Reject) return new GraphMediaPhysicalReleaseEffectResultV1.Rejected(new("release-authority-stale"));
            CancelSourceAfterRelease?.Cancel();
            return new GraphMediaPhysicalReleaseEffectResultV1.Released(_evidence);
        }
        public ValueTask<GraphMediaPhysicalReleaseEffectQueryResultV1> QueryAsync(
            GraphMediaPhysicalReleaseEffectRequestV1 request, CancellationToken cancellationToken)
        {
            QueryCalls++; LastQueryWasUncancelled = !cancellationToken.CanBeCanceled;
            return ValueTask.FromResult<GraphMediaPhysicalReleaseEffectQueryResultV1>(QueryNotFound
                ? new GraphMediaPhysicalReleaseEffectQueryResultV1.NotFound()
                : new GraphMediaPhysicalReleaseEffectQueryResultV1.Released(_evidence));
        }
    }

    private sealed class ScriptedJournal : IAuthorityJournalV1
    {
        private readonly IAuthorityJournalV1 _inner;
        internal ScriptedJournal(IAuthorityJournalV1 inner) => _inner = inner;
        internal int AppendCalls { get; private set; }
        internal int CancelAfterCommittedAppend { get; init; }
        internal int OutcomeUnknownAfterCommittedAppend { get; init; }
        internal CancellationTokenSource? CancelSource { get; init; }
        internal CountdownEvent? BarrierOnFirstTwoAppends { get; init; }
        public async ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request,
            CancellationToken cancellationToken = default)
        {
            AppendCalls++;
            if (AppendCalls <= 2 && BarrierOnFirstTwoAppends is { } barrier)
            { barrier.Signal(); Assert.True(barrier.Wait(TimeSpan.FromSeconds(10))); }
            var result = await _inner.AppendAsync(request, cancellationToken);
            if (result is AppendAuthorityResultV1.Committed && AppendCalls == CancelAfterCommittedAppend)
                CancelSource!.Cancel();
            if (result is AppendAuthorityResultV1.Committed && AppendCalls == OutcomeUnknownAfterCommittedAppend)
                return new AppendAuthorityResultV1.OutcomeUnknown(request.Facts[0].Correlation.OperationId!.Value);
            return result;
        }
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,
            CancellationToken cancellationToken = default) => _inner.ReadAsync(request, cancellationToken);
    }

    private sealed record ReleaseFixture(SessionAuthorityStampV1 Session, GraphMediaPhysicalReleaseRequestV1 Request);
    private static AuthorityPayloadAdmissionRegistryV1 Registry() => new([
        GraphMediaPhysicalReleasePayloadRegistrationsV1.Command,
        GraphMediaPhysicalReleasePayloadRegistrationsV1.Fact]);
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
        var correlation = new CorrelationEnvelopeV1(TenantId.FromValue(Id(60)),
            sessionId: SessionId.FromValue(Id(61)), operationId: Operation(15));
        return new(JournalFactId.Create(), position, null, OwnerSliceId.S1, registration.Schema, payload,
            AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload), correlation,
            new UtcInstant(1), new UtcInstant(1), new IntegrityEnvelopeV1(1, 1, Hash(62), []));
    }
}
