using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphMediaPhysicalReleaseWireTests
{
    [Fact]
    public void Command_and_fact_round_trip_canonically()
    {
        var f = Fixture();
        var commandBytes = GraphMediaPhysicalReleaseCodecs.EncodeCommandBody(f.Command);
        Assert.True(GraphMediaPhysicalReleaseCodecs.TryDecodeCommandBody(commandBytes, out var command));
        Assert.Equal(commandBytes, GraphMediaPhysicalReleaseCodecs.EncodeCommandBody(command!));
        Assert.Equal(f.Residence.GraphGeneration, command!.Residence.GraphGeneration);

        var fact = Fact(f, GraphMediaPhysicalReleaseOutcome.Released, Hash(40), null);
        var factBytes = GraphMediaPhysicalReleaseCodecs.EncodeFactBody(fact);
        Assert.True(GraphMediaPhysicalReleaseCodecs.TryDecodeFactBody(factBytes, out var decoded));
        Assert.Equal(factBytes, GraphMediaPhysicalReleaseCodecs.EncodeFactBody(decoded!));

        var outer = new GraphMediaPhysicalReleaseOuter(f.Session, f.Authority, commandBytes);
        var outerBytes = GraphMediaPhysicalReleaseCodecs.EncodeOuter(outer);
        Assert.True(GraphMediaPhysicalReleaseCodecs.TryDecodeOuter(outerBytes, out var decodedOuter));
        Assert.Equal(outerBytes, GraphMediaPhysicalReleaseCodecs.EncodeOuter(decodedOuter!));
    }

    [Fact]
    public void Constructors_own_bytes_and_close_outcome_invariants()
    {
        var f = Fixture();
        Assert.Throws<ArgumentException>(() => Fact(f, GraphMediaPhysicalReleaseOutcome.Released, null, null));
        Assert.Throws<ArgumentException>(() => Fact(f, GraphMediaPhysicalReleaseOutcome.Unknown, Hash(1), null));
        Assert.Throws<ArgumentException>(() => Fact(f, GraphMediaPhysicalReleaseOutcome.Rejected, null, new("not-authorized")));
        Assert.NotNull(Fact(f, GraphMediaPhysicalReleaseOutcome.Unknown, null, null));
        Assert.NotNull(Fact(f, GraphMediaPhysicalReleaseOutcome.Rejected, null, new("work-encumbered")));

        var body = GraphMediaPhysicalReleaseCodecs.EncodeCommandBody(f.Command);
        var outer = new GraphMediaPhysicalReleaseOuter(f.Session, f.Authority, body);
        body[0] ^= 1;
        Assert.NotEqual(body, outer.BodyBytes);
    }

    [Fact]
    public void Envelope_registration_and_fact_ids_are_exact()
    {
        var f = Fixture();
        var bytes = GraphMediaPhysicalReleaseCodecs.EncodeOuter(new(f.Session, f.Authority,
            GraphMediaPhysicalReleaseCodecs.EncodeCommandBody(f.Command)));
        var registration = GraphMediaPhysicalReleasePayloadRegistrations.Command;
        var proposal = new ProposedAuthorityFactV1(GraphMediaPhysicalReleaseFactIds.Command(f.Session, f.Operation),
            null, OwnerSliceId.S1, registration.Schema, bytes,
            AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, bytes),
            f.Correlation, default);
        Assert.Equal(AuthorityPayloadAdmissionV1.Exact,
            GraphMediaPhysicalReleasePayloadRegistrations.ValidateCommandEnvelope(f.Session, proposal));
        var wrongCorrelation = new ProposedAuthorityFactV1(proposal.FactId, null, proposal.Owner, proposal.PayloadSchema,
            proposal.PayloadBytes, proposal.PayloadHash, new CorrelationEnvelopeV1(f.Correlation.TenantId,
                operationId: OperationId.FromValue(Id(90))), default);
        Assert.Equal(AuthorityPayloadAdmissionV1.InvalidPayload,
            GraphMediaPhysicalReleasePayloadRegistrations.ValidateCommandEnvelope(f.Session, wrongCorrelation));
        var wrongAuthority = ExpectedAuthorityVectorV1.Create(f.Session,
            [new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(Id(91)))]);
        var wrongBytes = GraphMediaPhysicalReleaseCodecs.EncodeOuter(new(f.Session, wrongAuthority,
            GraphMediaPhysicalReleaseCodecs.EncodeCommandBody(f.Command)));
        var wrongGraph = new ProposedAuthorityFactV1(proposal.FactId, null, proposal.Owner, proposal.PayloadSchema,
            wrongBytes, AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, wrongBytes),
            f.Correlation, default);
        Assert.Equal(AuthorityPayloadAdmissionV1.InvalidPayload,
            GraphMediaPhysicalReleasePayloadRegistrations.ValidateCommandEnvelope(f.Session, wrongGraph));
        Assert.Equal(GraphMediaPhysicalReleaseFactIds.Command(f.Session, f.Operation),
            GraphMediaPhysicalReleaseFactIds.Command(f.Session, f.Operation));
        Assert.NotEqual(GraphMediaPhysicalReleaseFactIds.Command(f.Session, f.Operation),
            GraphMediaPhysicalReleaseFactIds.Fact(f.CommandPosition));
    }

    [Fact]
    public void Every_wire_field_and_noncanonical_shape_fails_closed()
    {
        var f = Fixture();
        var command = GraphMediaPhysicalReleaseCodecs.EncodeCommandBody(f.Command);
        Assert.False(GraphMediaPhysicalReleaseCodecs.TryDecodeCommandBody(command.Concat(new byte[] { 0 }).ToArray(), out _));
        Assert.False(GraphMediaPhysicalReleaseCodecs.TryDecodeCommandBody(command[..^1], out _));
        var fact = GraphMediaPhysicalReleaseCodecs.EncodeFactBody(Fact(f, GraphMediaPhysicalReleaseOutcome.Unknown, null, null));
        Assert.False(GraphMediaPhysicalReleaseCodecs.TryDecodeFactBody(fact.Concat(new byte[] { 0 }).ToArray(), out _));
        Assert.False(GraphMediaPhysicalReleaseCodecs.TryDecodeOuter(new byte[] { 0xa0 }, out _));
    }

    private static GraphMediaPhysicalReleaseFactBody Fact(F f, GraphMediaPhysicalReleaseOutcome outcome,
        Hash256? evidence, BoundedAscii? code) => new(f.CommandPosition, f.Residence.ResidenceId,
        f.Residence.RequestHash, f.Residence.GrantId, f.Residence.CurrentFact, f.Residence.Assignment,
        outcome, evidence, code, f.Stamp);

    private static F Fixture()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(1)), LiveSessionId.FromValue(Id(2)));
        var graph = GraphGenerationId.FromValue(Id(3));
        var operation = OperationId.FromValue(Id(4));
        var authority = ExpectedAuthorityVectorV1.Create(session, [new AuthorityAxisValueV1.Graph(graph)]);
        var charge = new CapacityChargeV1(new(3), new(TenantId.FromValue(Id(5)), SessionId.FromValue(Id(6)),
            new CapacitySubjectV1.Operation(operation)), 1, CapacityPurposeId.FromValue(Id(7)), new CapacityChargeWindowV1.NoWindow());
        var assignment = new GraphMediaCapacityAssignment(charge, GraphMediaRepresentationArm.ResidentBytes);
        var residence = new GraphMediaReleaseResidenceProof(OperationId.FromValue(Id(8)), Hash(8), Id(9), Id(10), graph,
            new("node"), ParticipantId.FromValue(Id(11)), new(session, 1), new(session, 2), new(session, 3),
            new(session, 4), CapacityGrantId.FromValue(Id(12)), new(session, 5), new(session, 6), Hash(13), Hash(14), Hash(15),
            assignment, GraphMediaResidenceClass.Controlled, GraphMediaResidenceState.Visible);
        var owner = new GraphMediaOwnerReleaseProof(residence.OwnerId, OperationId.FromValue(Id(16)), Hash(16),
            GraphMediaOwnerTransitionResult.Disposed, Hash(17), 1, Hash(18));
        var work = new GraphMediaWorkReleaseProof(Hash(19), GraphMediaReleaseEligibility.Eligible, 1, 1);
        var stamp = new MonotonicStampV1(ClockDomainId.FromValue(Id(20)), BootId.FromValue(Id(21)), 1);
        var command = new GraphMediaPhysicalReleaseCommandBody(operation, residence, owner, work, null, null, stamp);
        return new(session, operation, authority, residence, command, stamp, new(session, 7),
            new CorrelationEnvelopeV1(TenantId.FromValue(Id(22)), operationId: operation));
    }

    private sealed record F(SessionAuthorityStampV1 Session, OperationId Operation,
        ExpectedAuthorityVectorV1 Authority, GraphMediaReleaseResidenceProof Residence,
        GraphMediaPhysicalReleaseCommandBody Command, MonotonicStampV1 Stamp,
        JournalPositionV1 CommandPosition, CorrelationEnvelopeV1 Correlation);

    private static StableId128 Id(byte value) { Span<byte> bytes = stackalloc byte[16]; bytes.Fill(value); return StableId128.FromBytes(bytes); }
    private static Hash256 Hash(byte value) => Hash256.Compute([value]);
}
