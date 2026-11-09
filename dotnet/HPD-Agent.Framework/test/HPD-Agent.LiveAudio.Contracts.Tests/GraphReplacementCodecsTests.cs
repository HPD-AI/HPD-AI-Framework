using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphReplacementCodecsTests
{
    [Fact]
    public void Ledger_outer_projection_types_preserve_the_shared_canonical_shape()
    {
        var f = new Fixture();
        var commandBody = GraphReplacementCodecs.EncodeCommand(new GraphReplacementJournalCommand.Commit(f.Operation, f.Position(10)));
        var installedBody = GraphReplacementCodecs.EncodeInstalled(new GraphTopologyInstalled(f.Source, f.Source.Fingerprint, f.Position(3), f.Authority));
        var command = new GraphMutationCommand(f.Session, f.Authority, commandBody);
        var installed = new GraphTopologyInstalledFact(f.Session, f.Authority, installedBody);

        Assert.IsAssignableFrom<GraphOwnerPayload>(command);
        Assert.IsAssignableFrom<GraphOwnerPayload>(installed);
        Assert.True(GraphReplacementCodecs.TryDecodeOuter(GraphReplacementCodecs.EncodeOuter(command), out var decodedCommand));
        Assert.True(GraphReplacementCodecs.TryDecodeOuter(GraphReplacementCodecs.EncodeOuter(installed), out var decodedInstalled));
        Assert.Equal(command.Body, decodedCommand!.Body);
        Assert.Equal(installed.Body, decodedInstalled!.Body);
    }

    [Fact]
    public void Every_command_arm_round_trips_canonically()
    {
        var f = new Fixture();
        GraphReplacementJournalCommand[] commands =
        [
            new GraphReplacementJournalCommand.Prepare(f.Operation, f.Position(10), f.Source.Fingerprint,
                f.Target, f.Position(4), f.Authority, f.Observed, f.Deadline),
            new GraphReplacementJournalCommand.Commit(f.Operation, f.Position(11)),
            new GraphReplacementJournalCommand.SettleSource(f.Operation, f.Position(12), f.Position(20)),
        ];

        foreach (var command in commands)
        {
            var bytes = GraphReplacementCodecs.EncodeCommand(command);
            Assert.True(GraphReplacementCodecs.TryDecodeCommand(bytes, out var decoded));
            Assert.Equal(bytes, GraphReplacementCodecs.EncodeCommand(decoded!));
        }
    }

    [Fact]
    public void Installed_snapshot_and_all_valid_phase_arms_round_trip()
    {
        var f = new Fixture();
        var installed = new GraphTopologyInstalled(f.Source, f.Source.Fingerprint, f.Position(3), f.Authority);
        var installedBytes = GraphReplacementCodecs.EncodeInstalled(installed);
        Assert.True(GraphReplacementCodecs.TryDecodeInstalled(installedBytes, out var decodedInstalled));
        Assert.Equal(installedBytes, GraphReplacementCodecs.EncodeInstalled(decodedInstalled!));

        foreach (var snapshot in f.ValidSnapshots())
        {
            var outcome = snapshot.Phase switch
            {
                GraphReplacementPhase.Prepared => GraphReplacementJournalOutcome.Prepared,
                GraphReplacementPhase.Committed => GraphReplacementJournalOutcome.Committed,
                GraphReplacementPhase.SourceSettled => GraphReplacementJournalOutcome.SourceSettled,
                _ => GraphReplacementJournalOutcome.Rejected,
            };
            BoundedAscii? code = outcome == GraphReplacementJournalOutcome.Rejected
                ? new BoundedAscii("not-prepared")
                : null;
            var fact = new GraphReplacementFact(f.Position(11), f.Position(10), f.Position(10), outcome, snapshot, code);
            var bytes = GraphReplacementCodecs.EncodeFact(fact);
            Assert.True(GraphReplacementCodecs.TryDecodeFact(bytes, out var decoded));
            Assert.Equal(bytes, GraphReplacementCodecs.EncodeFact(decoded!));
        }
    }

    [Fact]
    public void Contradictory_phase_and_safe_code_arms_fail_closed()
    {
        var f = new Fixture();
        Assert.ThrowsAny<Exception>(() => GraphReplacementCodecs.EncodeFact(
            new GraphReplacementFact(f.Position(11), f.Position(10), f.Position(10),
                GraphReplacementJournalOutcome.Prepared, f.ValidSnapshots().ElementAt(1), new BoundedAscii("wrong"))));
        Assert.ThrowsAny<Exception>(() => GraphReplacementCodecs.EncodeFact(
            new GraphReplacementFact(f.Position(11), f.Position(10), f.Position(10),
                GraphReplacementJournalOutcome.Rejected, f.ValidSnapshots().First(), null)));
        Assert.ThrowsAny<Exception>(() => GraphReplacementCodecs.EncodeFact(
            new GraphReplacementFact(f.Position(11), f.Position(10), f.Position(10),
                GraphReplacementJournalOutcome.GenerationReplaced, f.ValidSnapshots().First(), new BoundedAscii("wrong"))));

        var malformed = f.ValidSnapshots().First() with
        {
            Phase = GraphReplacementPhase.Prepared,
            Target = null,
            Replacement = null,
        };
        Assert.ThrowsAny<Exception>(() => GraphReplacementCodecs.EncodeFact(
            new GraphReplacementFact(f.Position(11), f.Position(10), f.Position(10),
                GraphReplacementJournalOutcome.Rejected, malformed, new BoundedAscii("invalid"))));
        Assert.ThrowsAny<Exception>(() => GraphReplacementCodecs.EncodeFact(
            new GraphReplacementFact(f.Position(11), f.Position(10), f.Position(10),
                (GraphReplacementJournalOutcome)7, f.ValidSnapshots().First(), new BoundedAscii("unknown"))));
    }

    [Fact]
    public void None_arms_are_explicit_two_field_unions_with_empty_payloads()
    {
        var f = new Fixture();
        var fact = new GraphReplacementFact(f.Position(11), f.Position(10), f.Position(10),
            GraphReplacementJournalOutcome.Rejected, f.ValidSnapshots().First(), new BoundedAscii("not-prepared"));
        var hex = Convert.ToHexString(GraphReplacementCodecs.EncodeFact(fact)).ToLowerInvariant();

        Assert.Equal(4, Count(hex, "a201000240"));
    }

    [Fact]
    public void Installed_and_prepare_payloads_require_exact_inner_outer_authority_joins()
    {
        var f = new Fixture();
        var other = ExpectedAuthorityVectorV1.Create(f.Session,
            [new AuthorityAxisValueV1.Graph(GraphGenerationId.Create())]);
        var badInstalled = new GraphTopologyInstalled(f.Source, Hash256.Compute([1]), f.Position(3), f.Authority);
        Assert.ThrowsAny<Exception>(() => GraphReplacementCodecs.EncodeInstalled(badInstalled));

        var installed = new GraphTopologyInstalled(f.Source, f.Source.Fingerprint, f.Position(3), f.Authority);
        var installedOuter = GraphReplacementCodecs.EncodeOuter(new GraphOwnerPayload(
            f.Session, other, GraphReplacementCodecs.EncodeInstalled(installed)));
        Assert.False(GraphReplacementPayloadRegistrations.Installed.Validate(installedOuter, f.Session));

        var prepare = new GraphReplacementJournalCommand.Prepare(f.Operation, f.Position(10), f.Source.Fingerprint,
            f.Target, f.Position(4), f.Authority, f.Observed, f.Deadline);
        var prepareOuter = GraphReplacementCodecs.EncodeOuter(new GraphOwnerPayload(
            f.Session, other, GraphReplacementCodecs.EncodeCommand(prepare)));
        Assert.False(GraphReplacementPayloadRegistrations.Command.Validate(prepareOuter, f.Session));
    }

    [Fact]
    public void Outer_body_bound_is_exact_and_owned_before_encoding()
    {
        var f = new Fixture();
        Assert.NotNull(new GraphOwnerPayload(f.Session, f.Authority,
            new byte[GraphReplacementCodecs.MaximumBodyBytes]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphOwnerPayload(f.Session, f.Authority,
            new byte[GraphReplacementCodecs.MaximumBodyBytes + 1]));
    }

    [Fact]
    public void Registrations_reject_cross_session_and_missing_or_wrong_graph_axes()
    {
        var f = new Fixture();
        var other = new Fixture(40);
        var noGraph = ExpectedAuthorityVectorV1.Create(f.Session, []);

        byte[] Outer(ExpectedAuthorityVectorV1 authority, byte[] body) =>
            GraphReplacementCodecs.EncodeOuter(new GraphOwnerPayload(f.Session, authority, body));

        var commit = new GraphReplacementJournalCommand.Commit(f.Operation, other.Position(10));
        var settle = new GraphReplacementJournalCommand.SettleSource(f.Operation, other.Position(10), other.Position(20));
        Assert.False(GraphReplacementPayloadRegistrations.Command.Validate(Outer(f.Authority,
            GraphReplacementCodecs.EncodeCommand(commit)), f.Session));
        Assert.False(GraphReplacementPayloadRegistrations.Command.Validate(Outer(f.Authority,
            GraphReplacementCodecs.EncodeCommand(settle)), f.Session));

        var otherFact = new GraphReplacementFact(other.Position(11), other.Position(10), other.Position(10),
            GraphReplacementJournalOutcome.Rejected, other.ValidSnapshots().First(), new BoundedAscii("rejected"));
        Assert.False(GraphReplacementPayloadRegistrations.Fact.Validate(Outer(f.Authority,
            GraphReplacementCodecs.EncodeFact(otherFact)), f.Session));

        var ownCommit = new GraphReplacementJournalCommand.Commit(f.Operation, f.Position(10));
        Assert.False(GraphReplacementPayloadRegistrations.Command.Validate(Outer(noGraph,
            GraphReplacementCodecs.EncodeCommand(ownCommit)), f.Session));
        var installed = new GraphTopologyInstalled(f.Source, f.Source.Fingerprint, f.Position(3), f.Authority);
        Assert.False(GraphReplacementPayloadRegistrations.Installed.Validate(Outer(noGraph,
            GraphReplacementCodecs.EncodeInstalled(installed)), f.Session));
        var fact = new GraphReplacementFact(f.Position(11), f.Position(10), f.Position(10),
            GraphReplacementJournalOutcome.Rejected, f.ValidSnapshots().First(), new BoundedAscii("rejected"));
        Assert.False(GraphReplacementPayloadRegistrations.Fact.Validate(Outer(noGraph,
            GraphReplacementCodecs.EncodeFact(fact)), f.Session));

        var wrongGraph = ExpectedAuthorityVectorV1.Create(f.Session,
            [new AuthorityAxisValueV1.Graph(GraphGenerationId.Create())]);
        var wrongInstalled = new GraphTopologyInstalled(f.Source, f.Source.Fingerprint, f.Position(3), wrongGraph);
        Assert.False(GraphReplacementPayloadRegistrations.Installed.Validate(Outer(wrongGraph,
            GraphReplacementCodecs.EncodeInstalled(wrongInstalled)), f.Session));
    }

    [Fact]
    public void Outer_payload_is_canonical_session_bound_and_registrations_validate_inner_schema()
    {
        var f = new Fixture();
        var command = new GraphReplacementJournalCommand.Commit(f.Operation, f.Position(10));
        var outer = new GraphOwnerPayload(f.Session, f.Authority, GraphReplacementCodecs.EncodeCommand(command));
        var bytes = GraphReplacementCodecs.EncodeOuter(outer);

        Assert.True(GraphReplacementCodecs.TryDecodeOuter(bytes, out var decoded));
        Assert.Equal(f.Session, decoded!.Session);
        Assert.Equal(outer.Body.ToArray(), decoded.Body.ToArray());
        Assert.True(GraphReplacementPayloadRegistrations.Command.Validate(bytes, f.Session));
        Assert.False(GraphReplacementPayloadRegistrations.Fact.Validate(bytes, f.Session));
        Assert.False(GraphReplacementPayloadRegistrations.Command.Validate(bytes,
            new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create())));

        var changed = bytes.Concat(new byte[] { 0 }).ToArray();
        Assert.False(GraphReplacementPayloadRegistrations.Command.Validate(changed, f.Session));
    }

    [Fact]
    public void Canonical_bytes_have_fixed_integrity_golden()
    {
        var f = new Fixture();
        var command = new GraphReplacementJournalCommand.Commit(f.Operation, f.Position(10));
        var bytes = GraphReplacementCodecs.EncodeCommand(command);
        Assert.Equal("a2010202583da2015008090a0b0c0d0e0f101112131415161702a201a201500102030405060708090a0b0c0d0e0f10025002030405060708090a0b0c0d0e0f1011020a",
            Convert.ToHexString(bytes).ToLowerInvariant());
        Assert.Equal("f4fd59b69f6b04eddd8848cbe16f0030a30618636a14af445a45a8400eb56cc3",
            GraphReplacementCodecs.Hash("hpd.graph-replacement-command.v1", bytes).ToString());
    }

    private sealed class Fixture
    {
        internal SessionAuthorityStampV1 Session { get; }
        internal OperationId Operation { get; }
        internal ExpectedAuthorityVectorV1 Authority { get; }
        internal GraphTopologyPlan Source { get; }
        internal GraphTopologyPlan Target { get; }
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

        internal IEnumerable<GraphReplacementSnapshot> ValidSnapshots()
        {
            yield return new(GraphReplacementPhase.None, Source, Position(3), null, Authority, Position(10), null, null, null);
            var target = new GraphReplacementTargetArm(Target, Position(4));
            var replacement = new GraphReplacementIdentityArm(Operation, Position(11));
            yield return new(GraphReplacementPhase.Prepared, Source, Position(3), target, Authority, Position(12), replacement, null, null);
            var commit = new GraphReplacementCommitArm(Position(13), Position(15));
            yield return new(GraphReplacementPhase.Committed, Source, Position(3), target, Authority, Position(14), replacement, commit, null);
            yield return new(GraphReplacementPhase.SourceSettled, Source, Position(3), target, Authority, Position(16), replacement, commit,
                new GraphReplacementSettlementArm(Position(16), Position(20)));
        }

        private GraphTopologyPlan Plan(GraphGenerationId generation, CapacityGrantId grant, string key) =>
            new(Session, generation, grant, [new GraphTopologyNode(new BoundedAscii(key))], [], [new CapacityDimensionId(3)]);
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
