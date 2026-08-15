using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphMediaPhysicalReleaseWireV1Tests
{
    [Fact]
    public void Command_and_fact_round_trip_canonically()
    {
        var f = Fixture();
        var commandBytes = GraphMediaPhysicalReleaseCodecsV1.EncodeCommandBody(f.Command);
        Assert.True(GraphMediaPhysicalReleaseCodecsV1.TryDecodeCommandBody(commandBytes, out var command));
        Assert.Equal(commandBytes, GraphMediaPhysicalReleaseCodecsV1.EncodeCommandBody(command!));
        Assert.Equal(f.Residence.GraphGeneration, command!.Residence.GraphGeneration);

        var fact = Fact(f, GraphMediaPhysicalReleaseOutcomeV1.Released, Hash(40), null);
        var factBytes = GraphMediaPhysicalReleaseCodecsV1.EncodeFactBody(fact);
        Assert.True(GraphMediaPhysicalReleaseCodecsV1.TryDecodeFactBody(factBytes, out var decoded));
        Assert.Equal(factBytes, GraphMediaPhysicalReleaseCodecsV1.EncodeFactBody(decoded!));

        var outer = new GraphMediaPhysicalReleaseOuterV1(f.Session, f.Authority, commandBytes);
        var outerBytes = GraphMediaPhysicalReleaseCodecsV1.EncodeOuter(outer);
        Assert.True(GraphMediaPhysicalReleaseCodecsV1.TryDecodeOuter(outerBytes, out var decodedOuter));
        Assert.Equal(outerBytes, GraphMediaPhysicalReleaseCodecsV1.EncodeOuter(decodedOuter!));
    }

    [Fact]
    public void Constructors_own_bytes_and_close_outcome_invariants()
    {
        var f = Fixture();
        Assert.Throws<ArgumentException>(() => Fact(f, GraphMediaPhysicalReleaseOutcomeV1.Released, null, null));
        Assert.Throws<ArgumentException>(() => Fact(f, GraphMediaPhysicalReleaseOutcomeV1.Unknown, Hash(1), null));
        Assert.Throws<ArgumentException>(() => Fact(f, GraphMediaPhysicalReleaseOutcomeV1.Rejected, null, new("not-authorized")));
        Assert.NotNull(Fact(f, GraphMediaPhysicalReleaseOutcomeV1.Unknown, null, null));
        Assert.NotNull(Fact(f, GraphMediaPhysicalReleaseOutcomeV1.Rejected, null, new("work-encumbered")));

        var body = GraphMediaPhysicalReleaseCodecsV1.EncodeCommandBody(f.Command);
        var outer = new GraphMediaPhysicalReleaseOuterV1(f.Session, f.Authority, body);
        body[0] ^= 1;
        Assert.NotEqual(body, outer.BodyBytes);
    }

    [Fact]
    public void Envelope_registration_and_fact_ids_are_exact()
    {
        var f = Fixture();
        var bytes = GraphMediaPhysicalReleaseCodecsV1.EncodeOuter(new(f.Session, f.Authority,
            GraphMediaPhysicalReleaseCodecsV1.EncodeCommandBody(f.Command)));
        var registration = GraphMediaPhysicalReleasePayloadRegistrationsV1.Command;
        var proposal = new ProposedAuthorityFactV1(GraphMediaPhysicalReleaseFactIdsV1.Command(f.Session, f.Operation),
            null, OwnerSliceId.S1, registration.Schema, bytes,
            AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, bytes),
            f.Correlation, default);
        Assert.Equal(AuthorityPayloadAdmissionV1.Exact,
            GraphMediaPhysicalReleasePayloadRegistrationsV1.ValidateCommandEnvelope(f.Session, proposal));
        var wrongCorrelation = new ProposedAuthorityFactV1(proposal.FactId, null, proposal.Owner, proposal.PayloadSchema,
            proposal.PayloadBytes, proposal.PayloadHash, new CorrelationEnvelopeV1(f.Correlation.TenantId,
                operationId: OperationId.FromValue(Id(90))), default);
        Assert.Equal(AuthorityPayloadAdmissionV1.InvalidPayload,
            GraphMediaPhysicalReleasePayloadRegistrationsV1.ValidateCommandEnvelope(f.Session, wrongCorrelation));
        var wrongAuthority = ExpectedAuthorityVectorV1.Create(f.Session,
            [new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(Id(91)))]);
        var wrongBytes = GraphMediaPhysicalReleaseCodecsV1.EncodeOuter(new(f.Session, wrongAuthority,
            GraphMediaPhysicalReleaseCodecsV1.EncodeCommandBody(f.Command)));
        var wrongGraph = new ProposedAuthorityFactV1(proposal.FactId, null, proposal.Owner, proposal.PayloadSchema,
            wrongBytes, AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, wrongBytes),
            f.Correlation, default);
        Assert.Equal(AuthorityPayloadAdmissionV1.InvalidPayload,
            GraphMediaPhysicalReleasePayloadRegistrationsV1.ValidateCommandEnvelope(f.Session, wrongGraph));
        Assert.Equal(GraphMediaPhysicalReleaseFactIdsV1.Command(f.Session, f.Operation),
            GraphMediaPhysicalReleaseFactIdsV1.Command(f.Session, f.Operation));
        Assert.NotEqual(GraphMediaPhysicalReleaseFactIdsV1.Command(f.Session, f.Operation),
            GraphMediaPhysicalReleaseFactIdsV1.Fact(f.CommandPosition));
    }

    [Fact]
    public void Every_wire_field_and_noncanonical_shape_fails_closed()
    {
        var f = Fixture();
        var command = GraphMediaPhysicalReleaseCodecsV1.EncodeCommandBody(f.Command);
        Assert.False(GraphMediaPhysicalReleaseCodecsV1.TryDecodeCommandBody(command.Concat(new byte[] { 0 }).ToArray(), out _));
        Assert.False(GraphMediaPhysicalReleaseCodecsV1.TryDecodeCommandBody(command[..^1], out _));
        var fact = GraphMediaPhysicalReleaseCodecsV1.EncodeFactBody(Fact(f, GraphMediaPhysicalReleaseOutcomeV1.Unknown, null, null));
        Assert.False(GraphMediaPhysicalReleaseCodecsV1.TryDecodeFactBody(fact.Concat(new byte[] { 0 }).ToArray(), out _));
        Assert.False(GraphMediaPhysicalReleaseCodecsV1.TryDecodeOuter(new byte[] { 0xa0 }, out _));
    }

    private static GraphMediaPhysicalReleaseFactBodyV1 Fact(F f, GraphMediaPhysicalReleaseOutcomeV1 outcome,
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
        var assignment = new GraphMediaCapacityAssignmentV1(charge, GraphMediaRepresentationArmV1.ResidentBytes);
        var residence = new GraphMediaReleaseResidenceProofV1(OperationId.FromValue(Id(8)), Hash(8), Id(9), Id(10), graph,
            new("node"), ParticipantId.FromValue(Id(11)), new(session, 1), new(session, 2), new(session, 3),
            new(session, 4), CapacityGrantId.FromValue(Id(12)), new(session, 5), new(session, 6), Hash(13), Hash(14), Hash(15),
            assignment, GraphMediaResidenceClassV1.Controlled, GraphMediaResidenceStateV1.Visible);
        var owner = new GraphMediaOwnerReleaseProofV1(residence.OwnerId, OperationId.FromValue(Id(16)), Hash(16),
            GraphMediaOwnerTransitionResultV1.Disposed, Hash(17), 1, Hash(18));
        var work = new GraphMediaWorkReleaseProofV1(Hash(19), GraphMediaReleaseEligibilityV1.Eligible, 1, 1);
        var stamp = new MonotonicStampV1(ClockDomainId.FromValue(Id(20)), BootId.FromValue(Id(21)), 1);
        var command = new GraphMediaPhysicalReleaseCommandBodyV1(operation, residence, owner, work, null, null, stamp);
        return new(session, operation, authority, residence, command, stamp, new(session, 7),
            new CorrelationEnvelopeV1(TenantId.FromValue(Id(22)), operationId: operation));
    }

    private sealed record F(SessionAuthorityStampV1 Session, OperationId Operation,
        ExpectedAuthorityVectorV1 Authority, GraphMediaReleaseResidenceProofV1 Residence,
        GraphMediaPhysicalReleaseCommandBodyV1 Command, MonotonicStampV1 Stamp,
        JournalPositionV1 CommandPosition, CorrelationEnvelopeV1 Correlation);

    private static StableId128 Id(byte value) { Span<byte> bytes = stackalloc byte[16]; bytes.Fill(value); return StableId128.FromBytes(bytes); }
    private static Hash256 Hash(byte value) => Hash256.Compute([value]);
}
