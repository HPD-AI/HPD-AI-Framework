using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class SessionLifecyclePayloadV1Tests
{
    private const string Golden = "a301a20150101112131415161718191a1b1c1d1e1f0250202122232425262728292a2b2c2d2e2f02a201a20150101112131415161718191a1b1c1d1e1f0250202122232425262728292a2b2c2d2e2f028003420102";

    [Fact]
    public void CommandAndFact_UseExactCanonicalShapeAndSchemaSeparatedHashes()
    {
        var (session, vector) = Authority();
        var command = new SessionLifecycleCommandV1(session, vector, [1, 2]);
        var fact = new SessionLifecycleFactV1(session, vector, [1, 2]);

        Assert.Equal(Golden, Convert.ToHexString(SessionLifecyclePayloadV1Codec.Encode(command)).ToLowerInvariant());
        Assert.Equal(Golden, Convert.ToHexString(SessionLifecyclePayloadV1Codec.Encode(fact)).ToLowerInvariant());
        Assert.Equal("8486737b3b3624a5c5e5b90f5c2d4b8a5c7081c50b7b3f41b0c32494aab99e01",
            SessionLifecyclePayloadV1Codec.ComputeIntegrityHash(command).ToString());
        Assert.Equal("0f08e7c4371a624122d293a4063a2ad70407b651e97bb598d33b21a4aabdd158",
            SessionLifecyclePayloadV1Codec.ComputeIntegrityHash(fact).ToString());
        Assert.NotEqual(
            SessionLifecyclePayloadV1Codec.ComputeIntegrityHash(command),
            SessionLifecyclePayloadV1Codec.ComputeIntegrityHash(fact));
    }

    [Fact]
    public void CommandAndFact_RoundTripOwnBodiesAndHonorValueEquality()
    {
        var (session, vector) = Authority();
        var source = new byte[] { 3, 4, 5 };
        var command = new SessionLifecycleCommandV1(session, vector, source);
        var fact = new SessionLifecycleFactV1(session, vector, source);
        source[0] = 99;

        Assert.Equal(3, command.Body[0]);
        Assert.Equal(3, fact.Body[0]);
        Assert.True(SessionLifecyclePayloadV1Codec.TryDecodeCommand(
            SessionLifecyclePayloadV1Codec.Encode(command), out var decodedCommand));
        Assert.True(SessionLifecyclePayloadV1Codec.TryDecodeFact(
            SessionLifecyclePayloadV1Codec.Encode(fact), out var decodedFact));
        Assert.Equal(command, decodedCommand);
        Assert.Equal(fact, decodedFact);
        Assert.True(command == decodedCommand);
        Assert.True(fact == decodedFact);
    }

    [Fact]
    public void BodyBounds_IncludeEmptyAndMaximumButRejectMaximumPlusOne()
    {
        var (session, vector) = Authority();
        Assert.Empty(new SessionLifecycleCommandV1(session, vector, []).Body);
        var maximum = new byte[SessionLifecyclePayloadV1Codec.MaximumBodyBytes];
        var maximumVector = ExpectedAuthorityVectorV1.Create(session,
        [
            new AuthorityAxisValueV1.Graph(GraphGenerationId.Create()),
            new AuthorityAxisValueV1.Activity(ActivityGenerationId.Create()),
            new AuthorityAxisValueV1.Turn(TurnGenerationId.Create()),
            new AuthorityAxisValueV1.Provider(ProviderGenerationId.Create()),
            new AuthorityAxisValueV1.Output(OutputGenerationId.Create()),
            new AuthorityAxisValueV1.Sink(SinkGenerationId.Create()),
            new AuthorityAxisValueV1.Tool(ToolGenerationId.Create()),
            new AuthorityAxisValueV1.Route(RouteGenerationId.Create()),
            new AuthorityAxisValueV1.Privacy(PrivacyGenerationId.Create()),
            new AuthorityAxisValueV1.Transport(TransportGenerationId.Create()),
        ]);
        var fact = new SessionLifecycleFactV1(session, maximumVector, maximum);
        Assert.Equal(SessionLifecyclePayloadV1Codec.MaximumBodyBytes, fact.Body.Count);
        var maximumEncoding = SessionLifecyclePayloadV1Codec.Encode(fact);
        Assert.Equal(SessionLifecyclePayloadV1Codec.MaximumEncodedBytes, maximumEncoding.Length);
        Assert.True(SessionLifecyclePayloadV1Codec.TryDecodeFact(maximumEncoding, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionLifecycleCommandV1(
            session, vector, new byte[SessionLifecyclePayloadV1Codec.MaximumBodyBytes + 1]));
    }

    [Fact]
    public void Constructors_RejectDefaultNullAndSessionMismatch()
    {
        var (session, vector) = Authority();
        var other = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        Assert.Throws<ArgumentException>(() => new SessionLifecycleCommandV1(default, vector, []));
        Assert.Throws<ArgumentNullException>(() => new SessionLifecycleCommandV1(session, null!, []));
        Assert.Throws<ArgumentException>(() => new SessionLifecycleFactV1(
            other, vector, []));
    }

    [Fact]
    public void Decoders_RejectMalformedIndefiniteUnknownTrailingAndOversizedInputs()
    {
        Assert.False(SessionLifecyclePayloadV1Codec.TryDecodeCommand(new byte[] { 0xa0 }, out _));
        Assert.False(SessionLifecyclePayloadV1Codec.TryDecodeCommand(new byte[] { 0xbf, 0xff }, out _));
        Assert.False(SessionLifecyclePayloadV1Codec.TryDecodeFact(
            new byte[SessionLifecyclePayloadV1Codec.MaximumEncodedBytes + 1], out _));
        var valid = SessionLifecyclePayloadV1Codec.Encode(CreateCommand());
        Assert.False(SessionLifecyclePayloadV1Codec.TryDecodeCommand(valid.Concat(new byte[] { 0 }).ToArray(), out _));

        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(4);
        writer.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(writer, CreateCommand().Session);
        writer.WriteUInt64(2); AuthorityVectorCodecsV1.WriteVector(writer, CreateCommand().ExpectedAuthority);
        writer.WriteUInt64(3); writer.WriteByteString([]);
        writer.WriteUInt64(4); writer.WriteUInt64(0);
        writer.WriteEndMap();
        Assert.False(SessionLifecyclePayloadV1Codec.TryDecodeCommand(writer.Encode(), out _));

        var oversizedBody = new CborWriter(CborConformanceMode.Ctap2Canonical);
        oversizedBody.WriteStartMap(3);
        oversizedBody.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(oversizedBody, CreateCommand().Session);
        oversizedBody.WriteUInt64(2); AuthorityVectorCodecsV1.WriteVector(oversizedBody, CreateCommand().ExpectedAuthority);
        oversizedBody.WriteUInt64(3); oversizedBody.WriteByteString(new byte[SessionLifecyclePayloadV1Codec.MaximumBodyBytes + 1]);
        oversizedBody.WriteEndMap();
        Assert.True(oversizedBody.Encode().Length < SessionLifecyclePayloadV1Codec.MaximumEncodedBytes);
        Assert.False(SessionLifecyclePayloadV1Codec.TryDecodeCommand(oversizedBody.Encode(), out _));
    }

    [Fact]
    public void Registrations_RequireExactS1SchemaCanonicalPayloadAndEmbeddedSession()
    {
        var command = CreateValidCommand();
        var commandRegistration = new SessionLifecycleCommandPayloadRegistrationV1();
        var factRegistration = new SessionLifecycleFactPayloadRegistrationV1();
        Assert.Equal(OwnerSliceId.S1, commandRegistration.Owner);
        Assert.Equal(OwnerSliceId.S1, factRegistration.Owner);
        Assert.Equal(SessionLifecyclePayloadV1Codec.MaximumEncodedBytes, commandRegistration.MaximumPayloadBytes);
        Assert.Equal(SessionLifecyclePayloadV1Codec.MaximumEncodedBytes, factRegistration.MaximumPayloadBytes);
        Assert.True(commandRegistration.Validate(SessionLifecyclePayloadV1Codec.Encode(command), command.Session));
        Assert.False(commandRegistration.Validate(
            SessionLifecyclePayloadV1Codec.Encode(command),
            new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create())));
        Assert.True(factRegistration.Validate(SessionLifecyclePayloadV1Codec.Encode(CreateValidFact()), command.Session));
        Assert.False(factRegistration.Validate(new byte[] { 0xff }, command.Session));
    }

    [Fact]
    public async Task Journal_AdmitsExactLifecyclePayloadAndRejectsOwnerHashAndSessionContradictions()
    {
        var command = CreateValidCommand();
        var registration = new SessionLifecycleCommandPayloadRegistrationV1();
        var registry = new AuthorityPayloadAdmissionRegistryV1([registration]);
        var payload = SessionLifecyclePayloadV1Codec.Encode(command);
        var hash = AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload);
        var correlation = new CorrelationEnvelopeV1(TenantId.Create(), operationId: OperationId.Create());
        var journal = new InMemoryAuthorityJournalV1(registry, () => new UtcInstant(200),
            new AuthorityJournalCapacityV1(4, 16, 1_048_576));

        ProposedAuthorityFactV1 Proposal(OwnerSliceId owner, Hash256 payloadHash) => new(
            JournalFactId.Create(), null, owner, registration.Schema, payload, payloadHash, correlation, new UtcInstant(100));

        var admitted = await journal.AppendAsync(new AppendAuthorityBatchV1(
            command.Session, 0, [], [Proposal(OwnerSliceId.S1, hash)], 131_072));
        Assert.IsType<AppendAuthorityResultV1.Committed>(admitted);

        var wrongOwner = await journal.AppendAsync(new AppendAuthorityBatchV1(
            command.Session, 1, [], [Proposal(OwnerSliceId.S2, hash)], 131_072));
        Assert.IsType<AppendAuthorityResultV1.InvalidPayload>(wrongOwner);

        var wrongHash = await journal.AppendAsync(new AppendAuthorityBatchV1(
            command.Session, 1, [], [Proposal(OwnerSliceId.S1, Hash256.Compute("wrong"u8))], 131_072));
        Assert.IsType<AppendAuthorityResultV1.InvalidPayload>(wrongHash);

        var other = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var wrongSession = await journal.AppendAsync(new AppendAuthorityBatchV1(
            other, 0, [], [Proposal(OwnerSliceId.S1, hash)], 131_072));
        Assert.IsType<AppendAuthorityResultV1.InvalidPayload>(wrongSession);
    }

    private static SessionLifecycleCommandV1 CreateCommand()
    {
        var (session, vector) = Authority();
        return new SessionLifecycleCommandV1(session, vector, [1, 2]);
    }

    private static SessionLifecycleCommandV1 CreateValidCommand()
    {
        var (session, vector) = Authority();
        var inner = new SessionLifecycleCommandBodyV1.ReserveStarting(
            OperationId.FromValue(StableId128.FromBytes(Convert.FromHexString("000102030405060708090a0b0c0d0e0f"))),
            Hash256.Compute("start"u8));
        return new SessionLifecycleCommandV1(session, vector, SessionLifecycleBodyCodecsV1.Encode(inner));
    }

    private static SessionLifecycleFactV1 CreateValidFact()
    {
        var command = CreateValidCommand();
        var inner = new SessionLifecycleFactBodyV1(
            OperationId.FromValue(StableId128.FromBytes(Convert.FromHexString("000102030405060708090a0b0c0d0e0f"))),
            new JournalPositionV1(command.Session, 1), null, null, SessionLifecycleOutcomeV1.Applied,
            new SessionLifecycleSnapshotBodyV1(
                SessionLifecycleStateWireV1.Starting, SessionAdmissionWireV1.Closed,
                SessionAvailabilityWireV1.Unavailable, SessionReadinessWireV1.Unpublished,
                SessionTerminalIntentWireV1.None, SessionTerminalCauseWireV1.None,
                SessionTerminalIntentWireV1.None, SessionTerminalCauseWireV1.None,
                SessionTerminalSeverityWireV1.None, SessionConvergencePhaseWireV1.None,
                SessionMutationFenceWireV1.Open, false), null);
        return new SessionLifecycleFactV1(command.Session, command.ExpectedAuthority,
            SessionLifecycleBodyCodecsV1.Encode(inner));
    }

    private static (SessionAuthorityStampV1 Session, ExpectedAuthorityVectorV1 Vector) Authority()
    {
        var session = new SessionAuthorityStampV1(
            RuntimeGenerationId.FromValue(StableId128.FromBytes(Convert.FromHexString("101112131415161718191a1b1c1d1e1f"))),
            LiveSessionId.FromValue(StableId128.FromBytes(Convert.FromHexString("202122232425262728292a2b2c2d2e2f"))));
        return (session, ExpectedAuthorityVectorV1.Create(session, []));
    }
}
