using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class AuthorityVectorSnapshotReaderV1Tests
{
    [Fact]
    public async Task Reader_PinsFirstSnapshotAndFoldsPagesWithoutHistoryBuffer()
    {
        var session = Session();
        var first = Fact(1, session, AuthorityGenerationInitializationCodecV1.SchemaFor(AuthorityAxisId.Graph), OwnerSliceId.S2,
            EncodeInitialization(session, Initial, OwnerSliceId.S2));
        var second = Fact(2, session, new SchemaReferenceV1(SchemaId.Create(), 1, 0), OwnerSliceId.S4, [0x80]);
        var journal = new ScriptedJournal(
            new ReadAuthorityRangeResultV1.Batch(session, 2, 0, 2, [first], true),
            new ReadAuthorityRangeResultV1.Batch(session, 3, 1, 2, [second], false));

        var result = await AuthorityVectorSnapshotReaderV1.ReadAsync(journal, session, 1, 4096);

        var verified = Assert.IsType<AuthorityVectorSnapshotReadResultV1.Verified>(result);
        Assert.Equal(2, verified.SnapshotThrough);
        var current = Assert.IsType<AuthorityVectorReplayResultV1.Current>(verified.Replay).Snapshot;
        Assert.Equal(2, current.ThroughPosition);
        Assert.Equal(AuthorityAxisId.Graph, Assert.Single(current.Axes).AxisId);
        Assert.Equal([long.MaxValue, 2], journal.Requests.Select(static request => request.ThroughInclusive));
        Assert.Equal([0, 1], journal.Requests.Select(static request => request.AfterExclusive));
    }

    [Fact]
    public async Task Reader_MapsUnavailableOversizeAndThrownStoreFailuresToOutcomeUnknown()
    {
        var session = Session();
        var position = new JournalPositionV1(session, 1);
        var unavailable = await AuthorityVectorSnapshotReaderV1.ReadAsync(
            new ScriptedJournal(new ReadAuthorityRangeResultV1.StoreUnavailable(new BoundedAscii("offline"))), session);
        var oversized = await AuthorityVectorSnapshotReaderV1.ReadAsync(
            new ScriptedJournal(new ReadAuthorityRangeResultV1.ItemTooLarge(position, 9, 8)), session, maximumEncodedBytes: 8);
        var thrown = await AuthorityVectorSnapshotReaderV1.ReadAsync(new ThrowingJournal(), session);

        Assert.Equal("offline", Assert.IsType<AuthorityVectorSnapshotReadResultV1.OutcomeUnknown>(unavailable).SafeCode.ToString());
        Assert.Equal("item-too-large", Assert.IsType<AuthorityVectorSnapshotReadResultV1.OutcomeUnknown>(oversized).SafeCode.ToString());
        Assert.Equal("store-exception", Assert.IsType<AuthorityVectorSnapshotReadResultV1.OutcomeUnknown>(thrown).SafeCode.ToString());
    }

    [Fact]
    public async Task Reader_AcceptsTheFirstPinnedEmptySnapshotWithoutAnotherRead()
    {
        var session = Session();
        var journal = new ScriptedJournal(new ReadAuthorityRangeResultV1.Batch(session, 0, 0, 0, [], false));

        var result = await AuthorityVectorSnapshotReaderV1.ReadAsync(journal, session);

        var verified = Assert.IsType<AuthorityVectorSnapshotReadResultV1.Verified>(result);
        var current = Assert.IsType<AuthorityVectorReplayResultV1.Current>(verified.Replay).Snapshot;
        Assert.Equal(0, verified.SnapshotThrough);
        Assert.Equal(0, current.ThroughPosition);
        Assert.Empty(current.Axes);
        Assert.Single(journal.Requests);
        Assert.Equal(long.MaxValue, journal.Requests[0].ThroughInclusive);
    }

    [Fact]
    public async Task Reader_RejectsSnapshotDriftWithoutUpgradingPartialEvidence()
    {
        var session = Session();
        var first = Fact(1, session, new SchemaReferenceV1(SchemaId.Create(), 1, 0), OwnerSliceId.S4, [0x80]);
        var second = Fact(2, session, new SchemaReferenceV1(SchemaId.Create(), 1, 0), OwnerSliceId.S4, [0x80]);
        var third = Fact(3, session, new SchemaReferenceV1(SchemaId.Create(), 1, 0), OwnerSliceId.S4, [0x80]);
        var journal = new ScriptedJournal(
            new ReadAuthorityRangeResultV1.Batch(session, 2, 0, 2, [first], true),
            new ReadAuthorityRangeResultV1.Batch(session, 3, 1, 3, [second, third], false));

        var result = await AuthorityVectorSnapshotReaderV1.ReadAsync(journal, session, 1, 4096);

        var unknown = Assert.IsType<AuthorityVectorSnapshotReadResultV1.OutcomeUnknown>(result);
        Assert.Equal("snapshot-drift", unknown.SafeCode.ToString());
        Assert.Equal(1, unknown.LastVerifiedPosition);
    }

    [Fact]
    public async Task Reader_PreservesInvalidPrefixWithoutCallingItCurrentTruth()
    {
        var session = Session();
        var malformed = Fact(1, session,
            AuthorityGenerationInitializationCodecV1.SchemaFor(AuthorityAxisId.Graph), OwnerSliceId.S2, [0xff]);
        var journal = new ScriptedJournal(new ReadAuthorityRangeResultV1.Batch(session, 1, 0, 1, [malformed], false));

        var result = await AuthorityVectorSnapshotReaderV1.ReadAsync(journal, session);

        var verified = Assert.IsType<AuthorityVectorSnapshotReadResultV1.Verified>(result);
        Assert.Equal(1, verified.SnapshotThrough);
        Assert.Equal(0, Assert.IsType<AuthorityVectorReplayResultV1.InvalidHistory>(verified.Replay).LastPosition);
    }

    [Fact]
    public async Task Reader_PropagatesCallerCancellation()
    {
        var session = Session();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await AuthorityVectorSnapshotReaderV1.ReadAsync(new CancelingJournal(), session, cancellationToken: cancellation.Token));
    }

    private static readonly StableId128 Initial = StableId128.FromBytes(Convert.FromHexString("0102030405060708090a0b0c0d0e0f10"));

    private static SessionAuthorityStampV1 Session() => new(
        RuntimeGenerationId.FromValue(StableId128.FromBytes(Convert.FromHexString("000102030405060708090a0b0c0d0e0f"))),
        LiveSessionId.FromValue(StableId128.FromBytes(Convert.FromHexString("202122232425262728292a2b2c2d2e2f"))));

    private static AuthorityFactEnvelopeV1 Fact(
        long sequence,
        SessionAuthorityStampV1 session,
        SchemaReferenceV1 schema,
        OwnerSliceId owner,
        byte[] payload) => new(
            JournalFactId.Create(), new JournalPositionV1(session, sequence), null, owner, schema, payload,
            Hash256.Compute(payload), new CorrelationEnvelopeV1(TenantId.Create()), new UtcInstant(sequence),
            new UtcInstant(sequence), new IntegrityEnvelopeV1(1, 1, Hash256.Compute([1]), []));

    private static byte[] EncodeInitialization(SessionAuthorityStampV1 session, StableId128 initial, OwnerSliceId owner)
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

    private sealed class ScriptedJournal(params ReadAuthorityRangeResultV1[] results) : IAuthorityJournalV1
    {
        private readonly Queue<ReadAuthorityRangeResultV1> _results = new(results);
        internal List<ReadAuthorityRangeV1> Requests { get; } = [];
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(_results.Dequeue());
        }
    }

    private sealed class ThrowingJournal : IAuthorityJournalV1
    {
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request, CancellationToken cancellationToken = default) => throw new IOException("fixture");
    }

    private sealed class CancelingJournal : IAuthorityJournalV1
    {
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request, CancellationToken cancellationToken = default) => ValueTask.FromCanceled<ReadAuthorityRangeResultV1>(cancellationToken);
    }
}
