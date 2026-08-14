using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphMediaWorkExecutionWireV1Tests
{
    [Fact]
    public void Command_and_all_fact_arms_round_trip_canonically()
    {
        var command = Command();
        var bytes = GraphMediaWorkExecutionCodecsV1.EncodeCommandBody(command);
        Assert.True(GraphMediaWorkExecutionCodecsV1.TryDecodeCommandBody(bytes, out var decoded));
        Assert.NotNull(decoded); Assert.Equal(command.OperationId, decoded.OperationId);
        Assert.Equal(command.Work, decoded.Work); Assert.Equal(command.Cleanups, decoded.Cleanups);
        Assert.Equal(bytes, GraphMediaWorkExecutionCodecsV1.EncodeCommandBody(decoded));

        var position = Position(7);
        GraphMediaWorkExecutionFactBodyV1[] facts =
        [
            new(position, command.Work.WorkId, command.Work.RequestHash,
                GraphMediaWorkExecutionOutcomeV1.Completed, Hash(71), null, Stamp(72)),
            new(position, command.Work.WorkId, command.Work.RequestHash,
                GraphMediaWorkExecutionOutcomeV1.Unknown, null, null, Stamp(72)),
            new(position, command.Work.WorkId, command.Work.RequestHash,
                GraphMediaWorkExecutionOutcomeV1.Rejected, null, new("work-effect-rejected"), Stamp(72))
        ];
        foreach (var fact in facts)
        {
            var factBytes = GraphMediaWorkExecutionCodecsV1.EncodeFactBody(fact);
            Assert.True(GraphMediaWorkExecutionCodecsV1.TryDecodeFactBody(factBytes, out var factDecoded));
            Assert.Equal(fact, factDecoded);
            Assert.Equal(factBytes, GraphMediaWorkExecutionCodecsV1.EncodeFactBody(factDecoded!));
        }
    }

    [Fact]
    public void Outer_round_trip_owns_body_bytes()
    {
        var command = Command(); var body = GraphMediaWorkExecutionCodecsV1.EncodeCommandBody(command);
        var original = body.ToArray(); var outer = new GraphMediaWorkExecutionOuterV1(Session(), Authority(), body);
        body[0] ^= 0xff;
        Assert.Equal(original, outer.BodyBytes);
        var bytes = GraphMediaWorkExecutionCodecsV1.EncodeOuter(outer);
        Assert.True(GraphMediaWorkExecutionCodecsV1.TryDecodeOuter(bytes, out var decoded));
        Assert.Equal(original, decoded!.BodyBytes);
        Assert.Equal(bytes, GraphMediaWorkExecutionCodecsV1.EncodeOuter(decoded));
    }

    [Fact]
    public void Noncanonical_trailing_oversize_and_mutated_payloads_fail_closed()
    {
        var command = GraphMediaWorkExecutionCodecsV1.EncodeCommandBody(Command());
        var trailing = new byte[command.Length + 1]; command.CopyTo(trailing, 0);
        Assert.False(GraphMediaWorkExecutionCodecsV1.TryDecodeCommandBody(trailing, out _));
        var changed = command.ToArray(); changed[1] = 2;
        Assert.False(GraphMediaWorkExecutionCodecsV1.TryDecodeCommandBody(changed, out _));
        Assert.False(GraphMediaWorkExecutionCodecsV1.TryDecodeCommandBody(
            new byte[GraphMediaWorkExecutionCodecsV1.MaximumBodyBytes + 1], out _));
        Assert.False(GraphMediaWorkExecutionCodecsV1.TryDecodeOuter(
            new byte[GraphMediaWorkExecutionCodecsV1.MaximumOuterBytes + 1], out _));
    }

    [Fact]
    public void Constructor_invariants_close_cleanup_and_outcome_unions()
    {
        var command = Command(); var reversed = command.Cleanups.Reverse().ToArray();
        Assert.Throws<ArgumentException>(() => new GraphMediaWorkExecutionCommandBodyV1(
            command.OperationId, command.Work, reversed, null, command.ObservedAt));
        Assert.Throws<ArgumentException>(() => new GraphMediaWorkExecutionFactBodyV1(Position(7),
            command.Work.WorkId, command.Work.RequestHash, GraphMediaWorkExecutionOutcomeV1.Completed,
            null, null, Stamp(72)));
        Assert.Throws<ArgumentException>(() => new GraphMediaWorkExecutionFactBodyV1(Position(7),
            command.Work.WorkId, command.Work.RequestHash, GraphMediaWorkExecutionOutcomeV1.Unknown,
            Hash(73), null, Stamp(72)));
        Assert.Throws<ArgumentException>(() => new GraphMediaWorkExecutionFactBodyV1(Position(7),
            command.Work.WorkId, command.Work.RequestHash, GraphMediaWorkExecutionOutcomeV1.Rejected,
            null, new("not-authorized"), Stamp(72)));
    }

    [Fact]
    public void Fact_ids_are_deterministic_domain_separated_and_position_bound()
    {
        var operation = Operation(1); var command = GraphMediaWorkExecutionFactIdsV1.Command(Session(), operation);
        Assert.Equal(command, GraphMediaWorkExecutionFactIdsV1.Command(Session(), operation));
        var fact = GraphMediaWorkExecutionFactIdsV1.Fact(Position(7));
        Assert.NotEqual(command, fact);
        Assert.NotEqual(fact, GraphMediaWorkExecutionFactIdsV1.Fact(Position(8)));
        Assert.NotEqual(command, GraphMediaPhysicalReleaseFactIdsV1.Command(Session(), operation));
        Span<byte> commandBytes = stackalloc byte[16]; Span<byte> factBytes = stackalloc byte[16];
        Assert.True(command.TryWriteBytes(commandBytes)); Assert.True(fact.TryWriteBytes(factBytes));
        Assert.Equal("5B880C3C963039DC4D4DC913597A955A", Convert.ToHexString(commandBytes));
        Assert.Equal("EBDAF75179753F5C55FCF34FD48AF537", Convert.ToHexString(factBytes));
    }

    private static GraphMediaWorkExecutionCommandBodyV1 Command()
    {
        Assert.True(GraphMediaBindingV1.TryCreate(0, 1_000, Id(10), 1, 48_000, 2, 2,
            Id(11), 1, 0, GraphMediaDiscontinuityKindV1.ResetBefore, 400, 100, null, out var media));
        var scope = new CapacityScopeV1(TenantId.FromValue(Id(12)), SessionId.FromValue(Id(13)),
            new CapacitySubjectV1.Participant(ParticipantId.FromValue(Id(14))));
        var charge = new CapacityChargeV1(new(1), scope, 400, CapacityPurposeId.FromValue(Id(15)),
            new CapacityChargeWindowV1.NoWindow());
        var work = new GraphMediaWorkAuthorityV1(Id(16), Hash(17), Id(18), Operation(19), Hash(20),
            Id(21), new(Session(), Graph(), Id(22)), media!, ParticipantId.FromValue(Id(14)),
            Position(3), CapacityGrantId.FromValue(Id(23)), Position(4), Hash(24),
            new GraphMediaCapacityAssignmentV1(charge, GraphMediaRepresentationArmV1.ResidentBytes));
        return new(Operation(25), work,
            [new(Id(26), Hash(27)), new(Id(28), Hash(29))], null, Stamp(30));
    }
    private static ExpectedAuthorityVectorV1 Authority() => ExpectedAuthorityVectorV1.Create(Session(),
        [new AuthorityAxisValueV1.Graph(Graph())]);
    private static SessionAuthorityStampV1 Session() => new(RuntimeGenerationId.FromValue(Id(1)), LiveSessionId.FromValue(Id(2)));
    private static GraphGenerationId Graph() => GraphGenerationId.FromValue(Id(3));
    private static OperationId Operation(byte seed) => OperationId.FromValue(Id(seed));
    private static JournalPositionV1 Position(long sequence) => new(Session(), sequence);
    private static MonotonicStampV1 Stamp(long value) => new(ClockDomainId.FromValue(Id(31)), BootId.FromValue(Id(32)), checked((ulong)value));
    private static Hash256 Hash(byte seed) => Hash256.Compute([seed]);
    private static StableId128 Id(byte seed) { Span<byte> bytes = stackalloc byte[16]; bytes.Fill(seed); return StableId128.FromBytes(bytes); }
}
