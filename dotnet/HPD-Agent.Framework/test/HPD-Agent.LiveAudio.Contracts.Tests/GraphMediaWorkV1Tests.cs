using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.Graph.Runtime;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphMediaWorkV1Tests
{
    [Fact]
    public void Registration_requires_exact_visible_residence_and_owner()
    {
        var fixture = CreateFixture();
        var registered = fixture.Work.Register(fixture.Request, fixture.Residences, fixture.Ownership);
        Assert.Equal(GraphMediaWorkResultV1.Registered, registered.Result);
        Assert.NotSame(fixture.Work, registered.Ledger);

        var hidden = CreateControlledFixture(makeVisible: false);
        var request = fixture.Request with { ResidenceId = hidden.Request.ResidenceId };
        AssertUnchanged(GraphMediaWorkResultV1.ResidenceNotVisible, fixture.Work,
            fixture.Work.Register(request, hidden.Residences, hidden.Ownership));

        AssertUnchanged(GraphMediaWorkResultV1.ResidenceNotFound, fixture.Work,
            fixture.Work.Register(fixture.Request with { ResidenceId = Id(249) }, fixture.Residences, fixture.Ownership));
        var residence = fixture.Residences.Residences[fixture.Request.ResidenceId];
        var wrongOwner = GraphMediaOwnershipLedgerV1.Create(residence.OwnerKey, Id(250), residence.Media);
        AssertUnchanged(GraphMediaWorkResultV1.OwnerMismatch, fixture.Work,
            fixture.Work.Register(fixture.Request, fixture.Residences, wrongOwner));

        var stale = GraphMediaWorkLedgerV1.Create(
            new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(90)), LiveSessionId.FromValue(Id(91))), Graph());
        AssertUnchanged(GraphMediaWorkResultV1.StaleGeneration, stale,
            stale.Register(fixture.Request, fixture.Residences, fixture.Ownership));
    }

    [Fact]
    public void Registration_is_atomic_and_retries_are_closed()
    {
        var fixture = CreateFixture();
        var first = fixture.Work.Register(fixture.Request, fixture.Residences, fixture.Ownership);
        Assert.Single(first.Ledger.Work);
        Assert.Collection(first.Ledger.Cleanup, _ => { }, _ => { }, _ => { });
        AssertUnchanged(GraphMediaWorkResultV1.IdempotentRegistered, first.Ledger,
            first.Ledger.Register(fixture.Request, fixture.Residences, fixture.Ownership));
        AssertUnchanged(GraphMediaWorkResultV1.ContradictoryDuplicate, first.Ledger,
            first.Ledger.Register(fixture.Request with { RequestHash = Hash(92) }, fixture.Residences, fixture.Ownership));
        AssertUnchanged(GraphMediaWorkResultV1.InvalidRequest, fixture.Work,
            fixture.Work.Register(fixture.Request with { Cleanups = fixture.Request.Cleanups.Reverse().ToArray() },
                fixture.Residences, fixture.Ownership));
    }

    [Fact]
    public void Work_transitions_are_linear_and_idempotent()
    {
        var fixture = Registered();
        var running = fixture.Work.StartWork(fixture.Request.WorkId, fixture.Request.RequestHash);
        Assert.Equal(GraphMediaWorkResultV1.Running, running.Result);
        AssertUnchanged(GraphMediaWorkResultV1.Running, running.Ledger,
            running.Ledger.StartWork(fixture.Request.WorkId, fixture.Request.RequestHash));
        var terminal = running.Ledger.FinishWork(fixture.Request.WorkId, fixture.Request.RequestHash, Hash(93));
        Assert.Equal(GraphMediaWorkResultV1.Terminal, terminal.Result);
        AssertUnchanged(GraphMediaWorkResultV1.Terminal, terminal.Ledger,
            terminal.Ledger.FinishWork(fixture.Request.WorkId, fixture.Request.RequestHash, Hash(93)));
        AssertUnchanged(GraphMediaWorkResultV1.ContradictoryDuplicate, terminal.Ledger,
            terminal.Ledger.FinishWork(fixture.Request.WorkId, fixture.Request.RequestHash, Hash(94)));
    }

    [Fact]
    public void Cleanup_claims_reverse_registration_order()
    {
        var fixture = Registered();
        var rows = fixture.Request.Cleanups;
        AssertUnchanged(GraphMediaWorkResultV1.WrongState, fixture.Work,
            fixture.Work.ClaimCleanup(fixture.Request.WorkId, rows[^1].CleanupId, rows[^1].RequestHash));
        var ledger = fixture.Work.FinishWork(fixture.Request.WorkId, fixture.Request.RequestHash, Hash(99)).Ledger;
        AssertUnchanged(GraphMediaWorkResultV1.CleanupOrderConflict, ledger,
            ledger.ClaimCleanup(fixture.Request.WorkId, rows[0].CleanupId, rows[0].RequestHash));
        for (var index = rows.Count - 1; index >= 0; index--)
        {
            var claimed = ledger.ClaimCleanup(fixture.Request.WorkId, rows[index].CleanupId, rows[index].RequestHash);
            Assert.Equal(GraphMediaWorkResultV1.Running, claimed.Result);
            var finished = claimed.Ledger.FinishCleanup(rows[index].CleanupId, rows[index].RequestHash, Hash((byte)(100 + index)));
            Assert.Equal(GraphMediaWorkResultV1.Succeeded, finished.Result);
            ledger = finished.Ledger;
        }

        var global = CreateFixture();
        var first = global.Work.Register(global.Request, global.Residences, global.Ownership).Ledger;
        var laterRequest = WorkRequest(global.Residences, Id(80), global.Request.ResidenceId,
            [new(Id(81), Hash(81)), new(Id(82), Hash(82))]);
        var second = first.Register(laterRequest, global.Residences, global.Ownership).Ledger;
        second = second.FinishWork(global.Request.WorkId, global.Request.RequestHash, Hash(83)).Ledger;
        second = second.FinishWork(laterRequest.WorkId, laterRequest.RequestHash, Hash(84)).Ledger;
        AssertUnchanged(GraphMediaWorkResultV1.CleanupOrderConflict, second,
            second.ClaimCleanup(global.Request.WorkId, rows[^1].CleanupId, rows[^1].RequestHash));
    }

    [Fact]
    public void Cleanup_unknown_reconciliation_is_closed()
    {
        var fixture = Registered(); var cleanup = fixture.Request.Cleanups[^1];
        var terminal = fixture.Work.FinishWork(fixture.Request.WorkId, fixture.Request.RequestHash, Hash(104));
        var claimed = terminal.Ledger.ClaimCleanup(fixture.Request.WorkId, cleanup.CleanupId, cleanup.RequestHash);
        var unknown = claimed.Ledger.LoseCleanupOutcome(cleanup.CleanupId, cleanup.RequestHash);
        Assert.Equal(GraphMediaWorkResultV1.OutcomeUnknown, unknown.Result);
        Assert.Equal(GraphMediaReleaseEligibilityV1.Encumbered,
            unknown.Ledger.QueryReleaseEligibility(fixture.Request.ResidenceId));
        var running = unknown.Ledger.ReconcileCleanup(cleanup.CleanupId, cleanup.RequestHash, false, Hash(105));
        Assert.Equal(GraphMediaWorkResultV1.ReconciledRunning, running.Result);
        var runningRetry = running.Ledger.ReconcileCleanup(cleanup.CleanupId, cleanup.RequestHash, false, Hash(105));
        Assert.Equal(GraphMediaWorkResultV1.ReconciledRunning, runningRetry.Result);
        Assert.Same(running.Ledger, runningRetry.Ledger);
        AssertUnchanged(GraphMediaWorkResultV1.ContradictoryDuplicate, running.Ledger,
            running.Ledger.ReconcileCleanup(cleanup.CleanupId, cleanup.RequestHash, false, Hash(107)));
        AssertUnchanged(GraphMediaWorkResultV1.ContradictoryDuplicate, running.Ledger,
            running.Ledger.ReconcileCleanup(cleanup.CleanupId, cleanup.RequestHash, true, Hash(105)));

        unknown = claimed.Ledger.LoseCleanupOutcome(cleanup.CleanupId, cleanup.RequestHash);
        var succeeded = unknown.Ledger.ReconcileCleanup(cleanup.CleanupId, cleanup.RequestHash, true, Hash(106));
        Assert.Equal(GraphMediaWorkResultV1.ReconciledSucceeded, succeeded.Result);
        Assert.Equal(GraphMediaCleanupStateV1.Succeeded, succeeded.Ledger.Cleanup[cleanup.CleanupId].State);
        var succeededRetry = succeeded.Ledger.ReconcileCleanup(cleanup.CleanupId, cleanup.RequestHash, true, Hash(106));
        Assert.Equal(GraphMediaWorkResultV1.ReconciledSucceeded, succeededRetry.Result);
        Assert.Same(succeeded.Ledger, succeededRetry.Ledger);
        AssertUnchanged(GraphMediaWorkResultV1.ContradictoryDuplicate, succeeded.Ledger,
            succeeded.Ledger.ReconcileCleanup(cleanup.CleanupId, cleanup.RequestHash, true, Hash(108)));
        AssertUnchanged(GraphMediaWorkResultV1.ContradictoryDuplicate, succeeded.Ledger,
            succeeded.Ledger.ReconcileCleanup(cleanup.CleanupId, cleanup.RequestHash, false, Hash(106)));
    }

    [Fact]
    public void Bounds_fail_before_mutation()
    {
        var fixture = CreateFixture(); var ledger = fixture.Work;
        for (byte index = 0; index < GraphMediaWorkLedgerV1.MaximumWorkPerRuntime; index++)
        {
            var request = WorkRequest(fixture.Residences, Id((byte)(110 + index)), fixture.Request.ResidenceId,
                [new(Id((byte)(180 + index)), Hash((byte)(180 + index)))]);
            var registered = ledger.Register(request, fixture.Residences, fixture.Ownership);
            Assert.Equal(GraphMediaWorkResultV1.Registered, registered.Result); ledger = registered.Ledger;
        }
        var overflow = WorkRequest(fixture.Residences, Id(109), fixture.Request.ResidenceId,
            [new(Id(179), Hash(179))]);
        AssertUnchanged(GraphMediaWorkResultV1.WorkLimitReached, ledger,
            ledger.Register(overflow, fixture.Residences, fixture.Ownership));

        var tooMany = new GraphMediaWorkRegistrationV1(Id(108), Hash(108), fixture.Request.ResidenceId,
            Enumerable.Range(0, GraphMediaWorkLedgerV1.MaximumCleanupPerWork + 1)
                .Select(i => new GraphMediaCleanupRegistrationV1(Id((byte)(20 + i)), Hash((byte)(20 + i)))).ToArray());
        AssertUnchanged(GraphMediaWorkResultV1.InvalidRequest, fixture.Work,
            fixture.Work.Register(tooMany, fixture.Residences, fixture.Ownership));

        ledger = fixture.Work;
        for (byte workIndex = 0; workIndex < 4; workIndex++)
        {
            var cleanups = Enumerable.Range(0, GraphMediaWorkLedgerV1.MaximumCleanupPerWork)
                .Select(index => new GraphMediaCleanupRegistrationV1(
                    Id((byte)(100 + workIndex * GraphMediaWorkLedgerV1.MaximumCleanupPerWork + index)),
                    Hash((byte)(100 + workIndex * GraphMediaWorkLedgerV1.MaximumCleanupPerWork + index))))
                .ToArray();
            var request = WorkRequest(fixture.Residences, Id((byte)(210 + workIndex)),
                fixture.Request.ResidenceId, cleanups);
            ledger = ledger.Register(request, fixture.Residences, fixture.Ownership).Ledger;
        }
        Assert.Equal(GraphMediaWorkLedgerV1.MaximumCleanupPerRuntime, ledger.Cleanup.Count);
        var cleanupOverflow = WorkRequest(fixture.Residences, Id(214), fixture.Request.ResidenceId,
            [new(Id(164), Hash(164))]);
        AssertUnchanged(GraphMediaWorkResultV1.CleanupLimitReached, ledger,
            ledger.Register(cleanupOverflow, fixture.Residences, fixture.Ownership));
    }

    [Fact]
    public void Release_eligibility_requires_terminal_work_and_cleanup()
    {
        var fixture = Registered();
        Assert.Equal(GraphMediaReleaseEligibilityV1.Encumbered,
            fixture.Work.QueryReleaseEligibility(fixture.Request.ResidenceId));
        var ledger = fixture.Work.FinishWork(fixture.Request.WorkId, fixture.Request.RequestHash, Hash(200)).Ledger;
        foreach (var cleanup in fixture.Request.Cleanups.Reverse())
        {
            ledger = ledger.ClaimCleanup(fixture.Request.WorkId, cleanup.CleanupId, cleanup.RequestHash).Ledger;
            ledger = ledger.FinishCleanup(cleanup.CleanupId, cleanup.RequestHash, Hash(201)).Ledger;
        }
        Assert.Equal(GraphMediaReleaseEligibilityV1.Eligible,
            ledger.QueryReleaseEligibility(fixture.Request.ResidenceId));
        Assert.Equal(GraphMediaReleaseEligibilityV1.NotFound, ledger.QueryReleaseEligibility(Id(202)));
    }

    [Fact]
    public void Surface_and_forbidden_effect_inventory_is_exact()
    {
        Assert.False(typeof(GraphMediaWorkLedgerV1).IsPublic);
        Assert.Equal(64, GraphMediaWorkLedgerV1.MaximumWorkPerRuntime);
        Assert.Equal(64, GraphMediaWorkLedgerV1.MaximumCleanupPerRuntime);
        Assert.Equal(16, GraphMediaWorkLedgerV1.MaximumCleanupPerWork);
    }

    private static void AssertUnchanged(GraphMediaWorkResultV1 expected, GraphMediaWorkLedgerV1 before,
        GraphMediaWorkTransitionV1 result)
    { Assert.Equal(expected, result.Result); Assert.Same(before, result.Ledger); Assert.Equal(before.Fingerprint, result.Ledger.Fingerprint); }

    private static (GraphMediaWorkRegistrationV1 Request, GraphMediaResidenceLedgerV1 Residences,
        GraphMediaOwnershipLedgerV1 Ownership, GraphMediaWorkLedgerV1 Work) CreateFixture()
    {
        var controlled = CreateControlledFixture(true);
        var request = WorkRequest(controlled.Residences, Id(70), controlled.Request.ResidenceId,
            [new(Id(71), Hash(71)), new(Id(72), Hash(72)), new(Id(73), Hash(73))]);
        return (request, controlled.Residences, controlled.Ownership,
            GraphMediaWorkLedgerV1.Create(Session(), Graph()));
    }

    private static (GraphMediaWorkRegistrationV1 Request, GraphMediaResidenceLedgerV1 Residences,
        GraphMediaOwnershipLedgerV1 Ownership, GraphMediaWorkLedgerV1 Work) Registered()
    {
        var fixture = CreateFixture();
        var registered = fixture.Work.Register(fixture.Request, fixture.Residences, fixture.Ownership);
        Assert.Equal(GraphMediaWorkResultV1.Registered, registered.Result);
        return fixture with { Work = registered.Ledger };
    }

    private static GraphMediaWorkRegistrationV1 WorkRequest(GraphMediaResidenceLedgerV1 residences,
        StableId128 workId, StableId128 residenceId, IReadOnlyList<GraphMediaCleanupRegistrationV1> cleanup)
    {
        var request = new GraphMediaWorkRegistrationV1(workId, Hash(1), residenceId, cleanup);
        return request with { RequestHash = GraphMediaWorkLedgerV1.RegistrationHash(request, residences.Residences[residenceId]) };
    }

    private static (GraphMediaControlledResidenceRequestV1 Request, GraphMediaResidenceLedgerV1 Residences,
        GraphMediaOwnershipLedgerV1 Ownership) CreateControlledFixture(bool makeVisible)
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
        var bound = new GraphParticipantBindingResultV2.Bound(command.Position, fact.Position, fact.PayloadMemory, binding, proof);
        var fold = new GraphParticipantBindingFoldQueryResultV2.Bound(reservationApplied, command, fact, binding, proof);
        var request = new GraphMediaControlledResidenceRequestV1(operation, Hash(27), Id(28), sourceId,
            destinationId, new("node"), GraphMediaRepresentationArmV1.ResidentBytes, bound, fold, evidence);
        var assignment = new GraphMediaCapacityAssignmentV1(charges[0], request.Arm);
        request = request with { RequestHash = GraphMediaResidenceLedgerV1.ResidenceHash(request, source, assignment) };
        var residences = GraphMediaResidenceLedgerV1.Create(session, graph).PrepareControlled(request, ownership).Ledger;
        if (!makeVisible) return (request, residences, ownership);
        ownership = ownership.CopyOwners(session, graph, sourceId, [destinationId]).Ledger;
        var visible = residences.MakeVisible(operation, request.RequestHash, ownership);
        Assert.Equal(GraphMediaResidenceResultV1.Visible, visible.Result);
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
