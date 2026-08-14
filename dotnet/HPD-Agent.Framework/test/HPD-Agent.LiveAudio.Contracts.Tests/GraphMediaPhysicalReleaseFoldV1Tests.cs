using System.Formats.Cbor;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphMediaPhysicalReleaseFoldV1Tests
{
    [Fact]
    public void Unrelated_records_are_counted_and_ignored()
    {
        var f = Fixture();
        var fold = GraphMediaPhysicalReleaseFoldV1.Create(f.Session, f.Residence.ResidenceId, f.Registry);
        var unrelated = Initialization(f, 1);
        Assert.IsType<GraphMediaPhysicalReleaseFoldApplyResultV1.Ignored>(fold.Apply(unrelated));
        var result = Assert.IsType<GraphMediaPhysicalReleaseFoldResultV1.NotFound>(fold.Complete());
        Assert.Equal(1UL, result.RecordCount);
        Assert.True(result.TotalCanonicalRecordBytes > 0);
    }

    [Fact]
    public void Command_only_and_each_terminal_outcome_are_exact()
    {
        var commandOnly = Fixture();
        var fold = GraphMediaPhysicalReleaseFoldV1.Create(commandOnly.Session, commandOnly.Residence.ResidenceId, commandOnly.Registry);
        var command = Command(commandOnly, 1, commandOnly.Operation, null);
        Assert.IsType<GraphMediaPhysicalReleaseFoldApplyResultV1.Applied>(fold.Apply(command));
        Assert.IsType<GraphMediaPhysicalReleaseFoldResultV1.CommandOnly>(fold.Complete());

        foreach (var outcome in Enum.GetValues<GraphMediaPhysicalReleaseOutcomeV1>())
        {
            var f = Fixture(); fold = GraphMediaPhysicalReleaseFoldV1.Create(f.Session, f.Residence.ResidenceId, f.Registry);
            command = Command(f, 1, f.Operation, null); fold.Apply(command);
            var fact = Fact(f, command, 2, outcome);
            Assert.IsType<GraphMediaPhysicalReleaseFoldApplyResultV1.Applied>(fold.Apply(fact));
            var completed = fold.Complete();
            Assert.Equal(outcome switch
            {
                GraphMediaPhysicalReleaseOutcomeV1.Released => typeof(GraphMediaPhysicalReleaseFoldResultV1.Released),
                GraphMediaPhysicalReleaseOutcomeV1.Unknown => typeof(GraphMediaPhysicalReleaseFoldResultV1.Unknown),
                _ => typeof(GraphMediaPhysicalReleaseFoldResultV1.Rejected)
            }, completed.GetType());
        }
    }

    [Fact]
    public void Every_command_fact_join_fails_closed()
    {
        var f = Fixture();
        var fold = GraphMediaPhysicalReleaseFoldV1.Create(f.Session, f.Residence.ResidenceId, f.Registry);
        var command = Command(f, 1, f.Operation, null); fold.Apply(command);
        var wrong = Fact(f, command, 2, GraphMediaPhysicalReleaseOutcomeV1.Released,
            requestHash: Hash(99));
        var invalid = Assert.IsType<GraphMediaPhysicalReleaseFoldApplyResultV1.InvalidHistory>(fold.Apply(wrong));
        Assert.Equal("command-fact-join-invalid", invalid.SafeCode.ToString());

        f = Fixture(); fold = GraphMediaPhysicalReleaseFoldV1.Create(f.Session, f.Residence.ResidenceId, f.Registry);
        var orphan = Fact(f, Command(f, 3, f.Operation, null), 1, GraphMediaPhysicalReleaseOutcomeV1.Unknown);
        invalid = Assert.IsType<GraphMediaPhysicalReleaseFoldApplyResultV1.InvalidHistory>(fold.Apply(orphan));
        Assert.Equal("fact-without-command", invalid.SafeCode.ToString());
    }

    [Fact]
    public void Duplicate_predecessor_singleton_and_post_terminal_rules_are_closed()
    {
        var f = Fixture();
        var fold = GraphMediaPhysicalReleaseFoldV1.Create(f.Session, f.Residence.ResidenceId, f.Registry);
        var command = Command(f, 1, f.Operation, null);
        fold.Apply(command);
        var duplicate = Assert.IsType<GraphMediaPhysicalReleaseFoldApplyResultV1.Applied>(fold.Apply(command));
        Assert.True(duplicate.Duplicate);
        var second = Command(f, 2, OperationId.FromValue(Id(40)), null);
        Assert.Equal("predecessor-conflict", Assert.IsType<GraphMediaPhysicalReleaseFoldApplyResultV1.InvalidHistory>(fold.Apply(second)).SafeCode.ToString());

        f = Fixture(); fold = GraphMediaPhysicalReleaseFoldV1.Create(f.Session, f.Residence.ResidenceId, f.Registry);
        command = Command(f, 1, f.Operation, null); fold.Apply(command);
        var terminal = Fact(f, command, 2, GraphMediaPhysicalReleaseOutcomeV1.Released); fold.Apply(terminal);
        Assert.Equal("post-terminal-record", Assert.IsType<GraphMediaPhysicalReleaseFoldApplyResultV1.InvalidHistory>(fold.Apply(Command(f, 3, OperationId.FromValue(Id(41)), terminal.Position))).SafeCode.ToString());

        f = Fixture(); fold = GraphMediaPhysicalReleaseFoldV1.Create(f.Session, f.Residence.ResidenceId, f.Registry);
        command = Command(f, 1, f.Operation, null); fold.Apply(command);
        var rejected = Fact(f, command, 2, GraphMediaPhysicalReleaseOutcomeV1.Rejected); fold.Apply(rejected);
        Assert.IsType<GraphMediaPhysicalReleaseFoldApplyResultV1.Applied>(fold.Apply(Command(f, 3, OperationId.FromValue(Id(42)), rejected.Position)));
    }

    private static AuthorityFactEnvelopeV1 Command(F f, long sequence, OperationId operation, JournalPositionV1? predecessor)
    {
        var body = new GraphMediaPhysicalReleaseCommandBodyV1(operation, f.Residence, f.Owner, f.Work, null, predecessor, f.Stamp);
        var payload = GraphMediaPhysicalReleaseCodecsV1.EncodeOuter(new(f.Session, f.Authority,
            GraphMediaPhysicalReleaseCodecsV1.EncodeCommandBody(body)));
        return Envelope(f, sequence, GraphMediaPhysicalReleaseFactIdsV1.Command(f.Session, operation),
            GraphMediaPhysicalReleasePayloadRegistrationsV1.Command, payload, operation);
    }

    private static AuthorityFactEnvelopeV1 Fact(F f, AuthorityFactEnvelopeV1 command, long sequence,
        GraphMediaPhysicalReleaseOutcomeV1 outcome, Hash256? requestHash = null)
    {
        var body = new GraphMediaPhysicalReleaseFactBodyV1(command.Position, f.Residence.ResidenceId,
            requestHash ?? f.Residence.RequestHash, f.Residence.GrantId, f.Residence.CurrentFact,
            f.Residence.Assignment, outcome, outcome == GraphMediaPhysicalReleaseOutcomeV1.Released ? Hash(50) : null,
            outcome == GraphMediaPhysicalReleaseOutcomeV1.Rejected ? new("work-encumbered") : null, f.Stamp);
        var payload = GraphMediaPhysicalReleaseCodecsV1.EncodeOuter(new(f.Session, f.Authority,
            GraphMediaPhysicalReleaseCodecsV1.EncodeFactBody(body)));
        return Envelope(f, sequence, GraphMediaPhysicalReleaseFactIdsV1.Fact(command.Position),
            GraphMediaPhysicalReleasePayloadRegistrationsV1.Fact, payload, f.Operation);
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
        AuthorityPayloadRegistrationV1 registration, byte[] payload, OperationId operation) => new(factId,
        new(f.Session, sequence), null, registration.Owner, registration.Schema, payload,
        AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload),
        new CorrelationEnvelopeV1(TenantId.FromValue(Id(22)), operationId: operation), default, default,
        new IntegrityEnvelopeV1(1, 1, Hash(61), []));

    private static F Fixture()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(1)), LiveSessionId.FromValue(Id(2)));
        var graph = GraphGenerationId.FromValue(Id(3)); var operation = OperationId.FromValue(Id(4));
        var authority = ExpectedAuthorityVectorV1.Create(session, [new AuthorityAxisValueV1.Graph(graph)]);
        var charge = new CapacityChargeV1(new(3), new(TenantId.FromValue(Id(5)), SessionId.FromValue(Id(6)),
            new CapacitySubjectV1.Operation(operation)), 1, CapacityPurposeId.FromValue(Id(7)), new CapacityChargeWindowV1.NoWindow());
        var assignment = new GraphMediaCapacityAssignmentV1(charge, GraphMediaRepresentationArmV1.ResidentBytes);
        var residence = new GraphMediaReleaseResidenceProofV1(OperationId.FromValue(Id(8)), Hash(8), Id(9), Id(10), graph,
            new("node"), ParticipantId.FromValue(Id(11)), new(session, 1), new(session, 2), new(session, 3), new(session, 4),
            CapacityGrantId.FromValue(Id(12)), new(session, 5), new(session, 6), Hash(13), Hash(14), Hash(15), assignment,
            GraphMediaResidenceClassV1.Controlled, GraphMediaResidenceStateV1.Visible);
        var owner = new GraphMediaOwnerReleaseProofV1(residence.OwnerId, OperationId.FromValue(Id(16)), Hash(16),
            GraphMediaOwnerTransitionResultV1.Disposed, Hash(17), 1, Hash(18));
        var work = new GraphMediaWorkReleaseProofV1(Hash(19), GraphMediaReleaseEligibilityV1.Eligible, 1, 1);
        var stamp = new MonotonicStampV1(ClockDomainId.FromValue(Id(20)), BootId.FromValue(Id(21)), 1);
        var registry = new AuthorityPayloadAdmissionRegistryV1([GraphMediaPhysicalReleasePayloadRegistrationsV1.Command,
            GraphMediaPhysicalReleasePayloadRegistrationsV1.Fact,
            new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Graph)]);
        return new(session, graph, operation, authority, residence, owner, work, stamp, registry);
    }

    private sealed record F(SessionAuthorityStampV1 Session, GraphGenerationId Graph, OperationId Operation,
        ExpectedAuthorityVectorV1 Authority, GraphMediaReleaseResidenceProofV1 Residence,
        GraphMediaOwnerReleaseProofV1 Owner, GraphMediaWorkReleaseProofV1 Work, MonotonicStampV1 Stamp,
        AuthorityPayloadAdmissionRegistryV1 Registry);
    private static StableId128 Id(byte value) { Span<byte> bytes = stackalloc byte[16]; bytes.Fill(value); return StableId128.FromBytes(bytes); }
    private static Hash256 Hash(byte value) => Hash256.Compute([value]);
}
