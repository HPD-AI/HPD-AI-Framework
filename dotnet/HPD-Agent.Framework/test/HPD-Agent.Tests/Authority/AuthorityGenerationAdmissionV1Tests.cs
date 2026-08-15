using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class AuthorityGenerationAdmissionV1Tests
{
    [Fact]
    public async Task Journal_AdmitsEachOwnerInitializationAndTransitionExactlyOnce()
    {
        var fixture = new Fixture();
        var current = new Dictionary<AuthorityAxisId, StableId128>();
        foreach (var axis in Enum.GetValues<AuthorityAxisId>().Where(static axis => axis != AuthorityAxisId.Runtime))
        {
            var value = Id((byte)axis);
            current.Add(axis, value);
            Assert.IsType<AppendAuthorityResultV1.Committed>(await fixture.AppendInitializationAsync(axis, value));
        }
        foreach (var axis in current.Keys.OrderBy(static axis => axis))
        {
            var next = Id((byte)((byte)axis + 32));
            Assert.IsType<AppendAuthorityResultV1.Committed>(
                await fixture.AppendTransitionAsync(axis, current[axis], next));
            current[axis] = next;
        }
        Assert.IsType<AppendAuthorityResultV1.Committed>(await fixture.AppendTransitionAsync(
            AuthorityAxisId.Runtime, Stable(fixture.Session.RuntimeGenerationId), Id(96)));
    }

    [Fact]
    public async Task Journal_RejectsWrongSessionDuplicateInitializationTransitionBeforeInitializationAndStaleTransition()
    {
        var wrongSession = new Fixture();
        var graph = Id(1);
        var wrong = await wrongSession.AppendInitializationAsync(
            AuthorityAxisId.Graph, graph,
            payloadSession: new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()));
        Assert.Equal("invalid-canonical-payload", Assert.IsType<AppendAuthorityResultV1.InvalidPayload>(wrong).SafeCode.ToString());

        var transitionFirst = new Fixture();
        var beforeInitialization = await transitionFirst.AppendTransitionAsync(AuthorityAxisId.Graph, graph, Id(33));
        Assert.Equal("generation-state-conflict", Assert.IsType<AppendAuthorityResultV1.InvalidPayload>(beforeInitialization).SafeCode.ToString());

        var duplicate = new Fixture();
        Assert.IsType<AppendAuthorityResultV1.Committed>(await duplicate.AppendInitializationAsync(AuthorityAxisId.Graph, graph));
        var duplicateResult = await duplicate.AppendInitializationAsync(AuthorityAxisId.Graph, Id(2));
        Assert.Equal("generation-state-conflict", Assert.IsType<AppendAuthorityResultV1.InvalidPayload>(duplicateResult).SafeCode.ToString());

        var stale = new Fixture();
        Assert.IsType<AppendAuthorityResultV1.Committed>(await stale.AppendInitializationAsync(AuthorityAxisId.Graph, graph));
        var staleResult = await stale.AppendTransitionAsync(AuthorityAxisId.Graph, Id(2), Id(34));
        Assert.Equal("generation-state-conflict", Assert.IsType<AppendAuthorityResultV1.InvalidPayload>(staleResult).SafeCode.ToString());
    }

    [Fact]
    public async Task Journal_AppliesGenerationBatchesAtomicallyAndOneSameHeadContenderWins()
    {
        var fixture = new Fixture();
        var initial = Id(1);
        var next = Id(33);
        var init = fixture.Initialization(AuthorityAxisId.Graph, initial);
        var transition = fixture.Transition(AuthorityAxisId.Graph, initial, next);
        Assert.IsType<AppendAuthorityResultV1.Committed>(await fixture.AppendBatchAsync(init, transition));

        var left = fixture.Transition(AuthorityAxisId.Graph, next, Id(65));
        var right = fixture.Transition(AuthorityAxisId.Graph, next, Id(81));
        var expectedHead = fixture.Head;
        var results = await Task.WhenAll(
            fixture.Journal.AppendAsync(new AppendAuthorityBatchV1(fixture.Session, expectedHead, [], [left], 16_384)).AsTask(),
            fixture.Journal.AppendAsync(new AppendAuthorityBatchV1(fixture.Session, expectedHead, [], [right], 16_384)).AsTask());
        Assert.Single(results.OfType<AppendAuthorityResultV1.Committed>());
        Assert.Single(results.OfType<AppendAuthorityResultV1.SessionConflict>());

        var atomic = new Fixture();
        var invalidBatch = await atomic.AppendBatchAsync(
            atomic.Initialization(AuthorityAxisId.Graph, initial),
            atomic.Transition(AuthorityAxisId.Graph, Id(2), next));
        Assert.IsType<AppendAuthorityResultV1.InvalidPayload>(invalidBatch);
        var read = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await atomic.Journal.ReadAsync(
            new ReadAuthorityRangeV1(atomic.Session, 0, long.MaxValue, 16, 65_536)));
        Assert.Equal(0, read.SnapshotThrough);
        Assert.Empty(read.Facts);
    }

    [Fact]
    public async Task RuntimeReplacementClosesTheStreamAndOrdinaryAppendsDoNotReplayHistory()
    {
        var fixture = new Fixture();
        Assert.IsType<AppendAuthorityResultV1.Committed>(await fixture.AppendTransitionAsync(
            AuthorityAxisId.Runtime, Stable(fixture.Session.RuntimeGenerationId), Id(96)));
        var closed = await fixture.AppendOrdinaryAsync();
        Assert.Equal("generation-state-conflict", Assert.IsType<AppendAuthorityResultV1.InvalidPayload>(closed).SafeCode.ToString());

        var ordinary = new Fixture();
        for (var index = 0; index < 64; index++)
            Assert.IsType<AppendAuthorityResultV1.Committed>(await ordinary.AppendOrdinaryAsync());
        Assert.Equal(64, ordinary.Head);
    }

    private sealed class Fixture
    {
        private long _head;
        private readonly TenantId _tenant = TenantId.Create();

        internal Fixture()
        {
            var registrations = new List<AuthorityPayloadRegistrationV1> { new SessionAuthorityStampPayloadRegistrationV1() };
            registrations.AddRange(Enum.GetValues<AuthorityAxisId>()
                .Where(static axis => axis != AuthorityAxisId.Runtime)
                .Select(static axis => new AuthorityGenerationInitializationPayloadRegistrationV1(axis)));
            registrations.AddRange(Enum.GetValues<AuthorityAxisId>()
                .Select(static axis => new AuthorityGenerationTransitionPayloadRegistrationV1(axis)));
            Journal = new InMemoryAuthorityJournalV1(new AuthorityPayloadAdmissionRegistryV1(registrations),
                () => new UtcInstant(123), new AuthorityJournalCapacityV1(8, 128, 2 * 1024 * 1024));
            Session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        }

        internal InMemoryAuthorityJournalV1 Journal { get; }
        internal SessionAuthorityStampV1 Session { get; }
        internal long Head => _head;

        internal async ValueTask<AppendAuthorityResultV1> AppendInitializationAsync(
            AuthorityAxisId axis, StableId128 initial, SessionAuthorityStampV1? payloadSession = null)
        {
            var registration = new AuthorityGenerationInitializationPayloadRegistrationV1(axis);
            return await AppendAsync(registration, EncodeInitialization(
                payloadSession ?? Session, initial, registration.Owner));
        }

        internal async ValueTask<AppendAuthorityResultV1> AppendTransitionAsync(
            AuthorityAxisId axis, StableId128 expected, StableId128 proposed)
        {
            var registration = new AuthorityGenerationTransitionPayloadRegistrationV1(axis);
            return await AppendAsync(registration, EncodeTransition(Session, expected, proposed, registration.Owner));
        }

        internal ProposedAuthorityFactV1 Initialization(AuthorityAxisId axis, StableId128 initial)
        {
            var registration = new AuthorityGenerationInitializationPayloadRegistrationV1(axis);
            return Proposal(registration, EncodeInitialization(Session, initial, registration.Owner));
        }

        internal ProposedAuthorityFactV1 Transition(AuthorityAxisId axis, StableId128 expected, StableId128 proposed)
        {
            var registration = new AuthorityGenerationTransitionPayloadRegistrationV1(axis);
            return Proposal(registration, EncodeTransition(Session, expected, proposed, registration.Owner));
        }

        internal ValueTask<AppendAuthorityResultV1> AppendOrdinaryAsync()
        {
            var registration = new SessionAuthorityStampPayloadRegistrationV1();
            return AppendAsync(registration, SessionAuthorityStampV1Codec.Encode(Session));
        }

        internal async ValueTask<AppendAuthorityResultV1> AppendBatchAsync(params ProposedAuthorityFactV1[] proposals)
        {
            var result = await Journal.AppendAsync(new AppendAuthorityBatchV1(Session, _head, [], proposals, 16_384));
            if (result is AppendAuthorityResultV1.Committed committed) _head = committed.CurrentHead;
            return result;
        }

        private async ValueTask<AppendAuthorityResultV1> AppendAsync(
            AuthorityPayloadRegistrationV1 registration, byte[] payload)
        {
            return await AppendBatchAsync(Proposal(registration, payload));
        }

        private ProposedAuthorityFactV1 Proposal(AuthorityPayloadRegistrationV1 registration, byte[] payload) =>
            new(
                JournalFactId.Create(), null, registration.Owner, registration.Schema, payload,
                AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload),
                new CorrelationEnvelopeV1(_tenant, operationId: OperationId.Create()), new UtcInstant(100));
    }

    private static StableId128 Id(byte seed)
    {
        Span<byte> bytes = stackalloc byte[16];
        for (var index = 0; index < bytes.Length; index++) bytes[index] = (byte)(seed + index);
        return StableId128.FromBytes(bytes);
    }

    private static StableId128 Stable(RuntimeGenerationId value)
    {
        Span<byte> bytes = stackalloc byte[16];
        Assert.True(value.TryWriteBytes(bytes));
        return StableId128.FromBytes(bytes);
    }

    private static byte[] EncodeInitialization(
        SessionAuthorityStampV1 session, StableId128 initial, OwnerSliceId owner)
    {
        Span<byte> bytes = stackalloc byte[16];
        Assert.True(initial.TryWriteBytes(bytes));
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(3);
        writer.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(writer, session);
        writer.WriteUInt64(2); writer.WriteByteString(bytes);
        writer.WriteUInt64(3); writer.WriteUInt64((ushort)owner);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static byte[] EncodeTransition(
        SessionAuthorityStampV1 session, StableId128 expected, StableId128 proposed, OwnerSliceId owner)
    {
        Span<byte> expectedBytes = stackalloc byte[16];
        Span<byte> proposedBytes = stackalloc byte[16];
        Assert.True(expected.TryWriteBytes(expectedBytes));
        Assert.True(proposed.TryWriteBytes(proposedBytes));
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(4);
        writer.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(writer, session);
        writer.WriteUInt64(2); writer.WriteByteString(expectedBytes);
        writer.WriteUInt64(3); writer.WriteByteString(proposedBytes);
        writer.WriteUInt64(4); writer.WriteUInt64((ushort)owner);
        writer.WriteEndMap();
        return writer.Encode();
    }
}
