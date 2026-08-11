using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphReplacementCodecsV1Tests
{
    [Fact]
    public void Every_command_arm_round_trips_canonically()
    {
        var f = new Fixture();
        GraphReplacementJournalCommandV1[] commands =
        [
            new GraphReplacementJournalCommandV1.Prepare(f.Operation, f.Position(10), f.Source.Fingerprint,
                f.Target, f.Position(4), f.Authority, f.Observed, f.Deadline),
            new GraphReplacementJournalCommandV1.Commit(f.Operation, f.Position(11)),
            new GraphReplacementJournalCommandV1.SettleSource(f.Operation, f.Position(12), f.Position(20)),
        ];

        foreach (var command in commands)
        {
            var bytes = GraphReplacementCodecsV1.EncodeCommand(command);
            Assert.True(GraphReplacementCodecsV1.TryDecodeCommand(bytes, out var decoded));
            Assert.Equal(bytes, GraphReplacementCodecsV1.EncodeCommand(decoded!));
        }
    }

    [Fact]
    public void Installed_snapshot_and_all_valid_phase_arms_round_trip()
    {
        var f = new Fixture();
        var installed = new GraphTopologyInstalledBodyV1(f.Source, f.Source.Fingerprint, f.Position(3), f.Authority);
        var installedBytes = GraphReplacementCodecsV1.EncodeInstalled(installed);
        Assert.True(GraphReplacementCodecsV1.TryDecodeInstalled(installedBytes, out var decodedInstalled));
        Assert.Equal(installedBytes, GraphReplacementCodecsV1.EncodeInstalled(decodedInstalled!));

        foreach (var snapshot in f.ValidSnapshots())
        {
            var outcome = snapshot.Phase switch
            {
                GraphReplacementPhaseV1.Prepared => GraphReplacementJournalOutcomeV1.Prepared,
                GraphReplacementPhaseV1.Committed => GraphReplacementJournalOutcomeV1.Committed,
                GraphReplacementPhaseV1.SourceSettled => GraphReplacementJournalOutcomeV1.SourceSettled,
                _ => GraphReplacementJournalOutcomeV1.Rejected,
            };
            BoundedAscii? code = outcome == GraphReplacementJournalOutcomeV1.Rejected
                ? new BoundedAscii("not-prepared")
                : null;
            var fact = new GraphReplacementFactBodyV1(f.Position(11), f.Position(10), f.Position(10), outcome, snapshot, code);
            var bytes = GraphReplacementCodecsV1.EncodeFact(fact);
            Assert.True(GraphReplacementCodecsV1.TryDecodeFact(bytes, out var decoded));
            Assert.Equal(bytes, GraphReplacementCodecsV1.EncodeFact(decoded!));
        }
    }

    [Fact]
    public void Contradictory_phase_and_safe_code_arms_fail_closed()
    {
        var f = new Fixture();
        Assert.ThrowsAny<Exception>(() => GraphReplacementCodecsV1.EncodeFact(
            new GraphReplacementFactBodyV1(f.Position(11), f.Position(10), f.Position(10),
                GraphReplacementJournalOutcomeV1.Prepared, f.ValidSnapshots().ElementAt(1), new BoundedAscii("wrong"))));
        Assert.ThrowsAny<Exception>(() => GraphReplacementCodecsV1.EncodeFact(
            new GraphReplacementFactBodyV1(f.Position(11), f.Position(10), f.Position(10),
                GraphReplacementJournalOutcomeV1.Rejected, f.ValidSnapshots().First(), null)));
        Assert.ThrowsAny<Exception>(() => GraphReplacementCodecsV1.EncodeFact(
            new GraphReplacementFactBodyV1(f.Position(11), f.Position(10), f.Position(10),
                GraphReplacementJournalOutcomeV1.GenerationReplaced, f.ValidSnapshots().First(), new BoundedAscii("wrong"))));

        var malformed = f.ValidSnapshots().First() with
        {
            Phase = GraphReplacementPhaseV1.Prepared,
            Target = null,
            Replacement = null,
        };
        Assert.ThrowsAny<Exception>(() => GraphReplacementCodecsV1.EncodeFact(
            new GraphReplacementFactBodyV1(f.Position(11), f.Position(10), f.Position(10),
                GraphReplacementJournalOutcomeV1.Rejected, malformed, new BoundedAscii("invalid"))));
        Assert.ThrowsAny<Exception>(() => GraphReplacementCodecsV1.EncodeFact(
            new GraphReplacementFactBodyV1(f.Position(11), f.Position(10), f.Position(10),
                (GraphReplacementJournalOutcomeV1)7, f.ValidSnapshots().First(), new BoundedAscii("unknown"))));
    }

    [Fact]
    public void None_arms_are_explicit_two_field_unions_with_empty_payloads()
    {
        var f = new Fixture();
        var fact = new GraphReplacementFactBodyV1(f.Position(11), f.Position(10), f.Position(10),
            GraphReplacementJournalOutcomeV1.Rejected, f.ValidSnapshots().First(), new BoundedAscii("not-prepared"));
        var hex = Convert.ToHexString(GraphReplacementCodecsV1.EncodeFact(fact)).ToLowerInvariant();

        Assert.Equal(4, Count(hex, "a201000240"));
    }

    [Fact]
    public void Installed_and_prepare_payloads_require_exact_inner_outer_authority_joins()
    {
        var f = new Fixture();
        var other = ExpectedAuthorityVectorV1.Create(f.Session,
            [new AuthorityAxisValueV1.Graph(GraphGenerationId.Create())]);
        var badInstalled = new GraphTopologyInstalledBodyV1(f.Source, Hash256.Compute([1]), f.Position(3), f.Authority);
        Assert.ThrowsAny<Exception>(() => GraphReplacementCodecsV1.EncodeInstalled(badInstalled));

        var installed = new GraphTopologyInstalledBodyV1(f.Source, f.Source.Fingerprint, f.Position(3), f.Authority);
        var installedOuter = GraphReplacementCodecsV1.EncodeOuter(new GraphOwnerPayloadV1(
            f.Session, other, GraphReplacementCodecsV1.EncodeInstalled(installed)));
        Assert.False(GraphReplacementPayloadRegistrationsV1.Installed.Validate(installedOuter, f.Session));

        var prepare = new GraphReplacementJournalCommandV1.Prepare(f.Operation, f.Position(10), f.Source.Fingerprint,
            f.Target, f.Position(4), f.Authority, f.Observed, f.Deadline);
        var prepareOuter = GraphReplacementCodecsV1.EncodeOuter(new GraphOwnerPayloadV1(
            f.Session, other, GraphReplacementCodecsV1.EncodeCommand(prepare)));
        Assert.False(GraphReplacementPayloadRegistrationsV1.Command.Validate(prepareOuter, f.Session));
    }

    [Fact]
    public void Outer_body_bound_is_exact_and_owned_before_encoding()
    {
        var f = new Fixture();
        Assert.NotNull(new GraphOwnerPayloadV1(f.Session, f.Authority,
            new byte[GraphReplacementCodecsV1.MaximumBodyBytes]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphOwnerPayloadV1(f.Session, f.Authority,
            new byte[GraphReplacementCodecsV1.MaximumBodyBytes + 1]));
    }

    [Fact]
    public void Registrations_reject_cross_session_and_missing_or_wrong_graph_axes()
    {
        var f = new Fixture();
        var other = new Fixture(40);
        var noGraph = ExpectedAuthorityVectorV1.Create(f.Session, []);

        byte[] Outer(ExpectedAuthorityVectorV1 authority, byte[] body) =>
            GraphReplacementCodecsV1.EncodeOuter(new GraphOwnerPayloadV1(f.Session, authority, body));

        var commit = new GraphReplacementJournalCommandV1.Commit(f.Operation, other.Position(10));
        var settle = new GraphReplacementJournalCommandV1.SettleSource(f.Operation, other.Position(10), other.Position(20));
        Assert.False(GraphReplacementPayloadRegistrationsV1.Command.Validate(Outer(f.Authority,
            GraphReplacementCodecsV1.EncodeCommand(commit)), f.Session));
        Assert.False(GraphReplacementPayloadRegistrationsV1.Command.Validate(Outer(f.Authority,
            GraphReplacementCodecsV1.EncodeCommand(settle)), f.Session));

        var otherFact = new GraphReplacementFactBodyV1(other.Position(11), other.Position(10), other.Position(10),
            GraphReplacementJournalOutcomeV1.Rejected, other.ValidSnapshots().First(), new BoundedAscii("rejected"));
        Assert.False(GraphReplacementPayloadRegistrationsV1.Fact.Validate(Outer(f.Authority,
            GraphReplacementCodecsV1.EncodeFact(otherFact)), f.Session));

        var ownCommit = new GraphReplacementJournalCommandV1.Commit(f.Operation, f.Position(10));
        Assert.False(GraphReplacementPayloadRegistrationsV1.Command.Validate(Outer(noGraph,
            GraphReplacementCodecsV1.EncodeCommand(ownCommit)), f.Session));
        var installed = new GraphTopologyInstalledBodyV1(f.Source, f.Source.Fingerprint, f.Position(3), f.Authority);
        Assert.False(GraphReplacementPayloadRegistrationsV1.Installed.Validate(Outer(noGraph,
            GraphReplacementCodecsV1.EncodeInstalled(installed)), f.Session));
        var fact = new GraphReplacementFactBodyV1(f.Position(11), f.Position(10), f.Position(10),
            GraphReplacementJournalOutcomeV1.Rejected, f.ValidSnapshots().First(), new BoundedAscii("rejected"));
        Assert.False(GraphReplacementPayloadRegistrationsV1.Fact.Validate(Outer(noGraph,
            GraphReplacementCodecsV1.EncodeFact(fact)), f.Session));

        var wrongGraph = ExpectedAuthorityVectorV1.Create(f.Session,
            [new AuthorityAxisValueV1.Graph(GraphGenerationId.Create())]);
        var wrongInstalled = new GraphTopologyInstalledBodyV1(f.Source, f.Source.Fingerprint, f.Position(3), wrongGraph);
        Assert.False(GraphReplacementPayloadRegistrationsV1.Installed.Validate(Outer(wrongGraph,
            GraphReplacementCodecsV1.EncodeInstalled(wrongInstalled)), f.Session));
    }

    [Fact]
    public void Outer_payload_is_canonical_session_bound_and_registrations_validate_inner_schema()
    {
        var f = new Fixture();
        var command = new GraphReplacementJournalCommandV1.Commit(f.Operation, f.Position(10));
        var outer = new GraphOwnerPayloadV1(f.Session, f.Authority, GraphReplacementCodecsV1.EncodeCommand(command));
        var bytes = GraphReplacementCodecsV1.EncodeOuter(outer);

        Assert.True(GraphReplacementCodecsV1.TryDecodeOuter(bytes, out var decoded));
        Assert.Equal(f.Session, decoded!.Session);
        Assert.Equal(outer.Body.ToArray(), decoded.Body.ToArray());
        Assert.True(GraphReplacementPayloadRegistrationsV1.Command.Validate(bytes, f.Session));
        Assert.False(GraphReplacementPayloadRegistrationsV1.Fact.Validate(bytes, f.Session));
        Assert.False(GraphReplacementPayloadRegistrationsV1.Command.Validate(bytes,
            new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create())));

        var changed = bytes.Concat(new byte[] { 0 }).ToArray();
        Assert.False(GraphReplacementPayloadRegistrationsV1.Command.Validate(changed, f.Session));
    }

    [Fact]
    public void Canonical_bytes_have_fixed_integrity_golden()
    {
        var f = new Fixture();
        var command = new GraphReplacementJournalCommandV1.Commit(f.Operation, f.Position(10));
        var bytes = GraphReplacementCodecsV1.EncodeCommand(command);
        Assert.Equal("a2010202583da2015008090a0b0c0d0e0f101112131415161702a201a201500102030405060708090a0b0c0d0e0f10025002030405060708090a0b0c0d0e0f1011020a",
            Convert.ToHexString(bytes).ToLowerInvariant());
        Assert.Equal("f4fd59b69f6b04eddd8848cbe16f0030a30618636a14af445a45a8400eb56cc3",
            GraphReplacementCodecsV1.Hash("hpd.graph-replacement-command.v1", bytes).ToString());
    }

    private sealed class Fixture
    {
        internal SessionAuthorityStampV1 Session { get; }
        internal OperationId Operation { get; }
        internal ExpectedAuthorityVectorV1 Authority { get; }
        internal GraphTopologyPlanV1 Source { get; }
        internal GraphTopologyPlanV1 Target { get; }
        internal MonotonicStampV1 Observed { get; }
        internal MonotonicStampV1 Deadline { get; }

        internal Fixture(byte offset = 0)
        {
            Session = new(RuntimeGenerationId.FromValue(Id(checked((byte)(1 + offset)))),
                LiveSessionId.FromValue(Id(checked((byte)(2 + offset)))));
            Operation = OperationId.FromValue(Id(checked((byte)(8 + offset))));
            var sourceGeneration = GraphGenerationId.FromValue(Id(checked((byte)(3 + offset))));
            Authority = ExpectedAuthorityVectorV1.Create(Session, [new AuthorityAxisValueV1.Graph(sourceGeneration)]);
            Source = Plan(sourceGeneration, CapacityGrantId.FromValue(Id(checked((byte)(4 + offset)))), "source");
            Target = Plan(GraphGenerationId.FromValue(Id(checked((byte)(5 + offset)))),
                CapacityGrantId.FromValue(Id(checked((byte)(6 + offset)))), "target");
            var clock = ClockDomainId.FromValue(Id(checked((byte)(10 + offset))));
            var boot = BootId.FromValue(Id(checked((byte)(11 + offset))));
            Observed = new MonotonicStampV1(clock, boot, 100);
            Deadline = new MonotonicStampV1(clock, boot, 200);
        }

        internal JournalPositionV1 Position(long sequence) => new(Session, sequence);

        internal IEnumerable<GraphReplacementSnapshotWireV1> ValidSnapshots()
        {
            yield return new(GraphReplacementPhaseV1.None, Source, Position(3), null, Authority, Position(10), null, null, null);
            var target = new GraphReplacementTargetArmV1(Target, Position(4));
            var replacement = new GraphReplacementIdentityArmV1(Operation, Position(11));
            yield return new(GraphReplacementPhaseV1.Prepared, Source, Position(3), target, Authority, Position(12), replacement, null, null);
            var commit = new GraphReplacementCommitArmV1(Position(13), Position(15));
            yield return new(GraphReplacementPhaseV1.Committed, Source, Position(3), target, Authority, Position(14), replacement, commit, null);
            yield return new(GraphReplacementPhaseV1.SourceSettled, Source, Position(3), target, Authority, Position(16), replacement, commit,
                new GraphReplacementSettlementArmV1(Position(16), Position(20)));
        }

        private GraphTopologyPlanV1 Plan(GraphGenerationId generation, CapacityGrantId grant, string key) =>
            new(Session, generation, grant, [new GraphTopologyNodeV1(new BoundedAscii(key))], [], [new CapacityDimensionId(3)]);
    }

    private static StableId128 Id(byte seed)
    {
        Span<byte> bytes = stackalloc byte[16];
        for (var index = 0; index < bytes.Length; index++) bytes[index] = checked((byte)(seed + index));
        return StableId128.FromBytes(bytes);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var offset = 0; (offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0; offset += value.Length)
            count++;
        return count;
    }
}
