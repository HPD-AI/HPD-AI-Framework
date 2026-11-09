using System.Formats.Cbor;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphMediaPhysicalReleaseFoldTests
{
    [Fact]
    public void Unrelated_records_are_counted_and_ignored()
    {
        var f = Fixture();
        var fold = GraphMediaPhysicalReleaseFold.Create(f.Session, f.Residence.ResidenceId, f.Registry);
        var unrelated = Initialization(f, 1);
        Assert.IsType<GraphMediaPhysicalReleaseFoldApplyResult.Ignored>(fold.Apply(unrelated));
        var result = Assert.IsType<GraphMediaPhysicalReleaseFoldResult.NotFound>(fold.Complete());
        Assert.Equal(1UL, result.RecordCount);
        Assert.True(result.TotalCanonicalRecordBytes > 0);
    }

    [Fact]
    public void Command_only_and_each_terminal_outcome_are_exact()
    {
        var commandOnly = Fixture();
        var fold = GraphMediaPhysicalReleaseFold.Create(commandOnly.Session, commandOnly.Residence.ResidenceId, commandOnly.Registry);
        var command = Command(commandOnly, 1, commandOnly.Operation, null);
        Assert.IsType<GraphMediaPhysicalReleaseFoldApplyResult.Applied>(fold.Apply(command));
        Assert.IsType<GraphMediaPhysicalReleaseFoldResult.CommandOnly>(fold.Complete());

        foreach (var outcome in Enum.GetValues<GraphMediaPhysicalReleaseOutcome>())
        {
            var f = Fixture(); fold = GraphMediaPhysicalReleaseFold.Create(f.Session, f.Residence.ResidenceId, f.Registry);
            command = Command(f, 1, f.Operation, null); fold.Apply(command);
            var fact = Fact(f, command, 2, outcome);
            Assert.IsType<GraphMediaPhysicalReleaseFoldApplyResult.Applied>(fold.Apply(fact));
            var completed = fold.Complete();
            Assert.Equal(outcome switch
            {
                GraphMediaPhysicalReleaseOutcome.Released => typeof(GraphMediaPhysicalReleaseFoldResult.Released),
                GraphMediaPhysicalReleaseOutcome.Unknown => typeof(GraphMediaPhysicalReleaseFoldResult.Unknown),
                _ => typeof(GraphMediaPhysicalReleaseFoldResult.Rejected)
            }, completed.GetType());
        }
    }

    [Fact]
    public void Every_command_fact_join_fails_closed()
    {
        AssertJoinInvalid((f, command) => Fact(f, command, 2, GraphMediaPhysicalReleaseOutcome.Released, requestHash: Hash(99)));
        AssertJoinInvalid((f, command) => Fact(f, command, 2, GraphMediaPhysicalReleaseOutcome.Released, residenceId: Id(98)));
        AssertJoinInvalid((f, command) => Fact(f, command, 2, GraphMediaPhysicalReleaseOutcome.Released,
            grantId: CapacityGrantId.FromValue(Id(97))));
        AssertJoinInvalid((f, command) => Fact(f, command, 2, GraphMediaPhysicalReleaseOutcome.Released,
            currentFact: new JournalPositionV1(f.Session, 97)));
        AssertJoinInvalid((f, command) => Fact(f, command, 2, GraphMediaPhysicalReleaseOutcome.Released,
            assignment: new GraphMediaCapacityAssignment(new CapacityChargeV1(new(3), f.Residence.Assignment.Charge.Scope,
                2, f.Residence.Assignment.Charge.Purpose, f.Residence.Assignment.Charge.Window), GraphMediaRepresentationArm.ResidentBytes)));
        AssertJoinInvalid((f, command) => Fact(f, command, 2, GraphMediaPhysicalReleaseOutcome.Released,
            authority: ExpectedAuthorityVectorV1.Create(f.Session, [new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(Id(96)))])));
        AssertJoinInvalid((f, command) => Fact(f, command, 2, GraphMediaPhysicalReleaseOutcome.Released,
            correlation: new CorrelationEnvelopeV1(TenantId.FromValue(Id(95)), operationId: f.Operation)));
        AssertJoinInvalid((f, command) => Fact(f, command, 2, GraphMediaPhysicalReleaseOutcome.Released,
            envelopeObserved: new UtcInstant(1)));
        AssertJoinInvalid((f, command) => Fact(f, command, 2, GraphMediaPhysicalReleaseOutcome.Released,
            bodyObserved: new MonotonicStampV1(f.Stamp.ClockDomainId, f.Stamp.BootId, 2)));

        var f = Fixture(); var fold = GraphMediaPhysicalReleaseFold.Create(f.Session, f.Residence.ResidenceId, f.Registry);
        var orphan = Fact(f, Command(f, 3, f.Operation, null), 1, GraphMediaPhysicalReleaseOutcome.Unknown);
        var invalid = Assert.IsType<GraphMediaPhysicalReleaseFoldApplyResult.InvalidHistory>(fold.Apply(orphan));
        Assert.Equal("fact-without-command", invalid.SafeCode.ToString());
    }

    [Fact]
    public void Duplicate_predecessor_singleton_and_post_terminal_rules_are_closed()
    {
        var f = Fixture();
        var fold = GraphMediaPhysicalReleaseFold.Create(f.Session, f.Residence.ResidenceId, f.Registry);
        var command = Command(f, 1, f.Operation, null);
        fold.Apply(command);
        var duplicate = Assert.IsType<GraphMediaPhysicalReleaseFoldApplyResult.Applied>(fold.Apply(command));
        Assert.True(duplicate.Duplicate);
        var second = Command(f, 2, OperationId.FromValue(Id(40)), null);
        Assert.Equal("predecessor-conflict", Assert.IsType<GraphMediaPhysicalReleaseFoldApplyResult.InvalidHistory>(fold.Apply(second)).SafeCode.ToString());

        f = Fixture(); fold = GraphMediaPhysicalReleaseFold.Create(f.Session, f.Residence.ResidenceId, f.Registry);
        command = Command(f, 1, f.Operation, null); fold.Apply(command);
        var terminal = Fact(f, command, 2, GraphMediaPhysicalReleaseOutcome.Released); fold.Apply(terminal);
        Assert.Equal("post-terminal-record", Assert.IsType<GraphMediaPhysicalReleaseFoldApplyResult.InvalidHistory>(fold.Apply(Command(f, 3, OperationId.FromValue(Id(41)), terminal.Position))).SafeCode.ToString());

        f = Fixture(); fold = GraphMediaPhysicalReleaseFold.Create(f.Session, f.Residence.ResidenceId, f.Registry);
        command = Command(f, 1, f.Operation, null); fold.Apply(command);
        var rejected = Fact(f, command, 2, GraphMediaPhysicalReleaseOutcome.Rejected); fold.Apply(rejected);
        Assert.IsType<GraphMediaPhysicalReleaseFoldApplyResult.Applied>(fold.Apply(Command(f, 3, OperationId.FromValue(Id(42)), rejected.Position)));
    }

    private static AuthorityFactEnvelopeV1 Command(F f, long sequence, OperationId operation, JournalPositionV1? predecessor)
    {
        var body = new GraphMediaPhysicalReleaseCommandBody(operation, f.Residence, f.Owner, f.Work, null, predecessor, f.Stamp);
        var payload = GraphMediaPhysicalReleaseCodecs.EncodeOuter(new(f.Session, f.Authority,
            GraphMediaPhysicalReleaseCodecs.EncodeCommandBody(body)));
        return Envelope(f, sequence, GraphMediaPhysicalReleaseFactIds.Command(f.Session, operation),
            GraphMediaPhysicalReleasePayloadRegistrations.Command, payload, operation);
    }

    private static AuthorityFactEnvelopeV1 Fact(F f, AuthorityFactEnvelopeV1 command, long sequence,
        GraphMediaPhysicalReleaseOutcome outcome, Hash256? requestHash = null, StableId128? residenceId = null,
        CapacityGrantId? grantId = null, JournalPositionV1? currentFact = null,
        GraphMediaCapacityAssignment? assignment = null, ExpectedAuthorityVectorV1? authority = null,
        CorrelationEnvelopeV1? correlation = null, UtcInstant? envelopeObserved = null,
        MonotonicStampV1? bodyObserved = null)
    {
        var body = new GraphMediaPhysicalReleaseFactBody(command.Position, residenceId ?? f.Residence.ResidenceId,
            requestHash ?? f.Residence.RequestHash, grantId ?? f.Residence.GrantId, currentFact ?? f.Residence.CurrentFact,
            assignment ?? f.Residence.Assignment, outcome, outcome == GraphMediaPhysicalReleaseOutcome.Released ? Hash(50) : null,
            outcome == GraphMediaPhysicalReleaseOutcome.Rejected ? new("work-encumbered") : null, bodyObserved ?? f.Stamp);
        var payload = GraphMediaPhysicalReleaseCodecs.EncodeOuter(new(f.Session, authority ?? f.Authority,
            GraphMediaPhysicalReleaseCodecs.EncodeFactBody(body)));
        return Envelope(f, sequence, GraphMediaPhysicalReleaseFactIds.Fact(command.Position),
            GraphMediaPhysicalReleasePayloadRegistrations.Fact, payload, f.Operation, correlation, envelopeObserved);
    }

    private static AuthorityFactEnvelopeV1 Initialization(F f, long sequence)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(3); writer.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(writer, f.Session);
        writer.WriteUInt64(2); Span<byte> graph = stackalloc byte[16]; f.Graph.TryWriteBytes(graph); writer.WriteByteString(graph);
        writer.WriteUInt64(3); writer.WriteUInt64((ushort)OwnerSliceId.S2); writer.WriteEndMap();
        var payload = writer.Encode();
        var registration = new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Graph);
        return Envelope(f, sequence, JournalFactId.FromValue(Id(60)), registration, payload, f.Operation);
    }

    private static AuthorityFactEnvelopeV1 Envelope(F f, long sequence, JournalFactId factId,
        AuthorityPayloadRegistrationV1 registration, byte[] payload, OperationId operation,
        CorrelationEnvelopeV1? correlation = null, UtcInstant? observedAt = null) => new(factId,
        new(f.Session, sequence), null, registration.Owner, registration.Schema, payload,
        AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload),
        correlation ?? new CorrelationEnvelopeV1(TenantId.FromValue(Id(22)), operationId: operation), observedAt ?? default, default,
        new IntegrityEnvelopeV1(1, 1, Hash(61), []));

    private static void AssertJoinInvalid(Func<F, AuthorityFactEnvelopeV1, AuthorityFactEnvelopeV1> mutation)
    {
        var f = Fixture(); var fold = GraphMediaPhysicalReleaseFold.Create(f.Session, f.Residence.ResidenceId, f.Registry);
        var command = Command(f, 1, f.Operation, null); fold.Apply(command);
        var invalid = Assert.IsType<GraphMediaPhysicalReleaseFoldApplyResult.InvalidHistory>(fold.Apply(mutation(f, command)));
        Assert.Equal("command-fact-join-invalid", invalid.SafeCode.ToString());
    }

    private static F Fixture()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(1)), LiveSessionId.FromValue(Id(2)));
        var graph = GraphGenerationId.FromValue(Id(3)); var operation = OperationId.FromValue(Id(4));
        var authority = ExpectedAuthorityVectorV1.Create(session, [new AuthorityAxisValueV1.Graph(graph)]);
        var charge = new CapacityChargeV1(new(3), new(TenantId.FromValue(Id(5)), SessionId.FromValue(Id(6)),
            new CapacitySubjectV1.Operation(operation)), 1, CapacityPurposeId.FromValue(Id(7)), new CapacityChargeWindowV1.NoWindow());
        var assignment = new GraphMediaCapacityAssignment(charge, GraphMediaRepresentationArm.ResidentBytes);
        var residence = new GraphMediaReleaseResidenceProof(OperationId.FromValue(Id(8)), Hash(8), Id(9), Id(10), graph,
            new("node"), ParticipantId.FromValue(Id(11)), new(session, 1), new(session, 2), new(session, 3), new(session, 4),
            CapacityGrantId.FromValue(Id(12)), new(session, 5), new(session, 6), Hash(13), Hash(14), Hash(15), assignment,
            GraphMediaResidenceClass.Controlled, GraphMediaResidenceState.Visible);
        var owner = new GraphMediaOwnerReleaseProof(residence.OwnerId, OperationId.FromValue(Id(16)), Hash(16),
            GraphMediaOwnerTransitionResult.Disposed, Hash(17), 1, Hash(18));
        var work = new GraphMediaWorkReleaseProof(Hash(19), GraphMediaReleaseEligibility.Eligible, 1, 1);
        var stamp = new MonotonicStampV1(ClockDomainId.FromValue(Id(20)), BootId.FromValue(Id(21)), 1);
        var registry = new AuthorityPayloadAdmissionRegistryV1([GraphMediaPhysicalReleasePayloadRegistrations.Command,
            GraphMediaPhysicalReleasePayloadRegistrations.Fact,
            new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Graph)]);
        return new(session, graph, operation, authority, residence, owner, work, stamp, registry);
    }

    private sealed record F(SessionAuthorityStampV1 Session, GraphGenerationId Graph, OperationId Operation,
        ExpectedAuthorityVectorV1 Authority, GraphMediaReleaseResidenceProof Residence,
        GraphMediaOwnerReleaseProof Owner, GraphMediaWorkReleaseProof Work, MonotonicStampV1 Stamp,
        AuthorityPayloadAdmissionRegistryV1 Registry);
    private static StableId128 Id(byte value) { Span<byte> bytes = stackalloc byte[16]; bytes.Fill(value); return StableId128.FromBytes(bytes); }
    private static Hash256 Hash(byte value) => Hash256.Compute([value]);
}
