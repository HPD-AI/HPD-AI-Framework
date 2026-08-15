using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class SessionLifecycleBodyV1Tests
{
    [Fact]
    public void ReserveAndStartingFact_MatchIndependentGoldens()
    {
        var reserve = new SessionLifecycleCommandBodyV1.ReserveStarting(Operation(), Hash256.Compute("start"u8));
        var commandPosition = Position(1);
        var fact = new SessionLifecycleFactBodyV1(Operation(), commandPosition, null, null,
            SessionLifecycleOutcomeV1.Applied, Snapshot(SessionLifecycleStateWireV1.Starting), null);
        Assert.Equal(
            "a401010250000102030405060708090a0b0c0d0e0f03a1010004a1015820cced28c6dc3f99c2396a5eaad732bf6b28142335892b1cd0e6af6cdb53f5ccfa",
            Convert.ToHexString(SessionLifecycleBodyCodecsV1.Encode(reserve)).ToLowerInvariant());
        Assert.Equal(
            "a70150000102030405060708090a0b0c0d0e0f02a201a20150101112131415161718191a1b1c1d1e1f0250202122232425262728292a2b2c2d2e2f020103a1010004a10100050106ac0101020203010401050006000700080009000a000b010cf407a10100",
            Convert.ToHexString(SessionLifecycleBodyCodecsV1.Encode(fact)).ToLowerInvariant());
    }

    [Fact]
    public void AllSixCommands_RoundTripWithClosedKindsAndExactPredecessorRules()
    {
        var position = Position(7);
        SessionLifecycleCommandBodyV1[] values =
        [
            new SessionLifecycleCommandBodyV1.ReserveStarting(Operation(), Hash256.Compute("start"u8)),
            new SessionLifecycleCommandBodyV1.PublishReady(Operation(), position, SessionAvailabilityWireV1.Available),
            new SessionLifecycleCommandBodyV1.BeginDrain(Operation(), position),
            new SessionLifecycleCommandBodyV1.BeginTermination(Operation(), position,
                SessionTerminalIntentWireV1.Fault, SessionTerminalCauseWireV1.StartFailed,
                SessionTerminalSeverityWireV1.Recoverable, SessionConvergencePhaseWireV1.Quiescing),
            new SessionLifecycleCommandBodyV1.AdvanceTermination(Operation(), position,
                SessionConvergencePhaseWireV1.Finalizing, SessionTerminalIntentWireV1.Abort,
                SessionTerminalCauseWireV1.HostForced, SessionTerminalSeverityWireV1.Fatal, true),
            new SessionLifecycleCommandBodyV1.Complete(Operation(), position, true),
        ];

        foreach (var expected in values)
        {
            Assert.True(SessionLifecycleBodyCodecsV1.TryDecodeCommand(
                SessionLifecycleBodyCodecsV1.Encode(expected), out var actual));
            Assert.Equal(expected.Kind, actual!.Kind);
            Assert.Equal(expected.OperationId, actual.OperationId);
            Assert.Equal(expected.ExpectedLifecycleFact, actual.ExpectedLifecycleFact);
            Assert.Equal(expected.GetType(), actual.GetType());
            AssertVariant(expected, actual);
        }
        Assert.Null(values[0].ExpectedLifecycleFact);
        Assert.All(values.Skip(1), static value => Assert.NotNull(value.ExpectedLifecycleFact));
    }

    [Fact]
    public void CommandAndFact_MaximumEncodingsMatchFrozenBounds()
    {
        var position = Position(long.MaxValue);
        var command = new SessionLifecycleCommandBodyV1.AdvanceTermination(
            Operation(), position, SessionConvergencePhaseWireV1.Containing,
            SessionTerminalIntentWireV1.DeadlineContainment, SessionTerminalCauseWireV1.PolicyRevoked,
            SessionTerminalSeverityWireV1.Fatal, true);
        Assert.Equal(SessionLifecycleBodyCodecsV1.MaximumCommandBytes,
            SessionLifecycleBodyCodecsV1.Encode(command).Length);

        var fact = new SessionLifecycleFactBodyV1(
            command.OperationId, position, position, position, SessionLifecycleOutcomeV1.Rejected,
            Snapshot(SessionLifecycleStateWireV1.Terminating), new BoundedAscii(new string('x', 64)));
        var encoded = SessionLifecycleBodyCodecsV1.Encode(fact);
        Assert.Equal(SessionLifecycleBodyCodecsV1.MaximumFactBytes, encoded.Length);
        Assert.True(SessionLifecycleBodyCodecsV1.TryDecodeFact(encoded, out var decoded));
        Assert.Equal(fact.OperationId, decoded!.OperationId);
        Assert.Equal(fact.CommandPosition, decoded.CommandPosition);
        Assert.Equal(fact.CommandExpectedLifecycleFact, decoded.CommandExpectedLifecycleFact);
        Assert.Equal(fact.PreviousLifecycleFact, decoded.PreviousLifecycleFact);
        Assert.Equal(fact.Snapshot, decoded.Snapshot);
        Assert.Equal(fact.SafeCode, decoded.SafeCode);
    }

    [Fact]
    public void FactOutcome_EnforcesExactSafeCodeUnion()
    {
        var position = Position(2);
        var snapshot = Snapshot(SessionLifecycleStateWireV1.Starting);
        Assert.Throws<ArgumentException>(() => new SessionLifecycleFactBodyV1(
            Operation(), position, null, null, SessionLifecycleOutcomeV1.Rejected, snapshot, null));
        Assert.Throws<ArgumentException>(() => new SessionLifecycleFactBodyV1(
            Operation(), position, null, null, SessionLifecycleOutcomeV1.Applied, snapshot, new BoundedAscii("wrong")));
        Assert.Throws<ArgumentException>(() => new SessionLifecycleFactBodyV1(
            Operation(), position, null, null, SessionLifecycleOutcomeV1.Rejected, snapshot,
            new BoundedAscii(new string('x', 65))));
    }

    [Theory]
    [InlineData("a0")]
    [InlineData("bf0101ff")]
    [InlineData("a401070250000102030405060708090a0b0c0d0e0f03a1010004a0")]
    public void CommandDecoder_RejectsMalformedIndefiniteAndUnknownKinds(string hex) =>
        Assert.False(SessionLifecycleBodyCodecsV1.TryDecodeCommand(Convert.FromHexString(hex), out _));

    [Fact]
    public void Decoders_RejectAboveExactBoundsBeforeParsing()
    {
        Assert.False(SessionLifecycleBodyCodecsV1.TryDecodeCommand(
            new byte[SessionLifecycleBodyCodecsV1.MaximumCommandBytes + 1], out _));
        Assert.False(SessionLifecycleBodyCodecsV1.TryDecodeFact(
            new byte[SessionLifecycleBodyCodecsV1.MaximumFactBytes + 1], out _));
    }

    [Fact]
    public void OuterRegistrations_RejectOpaqueBodiesThatAreNotExactInnerProtocol()
    {
        var (session, vector) = Authority();
        var invalidCommand = new SessionLifecycleCommandV1(session, vector, [0xff]);
        var invalidFact = new SessionLifecycleFactV1(session, vector, [0xff]);
        Assert.False(new SessionLifecycleCommandPayloadRegistrationV1().Validate(
            SessionLifecyclePayloadV1Codec.Encode(invalidCommand), session));
        Assert.False(new SessionLifecycleFactPayloadRegistrationV1().Validate(
            SessionLifecyclePayloadV1Codec.Encode(invalidFact), session));

        var validCommand = new SessionLifecycleCommandV1(session, vector,
            SessionLifecycleBodyCodecsV1.Encode(new SessionLifecycleCommandBodyV1.ReserveStarting(
                Operation(), Hash256.Compute("request"u8))));
        Assert.True(new SessionLifecycleCommandPayloadRegistrationV1().Validate(
            SessionLifecyclePayloadV1Codec.Encode(validCommand), session));
    }

    [Fact]
    public void OuterRegistrations_RejectInnerPositionsFromAnotherSession()
    {
        var (session, vector) = Authority();
        var other = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var otherPosition = new JournalPositionV1(other, 1);
        var commandBody = new SessionLifecycleCommandBodyV1.BeginDrain(Operation(), otherPosition);
        var command = new SessionLifecycleCommandV1(session, vector, SessionLifecycleBodyCodecsV1.Encode(commandBody));
        Assert.False(new SessionLifecycleCommandPayloadRegistrationV1().Validate(
            SessionLifecyclePayloadV1Codec.Encode(command), session));

        var factBody = new SessionLifecycleFactBodyV1(Operation(), otherPosition, null, null,
            SessionLifecycleOutcomeV1.Applied, Snapshot(SessionLifecycleStateWireV1.Starting), null);
        var fact = new SessionLifecycleFactV1(session, vector, SessionLifecycleBodyCodecsV1.Encode(factBody));
        Assert.False(new SessionLifecycleFactPayloadRegistrationV1().Validate(
            SessionLifecyclePayloadV1Codec.Encode(fact), session));
    }

    [Fact]
    public void CodecSource_ContainsNoReflectionBasedEnumConversion()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "HPD-Agent", "Authority", "Wire", "SessionLifecycleBodyCodecsV1.cs"));
        Assert.DoesNotContain("Enum.ToObject", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FactDecoder_RejectsCrossSessionPredecessorAndInvalidSafeCodeShape()
    {
        var command = Position(5);
        var otherSession = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        Assert.Throws<ArgumentException>(() => new SessionLifecycleFactBodyV1(
            Operation(), command, new JournalPositionV1(otherSession, 4), command,
            SessionLifecycleOutcomeV1.Applied, Snapshot(SessionLifecycleStateWireV1.Active), null));

        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(1); writer.WriteUInt64(1); writer.WriteUInt64(2); writer.WriteEndMap();
        Assert.False(SessionLifecycleBodyCodecsV1.TryDecodeFact(writer.Encode(), out _));
    }

    private static SessionLifecycleSnapshotBodyV1 Snapshot(SessionLifecycleStateWireV1 state) => new(
        state,
        state == SessionLifecycleStateWireV1.Active ? SessionAdmissionWireV1.Open : SessionAdmissionWireV1.Closed,
        state == SessionLifecycleStateWireV1.Active ? SessionAvailabilityWireV1.Available : SessionAvailabilityWireV1.Unavailable,
        state == SessionLifecycleStateWireV1.Starting ? SessionReadinessWireV1.Unpublished : SessionReadinessWireV1.Succeeded,
        SessionTerminalIntentWireV1.None, SessionTerminalCauseWireV1.None,
        SessionTerminalIntentWireV1.None, SessionTerminalCauseWireV1.None,
        SessionTerminalSeverityWireV1.None, SessionConvergencePhaseWireV1.None,
        SessionMutationFenceWireV1.Open, false);

    private static void AssertVariant(SessionLifecycleCommandBodyV1 expected, SessionLifecycleCommandBodyV1 actual)
    {
        switch (expected)
        {
            case SessionLifecycleCommandBodyV1.ReserveStarting left:
                Assert.Equal(left.AdmissionFingerprint, Assert.IsType<SessionLifecycleCommandBodyV1.ReserveStarting>(actual).AdmissionFingerprint);
                break;
            case SessionLifecycleCommandBodyV1.PublishReady left:
                Assert.Equal(left.Availability, Assert.IsType<SessionLifecycleCommandBodyV1.PublishReady>(actual).Availability);
                break;
            case SessionLifecycleCommandBodyV1.BeginDrain:
                Assert.IsType<SessionLifecycleCommandBodyV1.BeginDrain>(actual);
                break;
            case SessionLifecycleCommandBodyV1.BeginTermination left:
                var terminal = Assert.IsType<SessionLifecycleCommandBodyV1.BeginTermination>(actual);
                Assert.Equal((left.Intent, left.Cause, left.Severity, left.Phase),
                    (terminal.Intent, terminal.Cause, terminal.Severity, terminal.Phase));
                break;
            case SessionLifecycleCommandBodyV1.AdvanceTermination left:
                var advance = Assert.IsType<SessionLifecycleCommandBodyV1.AdvanceTermination>(actual);
                Assert.Equal((left.Phase, left.Intent, left.Cause, left.Severity, left.ConversationStopped),
                    (advance.Phase, advance.Intent, advance.Cause, advance.Severity, advance.ConversationStopped));
                break;
            case SessionLifecycleCommandBodyV1.Complete left:
                Assert.Equal(left.ConversationStopped, Assert.IsType<SessionLifecycleCommandBodyV1.Complete>(actual).ConversationStopped);
                break;
            default:
                throw new InvalidOperationException("Unregistered lifecycle command test variant.");
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "HPD-Agent")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the test repository root.");
    }

    private static OperationId Operation() => OperationId.FromValue(
        StableId128.FromBytes(Convert.FromHexString("000102030405060708090a0b0c0d0e0f")));

    private static JournalPositionV1 Position(long sequence)
    {
        var (session, _) = Authority();
        return new(session, sequence);
    }

    private static (SessionAuthorityStampV1 Session, ExpectedAuthorityVectorV1 Vector) Authority()
    {
        var session = new SessionAuthorityStampV1(
            RuntimeGenerationId.FromValue(StableId128.FromBytes(Convert.FromHexString("101112131415161718191a1b1c1d1e1f"))),
            LiveSessionId.FromValue(StableId128.FromBytes(Convert.FromHexString("202122232425262728292a2b2c2d2e2f"))));
        return (session, ExpectedAuthorityVectorV1.Create(session, []));
    }
}
