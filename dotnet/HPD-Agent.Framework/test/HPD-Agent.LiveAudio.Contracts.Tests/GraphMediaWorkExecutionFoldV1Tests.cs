using System.Formats.Cbor;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphMediaWorkExecutionFoldV1Tests
{
    [Fact]
    public void Command_only_and_each_fact_arm_replay_exactly()
    {
        var f = Fixture();
        var fold = GraphMediaWorkExecutionFoldV1.Create(f.Session, f.Work.ResidenceId, f.Registry);
        var command = CommandEnvelope(f, 1, f.Operation, null);
        Assert.IsType<GraphMediaWorkExecutionFoldApplyResultV1.Applied>(fold.Apply(command));
        Assert.IsType<GraphMediaWorkExecutionFoldResultV1.CommandOnly>(fold.Complete());

        foreach (var outcome in Enum.GetValues<GraphMediaWorkExecutionOutcomeV1>())
        {
            f = Fixture(); fold = GraphMediaWorkExecutionFoldV1.Create(f.Session, f.Work.ResidenceId, f.Registry);
            command = CommandEnvelope(f, 1, f.Operation, null); fold.Apply(command);
            Assert.IsType<GraphMediaWorkExecutionFoldApplyResultV1.Applied>(fold.Apply(FactEnvelope(f, command, 2, outcome)));
            Assert.Equal(outcome switch
            {
                GraphMediaWorkExecutionOutcomeV1.Completed => typeof(GraphMediaWorkExecutionFoldResultV1.Completed),
                GraphMediaWorkExecutionOutcomeV1.Unknown => typeof(GraphMediaWorkExecutionFoldResultV1.Unknown),
                _ => typeof(GraphMediaWorkExecutionFoldResultV1.Rejected)
            }, fold.Complete().GetType());
        }
    }

    [Fact]
    public void Command_fact_identity_and_outer_joins_fail_closed()
    {
        AssertJoinInvalid((f, command) => FactEnvelope(f, command, 2, GraphMediaWorkExecutionOutcomeV1.Completed,
            requestHash: Hash(90)));
        AssertJoinInvalid((f, command) => FactEnvelope(f, command, 2, GraphMediaWorkExecutionOutcomeV1.Completed,
            workId: Id(91)));
        AssertJoinInvalid((f, command) => FactEnvelope(f, command, 2, GraphMediaWorkExecutionOutcomeV1.Completed,
            authority: ExpectedAuthorityVectorV1.Create(f.Session, [new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(Id(92)))])));
        AssertJoinInvalid((f, command) => FactEnvelope(f, command, 2, GraphMediaWorkExecutionOutcomeV1.Completed,
            correlation: new CorrelationEnvelopeV1(TenantId.FromValue(Id(93)), operationId: f.Operation)));
        AssertJoinInvalid((f, command) => FactEnvelope(f, command, 2, GraphMediaWorkExecutionOutcomeV1.Completed,
            bodyObserved: Stamp(94)));

        var f = Fixture(); var fold = GraphMediaWorkExecutionFoldV1.Create(f.Session, f.Work.ResidenceId, f.Registry);
        var orphanCommand = CommandEnvelope(f, 3, f.Operation, null);
        var invalid = Assert.IsType<GraphMediaWorkExecutionFoldApplyResultV1.InvalidHistory>(
            fold.Apply(FactEnvelope(f, orphanCommand, 1, GraphMediaWorkExecutionOutcomeV1.Unknown)));
        Assert.Equal("fact-without-command", invalid.SafeCode.ToString());
    }

    [Fact]
    public void Duplicate_predecessor_rejection_and_multi_work_laws_are_closed()
    {
        var f = Fixture(); var fold = GraphMediaWorkExecutionFoldV1.Create(f.Session, f.Work.ResidenceId, f.Registry);
        var command = CommandEnvelope(f, 1, f.Operation, null); fold.Apply(command);
        Assert.True(Assert.IsType<GraphMediaWorkExecutionFoldApplyResultV1.Applied>(fold.Apply(command)).Duplicate);
        var second = CommandEnvelope(f, 2, Operation(40), null);
        Assert.Equal("predecessor-conflict", Assert.IsType<GraphMediaWorkExecutionFoldApplyResultV1.InvalidHistory>(fold.Apply(second)).SafeCode.ToString());

        f = Fixture(); fold = GraphMediaWorkExecutionFoldV1.Create(f.Session, f.Work.ResidenceId, f.Registry);
        command = CommandEnvelope(f, 1, f.Operation, null); fold.Apply(command);
        var rejected = FactEnvelope(f, command, 2, GraphMediaWorkExecutionOutcomeV1.Rejected); fold.Apply(rejected);
        Assert.IsType<GraphMediaWorkExecutionFoldApplyResultV1.Applied>(fold.Apply(CommandEnvelope(f, 3, Operation(41), rejected.Position)));

        f = Fixture(); fold = GraphMediaWorkExecutionFoldV1.Create(f.Session, f.Work.ResidenceId, f.Registry);
        command = CommandEnvelope(f, 1, f.Operation, null); fold.Apply(command);
        var completed = FactEnvelope(f, command, 2, GraphMediaWorkExecutionOutcomeV1.Completed); fold.Apply(completed);
        var secondWork = Work(f, 42);
        var secondCommand = CommandEnvelope(f, 3, Operation(42), completed.Position, secondWork);
        Assert.IsType<GraphMediaWorkExecutionFoldApplyResultV1.Applied>(fold.Apply(secondCommand));
        var secondFact = FactEnvelope(f, secondCommand, 4, GraphMediaWorkExecutionOutcomeV1.Completed,
            workId: secondWork.WorkId, requestHash: secondWork.RequestHash);
        Assert.IsType<GraphMediaWorkExecutionFoldApplyResultV1.Applied>(fold.Apply(secondFact));
        Assert.IsType<GraphMediaWorkExecutionFoldResultV1.Completed>(fold.Query(f.Operation));
        Assert.IsType<GraphMediaWorkExecutionFoldResultV1.Completed>(fold.Query(Operation(42)));
    }

    [Fact]
    public void Changed_duplicate_position_and_fact_id_aliases_fail_closed()
    {
        var f = Fixture(); var fold = GraphMediaWorkExecutionFoldV1.Create(f.Session, f.Work.ResidenceId, f.Registry);
        var command = CommandEnvelope(f, 1, f.Operation, null); fold.Apply(command);
        var changed = CommandEnvelope(f, 1, Operation(43), null);
        Assert.Equal("position-invalid", Assert.IsType<GraphMediaWorkExecutionFoldApplyResultV1.InvalidHistory>(fold.Apply(changed)).SafeCode.ToString());

        f = Fixture(); fold = GraphMediaWorkExecutionFoldV1.Create(f.Session, f.Work.ResidenceId, f.Registry);
        command = CommandEnvelope(f, 1, f.Operation, null); fold.Apply(command);
        var aliased = Reposition(command, 2);
        Assert.Equal("fact-id-mismatch", Assert.IsType<GraphMediaWorkExecutionFoldApplyResultV1.InvalidHistory>(fold.Apply(aliased)).SafeCode.ToString());
    }

    [Fact]
    public void Registered_unrelated_records_are_counted_even_after_target_terminal()
    {
        var f = Fixture(); var fold = GraphMediaWorkExecutionFoldV1.Create(f.Session, f.Work.ResidenceId, f.Registry);
        var command = CommandEnvelope(f, 1, f.Operation, null); fold.Apply(command);
        fold.Apply(FactEnvelope(f, command, 2, GraphMediaWorkExecutionOutcomeV1.Completed));
        Assert.IsType<GraphMediaWorkExecutionFoldApplyResultV1.Ignored>(fold.Apply(Initialization(f, 3)));
        var result = Assert.IsType<GraphMediaWorkExecutionFoldResultV1.Completed>(fold.Complete());
        Assert.Equal(3UL, result.RecordCount);
    }

    private static void AssertJoinInvalid(Func<F, AuthorityFactEnvelopeV1, AuthorityFactEnvelopeV1> mutation)
    {
        var f = Fixture(); var fold = GraphMediaWorkExecutionFoldV1.Create(f.Session, f.Work.ResidenceId, f.Registry);
        var command = CommandEnvelope(f, 1, f.Operation, null); fold.Apply(command);
        var invalid = Assert.IsType<GraphMediaWorkExecutionFoldApplyResultV1.InvalidHistory>(fold.Apply(mutation(f, command)));
        Assert.Equal("command-fact-join-invalid", invalid.SafeCode.ToString());
    }

    private static AuthorityFactEnvelopeV1 CommandEnvelope(F f, long sequence, OperationId operation,
        JournalPositionV1? predecessor, GraphMediaWorkAuthorityV1? work = null)
    {
        var body = new GraphMediaWorkExecutionCommandBodyV1(operation, work ?? f.Work, f.Cleanups, predecessor, f.Stamp);
        var payload = GraphMediaWorkExecutionCodecsV1.EncodeOuter(new(f.Session, f.Authority,
            GraphMediaWorkExecutionCodecsV1.EncodeCommandBody(body)));
        return Envelope(f, sequence, GraphMediaWorkExecutionFactIdsV1.Command(f.Session, operation),
            GraphMediaWorkExecutionPayloadRegistrationsV1.Command, payload, operation);
    }

    private static AuthorityFactEnvelopeV1 FactEnvelope(F f, AuthorityFactEnvelopeV1 command, long sequence,
        GraphMediaWorkExecutionOutcomeV1 outcome, StableId128? workId = null, Hash256? requestHash = null,
        ExpectedAuthorityVectorV1? authority = null, CorrelationEnvelopeV1? correlation = null,
        MonotonicStampV1? bodyObserved = null)
    {
        var body = new GraphMediaWorkExecutionFactBodyV1(command.Position, workId ?? f.Work.WorkId,
            requestHash ?? f.Work.RequestHash, outcome,
            outcome == GraphMediaWorkExecutionOutcomeV1.Completed ? Hash(70) : null,
            outcome == GraphMediaWorkExecutionOutcomeV1.Rejected ? new("work-effect-rejected") : null,
            bodyObserved ?? f.Stamp);
        var payload = GraphMediaWorkExecutionCodecsV1.EncodeOuter(new(f.Session, authority ?? f.Authority,
            GraphMediaWorkExecutionCodecsV1.EncodeFactBody(body)));
        return Envelope(f, sequence, GraphMediaWorkExecutionFactIdsV1.Fact(command.Position),
            GraphMediaWorkExecutionPayloadRegistrationsV1.Fact, payload,
            command.Correlation.OperationId!.Value, correlation ?? command.Correlation);
    }

    private static AuthorityFactEnvelopeV1 Envelope(F f, long sequence, JournalFactId factId,
        AuthorityPayloadRegistrationV1 registration, byte[] payload, OperationId operation,
        CorrelationEnvelopeV1? correlation = null) => new(factId, new(f.Session, sequence), null,
        registration.Owner, registration.Schema, payload,
        AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload),
        correlation ?? new CorrelationEnvelopeV1(TenantId.FromValue(Id(50)), operationId: operation),
        default, default, new IntegrityEnvelopeV1(1, 1, Hash(51), []));

    private static AuthorityFactEnvelopeV1 Reposition(AuthorityFactEnvelopeV1 source, long sequence) => new(
        source.FactId, new(source.Position.Session, sequence), source.ThreadScope, source.Owner, source.PayloadSchema,
        source.PayloadBytes, source.PayloadHash, source.Correlation, source.ObservedAt, source.AdmittedAt, source.Integrity);

    private static AuthorityFactEnvelopeV1 Initialization(F f, long sequence)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(3); writer.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(writer, f.Session);
        writer.WriteUInt64(2); Span<byte> graph = stackalloc byte[16]; f.Work.OwnerKey.GraphGeneration.TryWriteBytes(graph); writer.WriteByteString(graph);
        writer.WriteUInt64(3); writer.WriteUInt64((ushort)OwnerSliceId.S2); writer.WriteEndMap();
        return Envelope(f, sequence, JournalFactId.FromValue(Id(60)),
            new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Graph), writer.Encode(), f.Operation);
    }

    private static F Fixture()
    {
        var session = Session(); var graph = GraphGenerationId.FromValue(Id(3)); var operation = Operation(25);
        var authority = ExpectedAuthorityVectorV1.Create(session, [new AuthorityAxisValueV1.Graph(graph)]);
        Assert.True(GraphMediaBindingV1.TryCreate(0, 1_000, Id(10), 1, 48_000, 2, 2, Id(11), 1, 0,
            GraphMediaDiscontinuityKindV1.ResetBefore, 400, 100, null, out var media));
        var participant = ParticipantId.FromValue(Id(14));
        var scope = new CapacityScopeV1(TenantId.FromValue(Id(12)), SessionId.FromValue(Id(13)),
            new CapacitySubjectV1.Participant(participant));
        var charge = new CapacityChargeV1(new(1), scope, 400, CapacityPurposeId.FromValue(Id(15)), new CapacityChargeWindowV1.NoWindow());
        var work = new GraphMediaWorkAuthorityV1(Id(16), Hash(17), Id(18), Operation(19), Hash(20), Id(21),
            new(session, graph, Id(22)), media!, participant, new(session, 3), CapacityGrantId.FromValue(Id(23)),
            new(session, 4), Hash(24), new(charge, GraphMediaRepresentationArmV1.ResidentBytes));
        var cleanups = new GraphMediaCleanupRegistrationV1[] { new(Id(26), Hash(27)), new(Id(28), Hash(29)) };
        var registry = new AuthorityPayloadAdmissionRegistryV1([
            GraphMediaWorkExecutionPayloadRegistrationsV1.Command, GraphMediaWorkExecutionPayloadRegistrationsV1.Fact,
            new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Graph)]);
        return new(session, operation, authority, work, cleanups, Stamp(30), registry);
    }

    private static GraphMediaWorkAuthorityV1 Work(F f, byte seed) => new(Id(seed), Hash((byte)(seed + 1)),
        f.Work.ResidenceId, f.Work.ResidenceOperationId, f.Work.ResidenceRequestHash, f.Work.OwnerId,
        f.Work.OwnerKey, f.Work.Media, f.Work.ParticipantId, f.Work.BindingFactPosition, f.Work.GrantId,
        f.Work.CurrentFact, f.Work.CoverageHashV2, f.Work.Assignment);

    private sealed record F(SessionAuthorityStampV1 Session, OperationId Operation,
        ExpectedAuthorityVectorV1 Authority, GraphMediaWorkAuthorityV1 Work,
        GraphMediaCleanupRegistrationV1[] Cleanups, MonotonicStampV1 Stamp,
        AuthorityPayloadAdmissionRegistryV1 Registry);
    private static SessionAuthorityStampV1 Session() => new(RuntimeGenerationId.FromValue(Id(1)), LiveSessionId.FromValue(Id(2)));
    private static OperationId Operation(byte seed) => OperationId.FromValue(Id(seed));
    private static MonotonicStampV1 Stamp(byte seed) => new(ClockDomainId.FromValue(Id(31)), BootId.FromValue(Id(32)), seed);
    private static Hash256 Hash(byte seed) => Hash256.Compute([seed]);
    private static StableId128 Id(byte seed) { Span<byte> bytes = stackalloc byte[16]; bytes.Fill(seed); return StableId128.FromBytes(bytes); }
}
