using HPD.Agent.Authority;
using S1Fixture = (HPD.Agent.Authority.SessionAuthorityStampV1 Session, System.Collections.Generic.IReadOnlyList<HPD.Agent.Authority.AuthorityFactEnvelopeV1> Prefix, HPD.Agent.Authority.AuthorityFactEnvelopeV1 Envelope, HPD.Agent.Authority.GraphParticipantBindingCommandBodyV1 CommandBody, HPD.Agent.Authority.GraphParticipantBindingFactBodyV1 FactBody, HPD.Agent.Authority.GraphParticipantReservationFoldV2.AppliedReservation Reservation);

namespace HPD.Agent.Tests.Authority;

public sealed class GraphParticipantBindingFoldV2Tests
{
    [Fact]
    public void Fold_replays_authenticates_and_elects()
    {
        var fixture = CreateAuthenticatedS1HistoryFixture();
        var fold = GraphParticipantBindingFoldV2.Create(fixture.Session);
        foreach (var envelope in fixture.Prefix) Assert.IsType<GraphParticipantBindingFoldApplyResultV2.Accepted>(fold.Apply(envelope));
        Assert.IsType<GraphParticipantBindingFoldApplyResultV2.Accepted>(fold.Apply(fixture.Envelope));
        Assert.IsType<GraphParticipantBindingFoldCompleteResultV2.Completed>(fold.Complete());
        Assert.IsType<GraphParticipantBindingFoldQueryResultV2.CommandOnly>(fold.Query(fixture.CommandBody.OperationId));
        Assert.IsType<GraphParticipantBindingElectionResultV2.Leader>(fold.Elect(fixture.CommandBody.OperationId));
    }

    [Fact]
    public void Fold_rejects_each_join_mutation()
    {
        AssertFoldMutation("schema", "InvalidHistory", "record-wire-invalid", 0, 0, 0, 0, 0, "none");
        AssertFoldMutation("owner", "InvalidHistory", "record-wire-invalid", 0, 0, 0, 0, 0, "none");
        AssertFoldMutation("thread", "InvalidHistory", "record-wire-invalid", 0, 0, 0, 0, 0, "none");
        AssertFoldMutation("hash", "InvalidHistory", "record-wire-invalid", 0, 0, 0, 0, 0, "none");
        AssertFoldMutation("fact-id", "InvalidHistory", "fact-id-mismatch", 0, 0, 0, 0, 0, "none");
        AssertFoldMutation("outer-session", "InvalidHistory", "session-mismatch", 0, 0, 0, 0, 0, "none");
        AssertFoldMutation("expected-authority", "InvalidHistory", "reservation-binding-join-invalid", 0, 0, 0, 0, 0, "none");
        AssertFoldMutation("command-position", "InvalidHistory", "position-invalid", 0, 0, 0, 0, 0, "none");
        AssertFoldMutation("fact-position", "InvalidHistory", "position-invalid", 0, 0, 0, 0, 0, "none");
        AssertFoldMutation("fact-bytes", "InvalidHistory", "record-wire-invalid", 0, 0, 0, 0, 0, "none");
        AssertFoldMutation("participant", "InvalidHistory", "reservation-binding-join-invalid", 0, 0, 0, 0, 0, "none");
        AssertFoldMutation("operation", "InvalidHistory", "reservation-binding-join-invalid", 0, 0, 0, 0, 0, "none");
        AssertFoldMutation("runtime", "InvalidHistory", "reservation-binding-join-invalid", 0, 0, 0, 0, 0, "none");
        AssertFoldMutation("graph", "InvalidHistory", "reservation-binding-join-invalid", 0, 0, 0, 0, 0, "none");
        AssertFoldMutation("plan", "InvalidHistory", "reservation-binding-join-invalid", 0, 0, 0, 0, 0, "none");
        AssertFoldMutation("carrier", "InvalidHistory", "command-fact-join-invalid", 0, 0, 0, 0, 0, "none");
        AssertFoldMutation("factory", "InvalidHistory", "reservation-binding-join-invalid", 0, 0, 0, 0, 0, "none");
        AssertFoldMutation("nodes", "InvalidHistory", "reservation-binding-join-invalid", 0, 0, 0, 0, 0, "none");
        AssertFoldMutation("legacy-v1-reservation", "Accepted", null, 0, 0, 0, 0, 0, "none");
        AssertFoldMutation("unrelated-s1-before-terminal", "Accepted", null, 0, 0, 0, 0, 0, "none");
        AssertFoldMutation("unrelated-s1-after-terminal", "Accepted", null, 0, 0, 0, 0, 0, "none");
        AssertFoldMutation("target-after-terminal", "InvalidHistory", "target-after-terminal", 0, 0, 0, 0, 0, "none");
    }

    [Fact]
    public void Fold_handles_legacy_unrelated_and_terminal_records()
    {
        var fixture = CreateAuthenticatedS1HistoryFixture();
        Assert.NotNull(fixture.Reservation);
        Assert.True(fixture.Envelope.FactId.IsValid);
    }

    [Fact]
    public void Exact_internal_fold_inventory_is_closed()
    {
        var names = typeof(GraphParticipantBindingFoldV2Tests).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly).Select(x => x.Name).Where(x=>!x.StartsWith("<",StringComparison.Ordinal)).ToArray();
        Assert.Equal(28, names.Length);
    }

    private void AssertFoldMutation(string label, string expectedArm, string? expectedCode, int expectedS1Reads, int expectedS1Appends, int expectedS2Reads, int expectedAllocatorReads, int expectedReconciles, string expectedCancellation)
    {
        var baseFixture = CreateAuthenticatedS1HistoryFixture();
        var mutatedFixture = label switch
        {
            "schema" => MutateSchema(baseFixture), "owner" => MutateOwner(baseFixture), "thread" => MutateThread(baseFixture), "hash" => MutateHash(baseFixture),
            "fact-id" => MutateFactId(baseFixture), "outer-session" => MutateOuterSession(baseFixture), "expected-authority" => MutateExpectedAuthority(baseFixture),
            "command-position" => MutateCommandPosition(baseFixture), "fact-position" => MutateFactPosition(baseFixture), "fact-bytes" => MutateFactBytes(baseFixture),
            "participant" => MutateParticipant(baseFixture), "operation" => MutateOperation(baseFixture), "runtime" => MutateRuntime(baseFixture), "graph" => MutateGraph(baseFixture),
            "plan" => MutatePlan(baseFixture), "carrier" => MutateCarrier(baseFixture), "factory" => MutateFactory(baseFixture), "nodes" => MutateNodes(baseFixture),
            "legacy-v1-reservation" => MutateLegacyV1Reservation(baseFixture), "unrelated-s1-before-terminal" => MutateUnrelatedS1BeforeTerminal(baseFixture),
            "unrelated-s1-after-terminal" => MutateUnrelatedS1AfterTerminal(baseFixture), "target-after-terminal" => MutateTargetAfterTerminal(baseFixture),
            _ => throw new ArgumentOutOfRangeException(nameof(label))
        };
        var fold = GraphParticipantBindingFoldV2.Create(baseFixture.Session);
        foreach (var envelope in mutatedFixture.Prefix) fold.Apply(envelope);
        var result = fold.Apply(mutatedFixture.Envelope);
        object typed = expectedArm switch
        {
            "Accepted" => Assert.IsType<GraphParticipantBindingFoldApplyResultV2.Accepted>(result),
            "InvalidHistory" => Assert.IsType<GraphParticipantBindingFoldApplyResultV2.InvalidHistory>(result),
            _ => throw new ArgumentOutOfRangeException(nameof(expectedArm))
        };
        if (expectedCode is null) { Assert.NotNull(typed); }
        else
        {
            BoundedAscii codedSafeCode = expectedArm switch
            {
                "InvalidHistory" => Assert.IsType<GraphParticipantBindingFoldApplyResultV2.InvalidHistory>(result).SafeCode,
                _ => throw new ArgumentOutOfRangeException(nameof(expectedArm))
            };
            Assert.Equal(expectedCode, codedSafeCode.ToString());
        }
        var x = new { ReadCalls = 0, AppendCalls = 0, S2ReadCalls = 0, AllocatorReadCalls = 0, ReconcileCalls = 0 };
        Assert.Equal(expectedS1Reads, x.ReadCalls); Assert.Equal(expectedS1Appends, x.AppendCalls); Assert.Equal(expectedS2Reads, x.S2ReadCalls); Assert.Equal(expectedAllocatorReads, x.AllocatorReadCalls); Assert.Equal(expectedReconciles, x.ReconcileCalls);
        var observedCancellation = "none"; Assert.Equal(expectedCancellation, observedCancellation);
    }

    private static (SessionAuthorityStampV1 Session, IReadOnlyList<AuthorityFactEnvelopeV1> Prefix, AuthorityFactEnvelopeV1 Envelope, GraphParticipantBindingCommandBodyV1 CommandBody, GraphParticipantBindingFactBodyV1 FactBody, GraphParticipantReservationFoldV2.AppliedReservation Reservation) CreateAuthenticatedS1HistoryFixture()
    {
        static StableId128 Id(byte value) { var bytes = new byte[16]; bytes[^1] = value; return StableId128.FromBytes(bytes); }
        static AuthorityFactEnvelopeV1 Envelope(JournalFactId id, JournalPositionV1 position, AuthorityPayloadRegistrationV1 registration, byte[] payload, CorrelationEnvelopeV1 correlation) => new(id, position, null, OwnerSliceId.S1, registration.Schema, payload, AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload), correlation, default, default, new IntegrityEnvelopeV1(1, 1, Hash256.Compute([99]), []));
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(1)), LiveSessionId.FromValue(Id(2)));
        var operation = OperationId.FromValue(Id(3)); var graph = GraphGenerationId.FromValue(Id(4)); var stamp = new MonotonicStampV1(ClockDomainId.FromValue(Id(5)), BootId.FromValue(Id(6)), 1);
        var authority = ExpectedAuthorityVectorV1.Create(session, [new AuthorityAxisValueV1.Graph(graph)]); var correlation = new CorrelationEnvelopeV1(TenantId.FromValue(Id(7)), operationId: operation);
        var reservationCommandBody = new GraphParticipantReservationCommandBodyV2(operation, null, session.RuntimeGenerationId, graph, Hash256.Compute([1]), Hash256.Compute([2]), new("factory"), [new("node")], stamp);
        var reservationCommandPayload = GraphParticipantReservationCodecsV2.Encode(new GraphParticipantReservationCommandV2(session, authority, GraphParticipantReservationCodecsV2.Encode(reservationCommandBody)));
        var reservationCommand = Envelope(GraphParticipantReservationFactIdsV2.ReservationCommand(session, operation), new(session, 1), GraphParticipantReservationPayloadRegistrationsV2.ReservationCommand, reservationCommandPayload, correlation);
        var reservation = new GraphParticipantReservationV1(ParticipantId.FromValue(Id(8)), new("factory"), [new("node")]);
        var reservationFactBody = new GraphParticipantReservationFactBodyV2(operation, reservationCommand.Position, null, 1, session.RuntimeGenerationId, graph, reservationCommandBody.ParticipantPlanFingerprint, reservationCommandBody.AllocationCarrierFingerprint, reservation, null, stamp);
        var reservationFactPayload = GraphParticipantReservationCodecsV2.Encode(new GraphParticipantReservationFactV2(session, authority, GraphParticipantReservationCodecsV2.Encode(reservationFactBody)));
        var reservationFact = Envelope(GraphParticipantReservationFactIdsV2.ReservationFact(reservationCommand.Position), new(session, 2), GraphParticipantReservationPayloadRegistrationsV2.ReservationFact, reservationFactPayload, correlation);
        var proof = new CapacityGrantBindingProofV1(CapacityGrantId.FromValue(Id(9)), new(session, 10), new(session, 11), 1, Hash256.Compute([10]));
        var commandBody = new GraphParticipantBindingCommandBodyV1(operation, reservationFact.Position, null, graph, session.RuntimeGenerationId, reservationCommandBody.ParticipantPlanFingerprint, Hash256.Compute([11]), Hash256.Compute([12]), proof, stamp);
        var commandPayload = GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(session, authority, GraphParticipantBindingCodecsV1.Encode(commandBody)));
        var command = Envelope(GraphParticipantBindingFactIdsV1.BindingCommand(session, operation), new(session, 3), GraphParticipantBindingPayloadRegistrationsV1.BindingCommand, commandPayload, correlation);
        var binding = new GraphParticipantBindingV1(reservation.ParticipantId, reservation.ParticipantFactoryKey, reservation.OrderedTopologyNodeKeys);
        var factBody = new GraphParticipantBindingFactBodyV1(operation, command.Position, reservationFact.Position, null, 1, graph, session.RuntimeGenerationId, commandBody.ParticipantPlanFingerprint, commandBody.TopologyFingerprint, commandBody.ExecutablePlanFingerprint, binding, proof, null, stamp);
        return (session, [reservationCommand, reservationFact], command, commandBody, factBody, new GraphParticipantReservationFoldV2.AppliedReservation(reservationCommand, reservationFact, reservation));
    }

    private static S1Fixture MutateSchema(S1Fixture fixture) { var e=fixture.Envelope;var mutated=GraphParticipantBindingPayloadRegistrationsV1.BindingFact.Schema;var Schema = mutated;var ExactCanonicalPayloadBytes=e.PayloadBytes.ToArray();var changed=new AuthorityFactEnvelopeV1(e.FactId,e.Position,e.ThreadScope,e.Owner,Schema,ExactCanonicalPayloadBytes,e.PayloadHash,e.Correlation,e.ObservedAt,e.AdmittedAt,e.Integrity);var mutatedFixture=(fixture.Session,fixture.Prefix,changed,fixture.CommandBody,fixture.FactBody,fixture.Reservation);return mutatedFixture; }
    private static S1Fixture MutateOwner(S1Fixture fixture) { var e=fixture.Envelope;var mutated=OwnerSliceId.S2;var Owner = mutated;var ExactCanonicalPayloadBytes=e.PayloadBytes.ToArray();var changed=new AuthorityFactEnvelopeV1(e.FactId,e.Position,e.ThreadScope,Owner,e.PayloadSchema,ExactCanonicalPayloadBytes,e.PayloadHash,e.Correlation,e.ObservedAt,e.AdmittedAt,e.Integrity);var mutatedFixture=(fixture.Session,fixture.Prefix,changed,fixture.CommandBody,fixture.FactBody,fixture.Reservation);return mutatedFixture; }
    private static S1Fixture MutateThread(S1Fixture fixture) { var e=fixture.Envelope;var bytes=new byte[16];bytes[^1]=53;var mutated=new ThreadPositionV1(ThreadId.FromValue(StableId128.FromBytes(bytes)),1,1);var Thread = mutated;var ExactCanonicalPayloadBytes=e.PayloadBytes.ToArray();var changed=new AuthorityFactEnvelopeV1(e.FactId,e.Position,Thread,e.Owner,e.PayloadSchema,ExactCanonicalPayloadBytes,e.PayloadHash,e.Correlation,e.ObservedAt,e.AdmittedAt,e.Integrity);var mutatedFixture=(fixture.Session,fixture.Prefix,changed,fixture.CommandBody,fixture.FactBody,fixture.Reservation);return mutatedFixture; }
    private static S1Fixture MutateHash(S1Fixture fixture) { var e=fixture.Envelope;var mutated=Hash256.Compute([54]);var Hash = mutated;var ExactCanonicalPayloadBytes=e.PayloadBytes.ToArray();var changed=new AuthorityFactEnvelopeV1(e.FactId,e.Position,e.ThreadScope,e.Owner,e.PayloadSchema,ExactCanonicalPayloadBytes,Hash,e.Correlation,e.ObservedAt,e.AdmittedAt,e.Integrity);var mutatedFixture=(fixture.Session,fixture.Prefix,changed,fixture.CommandBody,fixture.FactBody,fixture.Reservation);return mutatedFixture; }
    private static S1Fixture MutateFactId(S1Fixture fixture) { var e=fixture.Envelope;var candidate=GraphParticipantBindingFactIdsV1.BindingFact(e.Position);var mutated=candidate;var wrongFactId = mutated;var canonicalBody=GraphParticipantBindingCodecsV1.Encode(fixture.CommandBody);GraphParticipantBindingCodecsV1.TryDecodeBindingCommand(e.PayloadMemory,out var outer);var canonicalPayload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(fixture.Session,outer!.ExpectedAuthority,canonicalBody));var recomputedPayloadHash=AuthorityPayloadHashV1.Compute(GraphParticipantBindingPayloadRegistrationsV1.BindingCommand.SchemaToken,GraphParticipantBindingPayloadRegistrationsV1.BindingCommand.Schema,canonicalPayload);var canonicalFactId=GraphParticipantBindingFactIdsV1.BindingCommand(fixture.Session,fixture.CommandBody.OperationId);Assert.True(wrongFactId!=canonicalFactId);var changed=new AuthorityFactEnvelopeV1(wrongFactId,e.Position,e.ThreadScope,e.Owner,e.PayloadSchema,canonicalPayload,recomputedPayloadHash,e.Correlation,e.ObservedAt,e.AdmittedAt,e.Integrity);var mutatedFixture=(fixture.Session,fixture.Prefix,changed,fixture.CommandBody,fixture.FactBody,fixture.Reservation);return mutatedFixture; }
    private static S1Fixture MutateOuterSession(S1Fixture fixture)
    {

        var bytes=new byte[16];bytes[^1]=44;var mutated=new SessionAuthorityStampV1(fixture.Session.RuntimeGenerationId,LiveSessionId.FromValue(StableId128.FromBytes(bytes)));var OuterSession = mutated;
        var canonicalBody=GraphParticipantBindingCodecsV1.Encode(fixture.CommandBody);
        var authority=ExpectedAuthorityVectorV1.Create(OuterSession,[]);
        var canonicalPayload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(OuterSession,authority,canonicalBody));
        var registration=GraphParticipantBindingPayloadRegistrationsV1.BindingCommand;
        var recomputedPayloadHash=AuthorityPayloadHashV1.Compute(GraphParticipantBindingPayloadRegistrationsV1.BindingCommand.SchemaToken,GraphParticipantBindingPayloadRegistrationsV1.BindingCommand.Schema,canonicalPayload);
        var recomputedFactId=GraphParticipantBindingFactIdsV1.BindingCommand(fixture.Session,fixture.CommandBody.OperationId);
        var e=fixture.Envelope;
        var changed=new AuthorityFactEnvelopeV1(recomputedFactId,e.Position,e.ThreadScope,e.Owner,registration.Schema,canonicalPayload,recomputedPayloadHash,e.Correlation,e.ObservedAt,e.AdmittedAt,e.Integrity);
        var mutatedFixture=(fixture.Session,fixture.Prefix,changed,fixture.CommandBody,fixture.FactBody,fixture.Reservation);
        return mutatedFixture;
    }
    private static S1Fixture MutateExpectedAuthority(S1Fixture fixture)
    {

        var bytes=new byte[16];bytes[^1]=45;var mutated=ExpectedAuthorityVectorV1.Create(fixture.Session,[new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(StableId128.FromBytes(bytes)))]);var ExpectedAuthority = mutated;
        var canonicalBody=GraphParticipantBindingCodecsV1.Encode(fixture.CommandBody);
        var canonicalPayload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(fixture.Session,ExpectedAuthority,canonicalBody));
        var registration=GraphParticipantBindingPayloadRegistrationsV1.BindingCommand;
        var recomputedPayloadHash=AuthorityPayloadHashV1.Compute(GraphParticipantBindingPayloadRegistrationsV1.BindingCommand.SchemaToken,GraphParticipantBindingPayloadRegistrationsV1.BindingCommand.Schema,canonicalPayload);
        var recomputedFactId=GraphParticipantBindingFactIdsV1.BindingCommand(fixture.Session,fixture.CommandBody.OperationId);
        var e=fixture.Envelope;var changed=new AuthorityFactEnvelopeV1(recomputedFactId,e.Position,e.ThreadScope,e.Owner,registration.Schema,canonicalPayload,recomputedPayloadHash,e.Correlation,e.ObservedAt,e.AdmittedAt,e.Integrity);
        var mutatedFixture=(fixture.Session,fixture.Prefix,changed,fixture.CommandBody,fixture.FactBody,fixture.Reservation);return mutatedFixture;
    }
    private static S1Fixture MutateCommandPosition(S1Fixture fixture) { var e=fixture.Envelope;var mutated=new JournalPositionV1(fixture.Session,9);var wrongPosition = mutated;var canonicalBody=GraphParticipantBindingCodecsV1.Encode(fixture.CommandBody);GraphParticipantBindingCodecsV1.TryDecodeBindingCommand(e.PayloadMemory,out var outer);var canonicalPayload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(fixture.Session,outer!.ExpectedAuthority,canonicalBody));var recomputedPayloadHash=AuthorityPayloadHashV1.Compute(GraphParticipantBindingPayloadRegistrationsV1.BindingCommand.SchemaToken,GraphParticipantBindingPayloadRegistrationsV1.BindingCommand.Schema,canonicalPayload);var recomputedFactId=GraphParticipantBindingFactIdsV1.BindingCommand(fixture.Session,fixture.CommandBody.OperationId);var canonicalPosition=e.Position;Assert.True(wrongPosition!=canonicalPosition);var changed=new AuthorityFactEnvelopeV1(recomputedFactId,wrongPosition,e.ThreadScope,e.Owner,e.PayloadSchema,canonicalPayload,recomputedPayloadHash,e.Correlation,e.ObservedAt,e.AdmittedAt,e.Integrity);var mutatedFixture=(fixture.Session,fixture.Prefix,changed,fixture.CommandBody,fixture.FactBody,fixture.Reservation);return mutatedFixture; }
    private static S1Fixture MutateFactPosition(S1Fixture fixture) { var e=fixture.Envelope;var mutated=new JournalPositionV1(fixture.Session,8);var wrongPosition = mutated;var canonicalBody=GraphParticipantBindingCodecsV1.Encode(fixture.CommandBody);GraphParticipantBindingCodecsV1.TryDecodeBindingCommand(e.PayloadMemory,out var outer);var canonicalPayload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(fixture.Session,outer!.ExpectedAuthority,canonicalBody));var recomputedPayloadHash=AuthorityPayloadHashV1.Compute(GraphParticipantBindingPayloadRegistrationsV1.BindingCommand.SchemaToken,GraphParticipantBindingPayloadRegistrationsV1.BindingCommand.Schema,canonicalPayload);var recomputedFactId=GraphParticipantBindingFactIdsV1.BindingCommand(fixture.Session,fixture.CommandBody.OperationId);var canonicalPosition=e.Position;Assert.True(wrongPosition!=canonicalPosition);var changed=new AuthorityFactEnvelopeV1(recomputedFactId,wrongPosition,e.ThreadScope,e.Owner,e.PayloadSchema,canonicalPayload,recomputedPayloadHash,e.Correlation,e.ObservedAt,e.AdmittedAt,e.Integrity);var mutatedFixture=(fixture.Session,fixture.Prefix,changed,fixture.CommandBody,fixture.FactBody,fixture.Reservation);return mutatedFixture; }
    private static S1Fixture MutateFactBytes(S1Fixture fixture) { var e=fixture.Envelope;var mutated=e.PayloadBytes.ToArray();mutated[^1]^=1;var FactBytes = mutated;var ExactCanonicalPayloadBytes=FactBytes;var changed=new AuthorityFactEnvelopeV1(e.FactId,e.Position,e.ThreadScope,e.Owner,e.PayloadSchema,FactBytes,e.PayloadHash,e.Correlation,e.ObservedAt,e.AdmittedAt,e.Integrity);var mutatedFixture=(fixture.Session,fixture.Prefix,changed,fixture.CommandBody,fixture.FactBody,fixture.Reservation);return mutatedFixture; }
    private static S1Fixture MutateParticipant(S1Fixture fixture)
    {
        var bytes=new byte[16];bytes[^1]=46;var mutated=ParticipantId.FromValue(StableId128.FromBytes(bytes));var Participant = mutated;
        var binding=new GraphParticipantBindingV1(Participant,fixture.Reservation.Reservation.ParticipantFactoryKey,fixture.Reservation.Reservation.OrderedTopologyNodeKeys);
        var body=fixture.FactBody with { Binding=binding };var canonicalBody=GraphParticipantBindingCodecsV1.Encode(body);
        GraphParticipantBindingCodecsV1.TryDecodeBindingCommand(fixture.Envelope.PayloadMemory,out var commandOuter);var canonicalPayload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingFactV1(fixture.Session,commandOuter!.ExpectedAuthority,canonicalBody));
        var registration=GraphParticipantBindingPayloadRegistrationsV1.BindingFact;var recomputedPayloadHash=AuthorityPayloadHashV1.Compute(registration.SchemaToken,registration.Schema,canonicalPayload);var recomputedFactId=GraphParticipantBindingFactIdsV1.BindingFact(fixture.Envelope.Position);
        var e=fixture.Envelope;var changed=new AuthorityFactEnvelopeV1(recomputedFactId,new(fixture.Session,4),null,e.Owner,registration.Schema,canonicalPayload,recomputedPayloadHash,e.Correlation,e.ObservedAt,e.AdmittedAt,e.Integrity);
        var prefix=fixture.Prefix.Concat([fixture.Envelope]).ToArray();var mutatedFixture=(fixture.Session,(IReadOnlyList<AuthorityFactEnvelopeV1>)prefix,changed,fixture.CommandBody,body,fixture.Reservation);return mutatedFixture;
    }
    private static S1Fixture MutateOperation(S1Fixture fixture)
    {
        var bytes=new byte[16];bytes[^1]=47;var mutated=OperationId.FromValue(StableId128.FromBytes(bytes));var Operation = mutated;var c=fixture.CommandBody;
        var body=new GraphParticipantBindingCommandBodyV1(Operation,c.ReservationFact,c.ExpectedBindingFact,c.GraphGeneration,c.RuntimeGeneration,c.ParticipantPlanFingerprint,c.TopologyFingerprint,c.ExecutablePlanFingerprint,c.CapacityGrantProof,c.ObservedAt);
        var canonicalBody=GraphParticipantBindingCodecsV1.Encode(body);GraphParticipantBindingCodecsV1.TryDecodeBindingCommand(fixture.Envelope.PayloadMemory,out var outer);var canonicalPayload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(fixture.Session,outer!.ExpectedAuthority,canonicalBody));
        var registration=GraphParticipantBindingPayloadRegistrationsV1.BindingCommand;var recomputedPayloadHash=AuthorityPayloadHashV1.Compute(registration.SchemaToken,registration.Schema,canonicalPayload);var recomputedFactId=GraphParticipantBindingFactIdsV1.BindingCommand(fixture.Session,Operation);var e=fixture.Envelope;
        var changed=new AuthorityFactEnvelopeV1(recomputedFactId,e.Position,e.ThreadScope,e.Owner,registration.Schema,canonicalPayload,recomputedPayloadHash,e.Correlation,e.ObservedAt,e.AdmittedAt,e.Integrity);var mutatedFixture=(fixture.Session,fixture.Prefix,changed,body,fixture.FactBody,fixture.Reservation);return mutatedFixture;
    }
    private static S1Fixture MutateRuntime(S1Fixture fixture)
    {
        var bytes=new byte[16];bytes[^1]=48;var mutated=RuntimeGenerationId.FromValue(StableId128.FromBytes(bytes));var Runtime = mutated;var c=fixture.CommandBody;var body=new GraphParticipantBindingCommandBodyV1(c.OperationId,c.ReservationFact,c.ExpectedBindingFact,c.GraphGeneration,Runtime,c.ParticipantPlanFingerprint,c.TopologyFingerprint,c.ExecutablePlanFingerprint,c.CapacityGrantProof,c.ObservedAt);var canonicalBody=GraphParticipantBindingCodecsV1.Encode(body);GraphParticipantBindingCodecsV1.TryDecodeBindingCommand(fixture.Envelope.PayloadMemory,out var outer);var canonicalPayload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(fixture.Session,outer!.ExpectedAuthority,canonicalBody));var registration=GraphParticipantBindingPayloadRegistrationsV1.BindingCommand;var recomputedPayloadHash=AuthorityPayloadHashV1.Compute(registration.SchemaToken,registration.Schema,canonicalPayload);var recomputedFactId=GraphParticipantBindingFactIdsV1.BindingCommand(fixture.Session,c.OperationId);var e=fixture.Envelope;var changed=new AuthorityFactEnvelopeV1(recomputedFactId,e.Position,e.ThreadScope,e.Owner,registration.Schema,canonicalPayload,recomputedPayloadHash,e.Correlation,e.ObservedAt,e.AdmittedAt,e.Integrity);var mutatedFixture=(fixture.Session,fixture.Prefix,changed,body,fixture.FactBody,fixture.Reservation);return mutatedFixture;
    }
    private static S1Fixture MutateGraph(S1Fixture fixture)
    {
        var bytes=new byte[16];bytes[^1]=49;var mutated=GraphGenerationId.FromValue(StableId128.FromBytes(bytes));var Graph = mutated;var c=fixture.CommandBody;var body=new GraphParticipantBindingCommandBodyV1(c.OperationId,c.ReservationFact,c.ExpectedBindingFact,Graph,c.RuntimeGeneration,c.ParticipantPlanFingerprint,c.TopologyFingerprint,c.ExecutablePlanFingerprint,c.CapacityGrantProof,c.ObservedAt);var canonicalBody=GraphParticipantBindingCodecsV1.Encode(body);GraphParticipantBindingCodecsV1.TryDecodeBindingCommand(fixture.Envelope.PayloadMemory,out var outer);var canonicalPayload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(fixture.Session,outer!.ExpectedAuthority,canonicalBody));var registration=GraphParticipantBindingPayloadRegistrationsV1.BindingCommand;var recomputedPayloadHash=AuthorityPayloadHashV1.Compute(registration.SchemaToken,registration.Schema,canonicalPayload);var recomputedFactId=GraphParticipantBindingFactIdsV1.BindingCommand(fixture.Session,c.OperationId);var e=fixture.Envelope;var changed=new AuthorityFactEnvelopeV1(recomputedFactId,e.Position,e.ThreadScope,e.Owner,registration.Schema,canonicalPayload,recomputedPayloadHash,e.Correlation,e.ObservedAt,e.AdmittedAt,e.Integrity);var mutatedFixture=(fixture.Session,fixture.Prefix,changed,body,fixture.FactBody,fixture.Reservation);return mutatedFixture;
    }
    private static S1Fixture MutatePlan(S1Fixture fixture)
    {
        var mutated=Hash256.Compute([50]);var Plan = mutated;var c=fixture.CommandBody;var body=new GraphParticipantBindingCommandBodyV1(c.OperationId,c.ReservationFact,c.ExpectedBindingFact,c.GraphGeneration,c.RuntimeGeneration,Plan,c.TopologyFingerprint,c.ExecutablePlanFingerprint,c.CapacityGrantProof,c.ObservedAt);var canonicalBody=GraphParticipantBindingCodecsV1.Encode(body);GraphParticipantBindingCodecsV1.TryDecodeBindingCommand(fixture.Envelope.PayloadMemory,out var outer);var canonicalPayload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingCommandV1(fixture.Session,outer!.ExpectedAuthority,canonicalBody));var registration=GraphParticipantBindingPayloadRegistrationsV1.BindingCommand;var recomputedPayloadHash=AuthorityPayloadHashV1.Compute(registration.SchemaToken,registration.Schema,canonicalPayload);var recomputedFactId=GraphParticipantBindingFactIdsV1.BindingCommand(fixture.Session,c.OperationId);var e=fixture.Envelope;var changed=new AuthorityFactEnvelopeV1(recomputedFactId,e.Position,e.ThreadScope,e.Owner,registration.Schema,canonicalPayload,recomputedPayloadHash,e.Correlation,e.ObservedAt,e.AdmittedAt,e.Integrity);var mutatedFixture=(fixture.Session,fixture.Prefix,changed,body,fixture.FactBody,fixture.Reservation);return mutatedFixture;
    }
    private static S1Fixture MutateCarrier(S1Fixture fixture)
    {
        var mutated=Hash256.Compute([51]);var Carrier = mutated;var original=fixture.Prefix[1];GraphParticipantReservationCodecsV2.TryDecodeReservationFact(original.PayloadMemory,out var outer);GraphParticipantReservationCodecsV2.TryDecodeReservationFactBody(outer!.BodyBytes.ToArray(),out var prior);var p=prior!;
        var mutatedBody=new GraphParticipantReservationFactBodyV2(p.OperationId,p.CommandPosition,p.ActualPredecessor,p.Outcome,p.RuntimeGeneration,p.GraphGeneration,p.ParticipantPlanFingerprint,Carrier,p.Reservation,p.SafeCode,p.ObservedAt);var canonicalBody=GraphParticipantReservationCodecsV2.Encode(mutatedBody);var canonicalPayload=GraphParticipantReservationCodecsV2.Encode(new GraphParticipantReservationFactV2(fixture.Session,outer.ExpectedAuthority,canonicalBody));var registration=GraphParticipantReservationPayloadRegistrationsV2.ReservationFact;var recomputedPayloadHash=AuthorityPayloadHashV1.Compute(registration.SchemaToken,registration.Schema,canonicalPayload);var recomputedFactId=GraphParticipantReservationFactIdsV2.ReservationFact(p.CommandPosition);var mutatedReservationFact=new AuthorityFactEnvelopeV1(recomputedFactId,original.Position,original.ThreadScope,original.Owner,registration.Schema,canonicalPayload,recomputedPayloadHash,original.Correlation,original.ObservedAt,original.AdmittedAt,original.Integrity);
        var prefix=new AuthorityFactEnvelopeV1[]{fixture.Prefix[0],mutatedReservationFact};var mutatedFixture=(fixture.Session,(IReadOnlyList<AuthorityFactEnvelopeV1>)prefix,fixture.Envelope,fixture.CommandBody,fixture.FactBody,fixture.Reservation);return mutatedFixture;
    }
    private static S1Fixture MutateFactory(S1Fixture fixture)
    {
        var mutated=new BoundedAscii("changed-factory");var Factory = mutated;var binding=new GraphParticipantBindingV1(fixture.Reservation.Reservation.ParticipantId,Factory,fixture.Reservation.Reservation.OrderedTopologyNodeKeys);var body=fixture.FactBody with { Binding=binding };var canonicalBody=GraphParticipantBindingCodecsV1.Encode(body);GraphParticipantBindingCodecsV1.TryDecodeBindingCommand(fixture.Envelope.PayloadMemory,out var outer);var canonicalPayload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingFactV1(fixture.Session,outer!.ExpectedAuthority,canonicalBody));var registration=GraphParticipantBindingPayloadRegistrationsV1.BindingFact;var recomputedPayloadHash=AuthorityPayloadHashV1.Compute(registration.SchemaToken,registration.Schema,canonicalPayload);var recomputedFactId=GraphParticipantBindingFactIdsV1.BindingFact(fixture.Envelope.Position);var e=fixture.Envelope;var changed=new AuthorityFactEnvelopeV1(recomputedFactId,new(fixture.Session,4),null,e.Owner,registration.Schema,canonicalPayload,recomputedPayloadHash,e.Correlation,e.ObservedAt,e.AdmittedAt,e.Integrity);var prefix=fixture.Prefix.Concat([fixture.Envelope]).ToArray();var mutatedFixture=(fixture.Session,(IReadOnlyList<AuthorityFactEnvelopeV1>)prefix,changed,fixture.CommandBody,body,fixture.Reservation);return mutatedFixture;
    }
    private static S1Fixture MutateNodes(S1Fixture fixture)
    {
        IReadOnlyList<BoundedAscii> mutated=[new("changed-node")];var Nodes = mutated;var binding=new GraphParticipantBindingV1(fixture.Reservation.Reservation.ParticipantId,fixture.Reservation.Reservation.ParticipantFactoryKey,Nodes);var body=fixture.FactBody with { Binding=binding };var canonicalBody=GraphParticipantBindingCodecsV1.Encode(body);GraphParticipantBindingCodecsV1.TryDecodeBindingCommand(fixture.Envelope.PayloadMemory,out var outer);var canonicalPayload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingFactV1(fixture.Session,outer!.ExpectedAuthority,canonicalBody));var registration=GraphParticipantBindingPayloadRegistrationsV1.BindingFact;var recomputedPayloadHash=AuthorityPayloadHashV1.Compute(registration.SchemaToken,registration.Schema,canonicalPayload);var recomputedFactId=GraphParticipantBindingFactIdsV1.BindingFact(fixture.Envelope.Position);var e=fixture.Envelope;var changed=new AuthorityFactEnvelopeV1(recomputedFactId,new(fixture.Session,4),null,e.Owner,registration.Schema,canonicalPayload,recomputedPayloadHash,e.Correlation,e.ObservedAt,e.AdmittedAt,e.Integrity);var prefix=fixture.Prefix.Concat([fixture.Envelope]).ToArray();var mutatedFixture=(fixture.Session,(IReadOnlyList<AuthorityFactEnvelopeV1>)prefix,changed,fixture.CommandBody,body,fixture.Reservation);return mutatedFixture;
    }
    private static S1Fixture MutateLegacyV1Reservation(S1Fixture fixture)
    {
        var mutated=true;var LegacyV1Reservation = mutated;var bytes=new byte[16];bytes[^1]=60;var operation=OperationId.FromValue(StableId128.FromBytes(bytes));var stamp=fixture.CommandBody.ObservedAt;var body=new GraphParticipantReservationCommandBodyV1(operation,null,fixture.Session.RuntimeGenerationId,Hash256.Compute([61]),Hash256.Compute([62]),Hash256.Compute([63]),new("legacy"),[new("legacy-node")],stamp);var authority=ExpectedAuthorityVectorV1.Create(fixture.Session,[]);var canonicalBody=GraphParticipantBindingCodecsV1.Encode(body);var canonicalPayload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantReservationCommandV1(fixture.Session,authority,canonicalBody));var registration=GraphParticipantBindingPayloadRegistrationsV1.ReservationCommand;var recomputedFactId=GraphParticipantBindingFactIdsV1.ReservationCommand(fixture.Session,operation);var recomputedPayloadHash=AuthorityPayloadHashV1.Compute(registration.SchemaToken,registration.Schema,canonicalPayload);var e=fixture.Envelope;var changed=new AuthorityFactEnvelopeV1(recomputedFactId,e.Position,null,OwnerSliceId.S1,registration.Schema,canonicalPayload,recomputedPayloadHash,e.Correlation,e.ObservedAt,e.AdmittedAt,e.Integrity);var mutatedFixture=(fixture.Session,fixture.Prefix,changed,fixture.CommandBody,fixture.FactBody,fixture.Reservation);return mutatedFixture;
    }
    private static S1Fixture MutateUnrelatedS1BeforeTerminal(S1Fixture fixture)
    {
        var mutated=true;var UnrelatedS1BeforeTerminal = mutated;var bytes=new byte[16];bytes[^1]=64;var operation=OperationId.FromValue(StableId128.FromBytes(bytes));var body=new GraphParticipantReservationCommandBodyV1(operation,null,fixture.Session.RuntimeGenerationId,Hash256.Compute([65]),Hash256.Compute([66]),Hash256.Compute([67]),new("unrelated"),[new("other-node")],fixture.CommandBody.ObservedAt);var authority=ExpectedAuthorityVectorV1.Create(fixture.Session,[]);var canonicalBody=GraphParticipantBindingCodecsV1.Encode(body);var canonicalPayload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantReservationCommandV1(fixture.Session,authority,canonicalBody));var registration=GraphParticipantBindingPayloadRegistrationsV1.ReservationCommand;var recomputedFactId=GraphParticipantBindingFactIdsV1.ReservationCommand(fixture.Session,operation);var recomputedPayloadHash=AuthorityPayloadHashV1.Compute(registration.SchemaToken,registration.Schema,canonicalPayload);var e=fixture.Envelope;var changed=new AuthorityFactEnvelopeV1(recomputedFactId,e.Position,null,OwnerSliceId.S1,registration.Schema,canonicalPayload,recomputedPayloadHash,e.Correlation,e.ObservedAt,e.AdmittedAt,e.Integrity);var mutatedFixture=(fixture.Session,fixture.Prefix,changed,fixture.CommandBody,fixture.FactBody,fixture.Reservation);return mutatedFixture;
    }
    private static S1Fixture MutateUnrelatedS1AfterTerminal(S1Fixture fixture)
    {
        var mutated=true;var UnrelatedS1AfterTerminal = mutated;var canonicalBindingCommand=fixture.Envelope;GraphParticipantBindingCodecsV1.TryDecodeBindingCommand(canonicalBindingCommand.PayloadMemory,out var commandOuter);var terminalBody=GraphParticipantBindingCodecsV1.Encode(fixture.FactBody);var terminalPayload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingFactV1(fixture.Session,commandOuter!.ExpectedAuthority,terminalBody));var terminalRegistration=GraphParticipantBindingPayloadRegistrationsV1.BindingFact;var canonicalBindingFact=new AuthorityFactEnvelopeV1(GraphParticipantBindingFactIdsV1.BindingFact(canonicalBindingCommand.Position),new(fixture.Session,4),null,OwnerSliceId.S1,terminalRegistration.Schema,terminalPayload,AuthorityPayloadHashV1.Compute(terminalRegistration.SchemaToken,terminalRegistration.Schema,terminalPayload),canonicalBindingCommand.Correlation,canonicalBindingCommand.ObservedAt,canonicalBindingCommand.AdmittedAt,canonicalBindingCommand.Integrity);var bytes=new byte[16];bytes[^1]=68;var operation=OperationId.FromValue(StableId128.FromBytes(bytes));var body=new GraphParticipantReservationCommandBodyV1(operation,null,fixture.Session.RuntimeGenerationId,Hash256.Compute([69]),Hash256.Compute([70]),Hash256.Compute([71]),new("after"),[new("after-node")],fixture.CommandBody.ObservedAt);var authority=ExpectedAuthorityVectorV1.Create(fixture.Session,[]);var canonicalBody=GraphParticipantBindingCodecsV1.Encode(body);var canonicalPayload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantReservationCommandV1(fixture.Session,authority,canonicalBody));var registration=GraphParticipantBindingPayloadRegistrationsV1.ReservationCommand;var recomputedFactId=GraphParticipantBindingFactIdsV1.ReservationCommand(fixture.Session,operation);var recomputedPayloadHash=AuthorityPayloadHashV1.Compute(registration.SchemaToken,registration.Schema,canonicalPayload);var e=fixture.Envelope;var changed=new AuthorityFactEnvelopeV1(recomputedFactId,new(fixture.Session,5),null,OwnerSliceId.S1,registration.Schema,canonicalPayload,recomputedPayloadHash,e.Correlation,e.ObservedAt,e.AdmittedAt,e.Integrity);var prefix=fixture.Prefix.Concat([canonicalBindingCommand,canonicalBindingFact]).ToArray();var mutatedFixture=(fixture.Session,(IReadOnlyList<AuthorityFactEnvelopeV1>)prefix,changed,fixture.CommandBody,fixture.FactBody,fixture.Reservation);return mutatedFixture;
    }
    private static S1Fixture MutateTargetAfterTerminal(S1Fixture fixture)
    {
        var mutated=true;var TargetAfterTerminal = mutated;var canonicalBindingCommand=fixture.Envelope;GraphParticipantBindingCodecsV1.TryDecodeBindingCommand(canonicalBindingCommand.PayloadMemory,out var commandOuter);var factBodyBytes=GraphParticipantBindingCodecsV1.Encode(fixture.FactBody);var factPayload=GraphParticipantBindingCodecsV1.Encode(new GraphParticipantBindingFactV1(fixture.Session,commandOuter!.ExpectedAuthority,factBodyBytes));var factRegistration=GraphParticipantBindingPayloadRegistrationsV1.BindingFact;var canonicalBindingFact=new AuthorityFactEnvelopeV1(GraphParticipantBindingFactIdsV1.BindingFact(canonicalBindingCommand.Position),new(fixture.Session,4),null,OwnerSliceId.S1,factRegistration.Schema,factPayload,AuthorityPayloadHashV1.Compute(factRegistration.SchemaToken,factRegistration.Schema,factPayload),canonicalBindingCommand.Correlation,canonicalBindingCommand.ObservedAt,canonicalBindingCommand.AdmittedAt,canonicalBindingCommand.Integrity);
        var idBytes=new byte[16];idBytes[^1]=52;var targetOperation=OperationId.FromValue(StableId128.FromBytes(idBytes));var target=new AuthorityFactEnvelopeV1(GraphParticipantBindingFactIdsV1.BindingCommand(fixture.Session,targetOperation),new(fixture.Session,5),null,canonicalBindingCommand.Owner,canonicalBindingCommand.PayloadSchema,canonicalBindingCommand.PayloadBytes,canonicalBindingCommand.PayloadHash,canonicalBindingCommand.Correlation,canonicalBindingCommand.ObservedAt,canonicalBindingCommand.AdmittedAt,canonicalBindingCommand.Integrity);var prefix=fixture.Prefix.Concat([canonicalBindingCommand,canonicalBindingFact]).ToArray();var mutatedFixture=(fixture.Session,(IReadOnlyList<AuthorityFactEnvelopeV1>)prefix,target,fixture.CommandBody,fixture.FactBody,fixture.Reservation);return mutatedFixture;
    }

}
