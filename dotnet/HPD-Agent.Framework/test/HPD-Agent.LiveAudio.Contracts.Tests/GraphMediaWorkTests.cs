using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.Graph.Runtime;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphMediaWorkTests
{
    [Fact]
    public void Registration_requires_exact_visible_residence_and_owner()
    {
        var fixture = CreateFixture();
        var registered = fixture.Work.Register(fixture.Request, fixture.Residences, fixture.Ownership);
        Assert.Equal(GraphMediaWorkResult.Registered, registered.Result);
        Assert.NotSame(fixture.Work, registered.Ledger);

        var hidden = CreateControlledFixture(makeVisible: false);
        var request = fixture.Request with { ResidenceId = hidden.Request.ResidenceId };
        AssertUnchanged(GraphMediaWorkResult.ResidenceNotVisible, fixture.Work,
            fixture.Work.Register(request, hidden.Residences, hidden.Ownership));

        AssertUnchanged(GraphMediaWorkResult.ResidenceNotFound, fixture.Work,
            fixture.Work.Register(fixture.Request with { ResidenceId = Id(249) }, fixture.Residences, fixture.Ownership));
        var residence = fixture.Residences.Residences[fixture.Request.ResidenceId];
        var wrongOwner = GraphMediaOwnershipLedger.Create(residence.OwnerKey, Id(250), residence.Media);
        AssertUnchanged(GraphMediaWorkResult.OwnerMismatch, fixture.Work,
            fixture.Work.Register(fixture.Request, fixture.Residences, wrongOwner));

        var stale = GraphMediaWorkLedger.Create(
            new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(90)), LiveSessionId.FromValue(Id(91))), Graph());
        AssertUnchanged(GraphMediaWorkResult.StaleGeneration, stale,
            stale.Register(fixture.Request, fixture.Residences, fixture.Ownership));
    }

    [Fact]
    public void Registration_is_atomic_and_retries_are_closed()
    {
        var fixture = CreateFixture();
        var first = fixture.Work.Register(fixture.Request, fixture.Residences, fixture.Ownership);
        Assert.Single(first.Ledger.Work);
        Assert.Collection(first.Ledger.Cleanup, _ => { }, _ => { }, _ => { });
        AssertUnchanged(GraphMediaWorkResult.IdempotentRegistered, first.Ledger,
            first.Ledger.Register(fixture.Request, fixture.Residences, fixture.Ownership));
        AssertUnchanged(GraphMediaWorkResult.ContradictoryDuplicate, first.Ledger,
            first.Ledger.Register(fixture.Request with { RequestHash = Hash(92) }, fixture.Residences, fixture.Ownership));
        AssertUnchanged(GraphMediaWorkResult.InvalidRequest, fixture.Work,
            fixture.Work.Register(fixture.Request with { Cleanups = fixture.Request.Cleanups.Reverse().ToArray() },
                fixture.Residences, fixture.Ownership));
    }

    [Fact]
    public void Work_transitions_are_linear_and_idempotent()
    {
        var fixture = Registered();
        var running = fixture.Work.StartWork(fixture.Request.WorkId, fixture.Request.RequestHash);
        Assert.Equal(GraphMediaWorkResult.Running, running.Result);
        AssertUnchanged(GraphMediaWorkResult.Running, running.Ledger,
            running.Ledger.StartWork(fixture.Request.WorkId, fixture.Request.RequestHash));
        var terminal = running.Ledger.FinishWork(fixture.Request.WorkId, fixture.Request.RequestHash, Hash(93));
        Assert.Equal(GraphMediaWorkResult.Terminal, terminal.Result);
        AssertUnchanged(GraphMediaWorkResult.Terminal, terminal.Ledger,
            terminal.Ledger.FinishWork(fixture.Request.WorkId, fixture.Request.RequestHash, Hash(93)));
        AssertUnchanged(GraphMediaWorkResult.ContradictoryDuplicate, terminal.Ledger,
            terminal.Ledger.FinishWork(fixture.Request.WorkId, fixture.Request.RequestHash, Hash(94)));
    }

    [Fact]
    public void Cleanup_claims_reverse_registration_order()
    {
        var fixture = Registered();
        var rows = fixture.Request.Cleanups;
        AssertUnchanged(GraphMediaWorkResult.WrongState, fixture.Work,
            fixture.Work.ClaimCleanup(fixture.Request.WorkId, rows[^1].CleanupId, rows[^1].RequestHash));
        var ledger = fixture.Work.FinishWork(fixture.Request.WorkId, fixture.Request.RequestHash, Hash(99)).Ledger;
        AssertUnchanged(GraphMediaWorkResult.CleanupOrderConflict, ledger,
            ledger.ClaimCleanup(fixture.Request.WorkId, rows[0].CleanupId, rows[0].RequestHash));
        for (var index = rows.Count - 1; index >= 0; index--)
        {
            var claimed = ledger.ClaimCleanup(fixture.Request.WorkId, rows[index].CleanupId, rows[index].RequestHash);
            Assert.Equal(GraphMediaWorkResult.Running, claimed.Result);
            var finished = claimed.Ledger.FinishCleanup(rows[index].CleanupId, rows[index].RequestHash, Hash((byte)(100 + index)));
            Assert.Equal(GraphMediaWorkResult.Succeeded, finished.Result);
            ledger = finished.Ledger;
        }

        var global = CreateFixture();
        var first = global.Work.Register(global.Request, global.Residences, global.Ownership).Ledger;
        var laterRequest = WorkRequest(global.Residences, Id(80), global.Request.ResidenceId,
            [new(Id(81), Hash(81)), new(Id(82), Hash(82))]);
        var second = first.Register(laterRequest, global.Residences, global.Ownership).Ledger;
        second = second.FinishWork(global.Request.WorkId, global.Request.RequestHash, Hash(83)).Ledger;
        second = second.FinishWork(laterRequest.WorkId, laterRequest.RequestHash, Hash(84)).Ledger;
        AssertUnchanged(GraphMediaWorkResult.CleanupOrderConflict, second,
            second.ClaimCleanup(global.Request.WorkId, rows[^1].CleanupId, rows[^1].RequestHash));
    }

    [Fact]
    public void Cleanup_unknown_reconciliation_is_closed()
    {
        var fixture = Registered(); var cleanup = fixture.Request.Cleanups[^1];
        var terminal = fixture.Work.FinishWork(fixture.Request.WorkId, fixture.Request.RequestHash, Hash(104));
        var claimed = terminal.Ledger.ClaimCleanup(fixture.Request.WorkId, cleanup.CleanupId, cleanup.RequestHash);
        var unknown = claimed.Ledger.LoseCleanupOutcome(cleanup.CleanupId, cleanup.RequestHash);
        Assert.Equal(GraphMediaWorkResult.OutcomeUnknown, unknown.Result);
        Assert.Equal(GraphMediaReleaseEligibility.Encumbered,
            unknown.Ledger.QueryReleaseEligibility(fixture.Request.ResidenceId));
        var running = unknown.Ledger.ReconcileCleanup(cleanup.CleanupId, cleanup.RequestHash, false, Hash(105));
        Assert.Equal(GraphMediaWorkResult.ReconciledRunning, running.Result);
        var runningRetry = running.Ledger.ReconcileCleanup(cleanup.CleanupId, cleanup.RequestHash, false, Hash(105));
        Assert.Equal(GraphMediaWorkResult.ReconciledRunning, runningRetry.Result);
        Assert.Same(running.Ledger, runningRetry.Ledger);
        AssertUnchanged(GraphMediaWorkResult.ContradictoryDuplicate, running.Ledger,
            running.Ledger.ReconcileCleanup(cleanup.CleanupId, cleanup.RequestHash, false, Hash(107)));
        AssertUnchanged(GraphMediaWorkResult.ContradictoryDuplicate, running.Ledger,
            running.Ledger.ReconcileCleanup(cleanup.CleanupId, cleanup.RequestHash, true, Hash(105)));

        unknown = claimed.Ledger.LoseCleanupOutcome(cleanup.CleanupId, cleanup.RequestHash);
        var succeeded = unknown.Ledger.ReconcileCleanup(cleanup.CleanupId, cleanup.RequestHash, true, Hash(106));
        Assert.Equal(GraphMediaWorkResult.ReconciledSucceeded, succeeded.Result);
        Assert.Equal(GraphMediaCleanupState.Succeeded, succeeded.Ledger.Cleanup[cleanup.CleanupId].State);
        var succeededRetry = succeeded.Ledger.ReconcileCleanup(cleanup.CleanupId, cleanup.RequestHash, true, Hash(106));
        Assert.Equal(GraphMediaWorkResult.ReconciledSucceeded, succeededRetry.Result);
        Assert.Same(succeeded.Ledger, succeededRetry.Ledger);
        AssertUnchanged(GraphMediaWorkResult.ContradictoryDuplicate, succeeded.Ledger,
            succeeded.Ledger.ReconcileCleanup(cleanup.CleanupId, cleanup.RequestHash, true, Hash(108)));
        AssertUnchanged(GraphMediaWorkResult.ContradictoryDuplicate, succeeded.Ledger,
            succeeded.Ledger.ReconcileCleanup(cleanup.CleanupId, cleanup.RequestHash, false, Hash(106)));
    }

    [Fact]
    public void Work_unknown_reconciliation_is_closed_and_retains_release_encumbrance()
    {
        var fixture = Registered();
        var running = fixture.Work.StartWork(fixture.Request.WorkId, fixture.Request.RequestHash);
        var unknown = running.Ledger.LoseWorkOutcome(fixture.Request.WorkId, fixture.Request.RequestHash);
        Assert.Equal(GraphMediaWorkResult.OutcomeUnknown, unknown.Result);
        Assert.Equal(GraphMediaWorkState.Unknown, unknown.Ledger.Work[fixture.Request.WorkId].State);
        Assert.Equal(GraphMediaReleaseEligibility.Encumbered,
            unknown.Ledger.QueryReleaseEligibility(fixture.Request.ResidenceId));
        AssertUnchanged(GraphMediaWorkResult.WrongState, unknown.Ledger,
            unknown.Ledger.FinishWork(fixture.Request.WorkId, fixture.Request.RequestHash, Hash(108)));

        var stillRunning = unknown.Ledger.ReconcileWork(fixture.Request.WorkId,
            fixture.Request.RequestHash, false, Hash(109));
        Assert.Equal(GraphMediaWorkResult.ReconciledRunning, stillRunning.Result);
        Assert.Same(stillRunning.Ledger, stillRunning.Ledger.ReconcileWork(fixture.Request.WorkId,
            fixture.Request.RequestHash, false, Hash(109)).Ledger);
        AssertUnchanged(GraphMediaWorkResult.ContradictoryDuplicate, stillRunning.Ledger,
            stillRunning.Ledger.ReconcileWork(fixture.Request.WorkId,
                fixture.Request.RequestHash, false, Hash(110)));

        unknown = running.Ledger.LoseWorkOutcome(fixture.Request.WorkId, fixture.Request.RequestHash);
        var terminal = unknown.Ledger.ReconcileWork(fixture.Request.WorkId,
            fixture.Request.RequestHash, true, Hash(111));
        Assert.Equal(GraphMediaWorkResult.ReconciledTerminal, terminal.Result);
        Assert.Equal(GraphMediaWorkState.Terminal, terminal.Ledger.Work[fixture.Request.WorkId].State);
        Assert.Equal(Hash(111), terminal.Ledger.Work[fixture.Request.WorkId].OutcomeHash);
        Assert.Same(terminal.Ledger, terminal.Ledger.ReconcileWork(fixture.Request.WorkId,
            fixture.Request.RequestHash, true, Hash(111)).Ledger);
    }

    [Fact]
    public void Rejected_execution_returns_running_work_to_registered_for_a_later_operation()
    {
        var fixture = Registered();
        var running = fixture.Work.StartWork(fixture.Request.WorkId, fixture.Request.RequestHash);
        var rejected = running.Ledger.RejectWork(fixture.Request.WorkId, fixture.Request.RequestHash);
        Assert.Equal(GraphMediaWorkResult.Rejected, rejected.Result);
        Assert.Equal(GraphMediaWorkState.Registered, rejected.Ledger.Work[fixture.Request.WorkId].State);
        Assert.Null(rejected.Ledger.Work[fixture.Request.WorkId].OutcomeHash);
        Assert.Equal(GraphMediaWorkResult.Running,
            rejected.Ledger.StartWork(fixture.Request.WorkId, fixture.Request.RequestHash).Result);
    }

    [Fact]
    public void Bounds_fail_before_mutation()
    {
        var fixture = CreateFixture(); var ledger = fixture.Work;
        for (byte index = 0; index < GraphMediaWorkLedger.MaximumWorkPerRuntime; index++)
        {
            var request = WorkRequest(fixture.Residences, Id((byte)(110 + index)), fixture.Request.ResidenceId,
                [new(Id((byte)(180 + index)), Hash((byte)(180 + index)))]);
            var registered = ledger.Register(request, fixture.Residences, fixture.Ownership);
            Assert.Equal(GraphMediaWorkResult.Registered, registered.Result); ledger = registered.Ledger;
        }
        var overflow = WorkRequest(fixture.Residences, Id(109), fixture.Request.ResidenceId,
            [new(Id(179), Hash(179))]);
        AssertUnchanged(GraphMediaWorkResult.WorkLimitReached, ledger,
            ledger.Register(overflow, fixture.Residences, fixture.Ownership));

        var tooMany = new GraphMediaWorkRegistration(Id(108), Hash(108), fixture.Request.ResidenceId,
            Enumerable.Range(0, GraphMediaWorkLedger.MaximumCleanupPerWork + 1)
                .Select(i => new GraphMediaCleanupRegistration(Id((byte)(20 + i)), Hash((byte)(20 + i)))).ToArray());
        AssertUnchanged(GraphMediaWorkResult.InvalidRequest, fixture.Work,
            fixture.Work.Register(tooMany, fixture.Residences, fixture.Ownership));

        ledger = fixture.Work;
        for (byte workIndex = 0; workIndex < 4; workIndex++)
        {
            var cleanups = Enumerable.Range(0, GraphMediaWorkLedger.MaximumCleanupPerWork)
                .Select(index => new GraphMediaCleanupRegistration(
                    Id((byte)(100 + workIndex * GraphMediaWorkLedger.MaximumCleanupPerWork + index)),
                    Hash((byte)(100 + workIndex * GraphMediaWorkLedger.MaximumCleanupPerWork + index))))
                .ToArray();
            var request = WorkRequest(fixture.Residences, Id((byte)(210 + workIndex)),
                fixture.Request.ResidenceId, cleanups);
            ledger = ledger.Register(request, fixture.Residences, fixture.Ownership).Ledger;
        }
        Assert.Equal(GraphMediaWorkLedger.MaximumCleanupPerRuntime, ledger.Cleanup.Count);
        var cleanupOverflow = WorkRequest(fixture.Residences, Id(214), fixture.Request.ResidenceId,
            [new(Id(164), Hash(164))]);
        AssertUnchanged(GraphMediaWorkResult.CleanupLimitReached, ledger,
            ledger.Register(cleanupOverflow, fixture.Residences, fixture.Ownership));
    }

    [Fact]
    public void Release_eligibility_requires_terminal_work_and_cleanup()
    {
        var fixture = Registered();
        Assert.Equal(GraphMediaReleaseEligibility.Encumbered,
            fixture.Work.QueryReleaseEligibility(fixture.Request.ResidenceId));
        var ledger = fixture.Work.FinishWork(fixture.Request.WorkId, fixture.Request.RequestHash, Hash(200)).Ledger;
        foreach (var cleanup in fixture.Request.Cleanups.Reverse())
        {
            ledger = ledger.ClaimCleanup(fixture.Request.WorkId, cleanup.CleanupId, cleanup.RequestHash).Ledger;
            ledger = ledger.FinishCleanup(cleanup.CleanupId, cleanup.RequestHash, Hash(201)).Ledger;
        }
        Assert.Equal(GraphMediaReleaseEligibility.Eligible,
            ledger.QueryReleaseEligibility(fixture.Request.ResidenceId));
        Assert.Equal(GraphMediaReleaseEligibility.NotFound, ledger.QueryReleaseEligibility(Id(202)));
    }

    [Fact]
    public void Surface_and_forbidden_effect_inventory_is_exact()
    {
        Assert.False(typeof(GraphMediaWorkLedger).IsPublic);
        Assert.Equal(64, GraphMediaWorkLedger.MaximumWorkPerRuntime);
        Assert.Equal(64, GraphMediaWorkLedger.MaximumCleanupPerRuntime);
        Assert.Equal(16, GraphMediaWorkLedger.MaximumCleanupPerWork);
    }

    private static void AssertUnchanged(GraphMediaWorkResult expected, GraphMediaWorkLedger before,
        GraphMediaWorkTransition result)
    { Assert.Equal(expected, result.Result); Assert.Same(before, result.Ledger); Assert.Equal(before.Fingerprint, result.Ledger.Fingerprint); }

    private static (GraphMediaWorkRegistration Request, GraphMediaResidenceLedger Residences,
        GraphMediaOwnershipLedger Ownership, GraphMediaWorkLedger Work) CreateFixture()
    {
        var controlled = CreateControlledFixture(true);
        var request = WorkRequest(controlled.Residences, Id(70), controlled.Request.ResidenceId,
            [new(Id(71), Hash(71)), new(Id(72), Hash(72)), new(Id(73), Hash(73))]);
        return (request, controlled.Residences, controlled.Ownership,
            GraphMediaWorkLedger.Create(Session(), Graph()));
    }

    private static (GraphMediaWorkRegistration Request, GraphMediaResidenceLedger Residences,
        GraphMediaOwnershipLedger Ownership, GraphMediaWorkLedger Work) Registered()
    {
        var fixture = CreateFixture();
        var registered = fixture.Work.Register(fixture.Request, fixture.Residences, fixture.Ownership);
        Assert.Equal(GraphMediaWorkResult.Registered, registered.Result);
        return fixture with { Work = registered.Ledger };
    }

    private static GraphMediaWorkRegistration WorkRequest(GraphMediaResidenceLedger residences,
        StableId128 workId, StableId128 residenceId, IReadOnlyList<GraphMediaCleanupRegistration> cleanup)
    {
        var request = new GraphMediaWorkRegistration(workId, Hash(1), residenceId, cleanup);
        return request with { RequestHash = GraphMediaWorkLedger.RegistrationHash(request, residences.Residences[residenceId]) };
    }

    private static (GraphMediaControlledResidenceRequest Request, GraphMediaResidenceLedger Residences,
        GraphMediaOwnershipLedger Ownership) CreateControlledFixture(bool makeVisible)
    {
        var session = Session(); var graph = Graph(); var sourceId = Id(10); var destinationId = Id(11);
        Assert.True(GraphMediaBinding.TryCreate(0, 1_000, Id(12), 1, 48_000, 2, 2, Id(13), 1, 0,
            GraphMediaDiscontinuityKind.ResetBefore, 400, 100, null, out var media));
        var ownership = GraphMediaOwnershipLedger.Create(new(session, graph, Id(14)), sourceId, media!);
        var source = ownership.Owners[sourceId]; var operation = Operation(15); var participant = ParticipantId.FromValue(Id(16));
        var authority = ExpectedAuthorityVectorV1.Create(session, [new AuthorityAxisValueV1.Graph(graph)]);
        var scope = new CapacityScopeV1(TenantId.FromValue(Id(17)), SessionId.FromValue(Id(18)), new CapacitySubjectV1.Participant(participant));
        var charges = new[] { Charge(1, 400, 19, scope), Charge(4, 200, 20, scope), Charge(5, 1_000, 21, scope) };
        var capacity = new CapacityRequestV1(operation, authority, charges, Stamp(30), CapacityPriorityV1.Normal);
        var preGrant = new GraphParticipantPreGrantPlan(participant, operation, Position(1), Position(2), graph,
            Hash(22), new("factory"), [1], Hash(23), [new("node")], [2], Hash(24), capacity);
        var grantId = CapacityGrantId.FromValue(Id(25));
        var topology = new GraphTopologyPlan(session, graph, grantId, [new(new("node"))], [], [new(1), new(4), new(5)]);
        var catalog = Assert.IsType<GraphRuntimeExecutableCatalogResult.Created>(
            GraphRuntimeExecutableFactoryCatalog.FromGeneratedApplicationManifest([new(new("node"), "tests:node@1", 1)]));
        var executable = Assert.IsType<GraphRuntimeExecutableCompileResult.Compiled>(
            GraphRuntimeExecutablePlan.Compile(topology, topology.Fingerprint, catalog, charges)).Plan;
        var evidence = new GraphParticipantBindingPlanEvidence(preGrant, grantId, Position(30), Position(31),
            new CapacityGrantExpiryV1.NoExpiry(), [3], Hash(26), topology, executable,
            topology.Fingerprint, executable.Fingerprint);
        var reservation = new GraphParticipantReservationV1(participant, new("factory"), [new("node")]);
        var reservationApplied = new GraphParticipantReservationFoldV2.AppliedReservation(
            Envelope(Position(1), [1]), Envelope(Position(2), [2]), reservation);
        var proof = new CapacityGrantBindingProofV1(grantId, evidence.GrantedAt, evidence.CurrentFact, 3, evidence.CoverageHashV2);
        var binding = new GraphParticipantBindingV1(participant, new("factory"), [new("node")]);
        var body = new GraphParticipantBindingCommandBodyV1(operation, Position(2), null, graph,
            session.RuntimeGenerationId, preGrant.ParticipantPlanFingerprint, evidence.TopologyFingerprint,
            evidence.ExecutableFingerprint, proof, Stamp(40));
        var payload = GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(session,
            authority, GraphParticipantBindingCodecsV1.Encode(body)));
        var command = Envelope(Position(3), payload, GraphParticipantBindingPayloadRegistrationsV1.BindingCommand);
        var fact = Envelope(Position(4), [9], GraphParticipantBindingPayloadRegistrationsV1.BindingFact);
        var bound = new GraphParticipantBindingResult.Bound(command.Position, fact.Position, fact.PayloadMemory, binding, proof);
        var fold = new GraphParticipantBindingFoldQueryResultV2.Bound(reservationApplied, command, fact, binding, proof);
        var request = new GraphMediaControlledResidenceRequest(operation, Hash(27), Id(28), sourceId,
            destinationId, new("node"), GraphMediaRepresentationArm.ResidentBytes, bound, fold, evidence);
        var assignment = new GraphMediaCapacityAssignment(charges[0], request.Arm);
        request = request with { RequestHash = GraphMediaResidenceLedger.ResidenceHash(request, source, assignment) };
        var residences = GraphMediaResidenceLedger.Create(session, graph).PrepareControlled(request, ownership).Ledger;
        if (!makeVisible) return (request, residences, ownership);
        ownership = ownership.CopyOwners(session, graph, sourceId, [destinationId]).Ledger;
        var visible = residences.MakeVisible(operation, request.RequestHash, ownership);
        Assert.Equal(GraphMediaResidenceResult.Visible, visible.Result);
        return (request, visible.Ledger, ownership);
    }

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
