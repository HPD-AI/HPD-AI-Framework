using System.Formats.Cbor;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphReplacementSnapshotReaderV1Tests
{
    [Fact]
    public async Task Empty_pinned_snapshot_is_verified_without_inventing_topology()
    {
        var session = Session();
        var journal = new InMemoryAuthorityJournalV1(new AuthorityPayloadAdmissionRegistryV1([
            GraphReplacementPayloadRegistrationsV1.Command,
            GraphReplacementPayloadRegistrationsV1.Installed,
            GraphReplacementPayloadRegistrationsV1.Fact,
        ]), () => new UtcInstant(1), new AuthorityJournalCapacityV1(2, 16, 1_000_000));

        var verified = Assert.IsType<GraphReplacementSnapshotReadResultV1.Verified>(
            await GraphReplacementSnapshotReaderV1.ReadAsync(journal, session, maximumFacts: 1));
        var current = Assert.IsType<GraphReplacementJournalFoldResultV1.Current>(verified.Fold);
        Assert.Equal(0, verified.SnapshotThrough);
        Assert.Null(current.State);
    }

    [Fact]
    public async Task Store_exception_is_unknown_and_cancellation_is_observed_after_an_ignoring_port()
    {
        var session = Session();
        var unknown = Assert.IsType<GraphReplacementSnapshotReadResultV1.OutcomeUnknown>(
            await GraphReplacementSnapshotReaderV1.ReadAsync(new ThrowingJournal(), session));
        Assert.Equal("graph-store-exception", unknown.SafeCode.ToString());
        Assert.Equal(0, unknown.LastVerifiedPosition);

        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await GraphReplacementSnapshotReaderV1.ReadAsync(
                new CancelThenReturnJournal(session, cancellation.Cancel), session,
            cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task Oversize_journal_item_is_unknown_with_no_fabricated_state()
    {
        var result = Assert.IsType<GraphReplacementSnapshotReadResultV1.OutcomeUnknown>(
            await GraphReplacementSnapshotReaderV1.ReadAsync(new ItemTooLargeJournal(), Session()));
        Assert.Equal("graph-item-too-large", result.SafeCode.ToString());
        Assert.Equal(0, result.LastVerifiedPosition);
    }

    [Fact]
    public async Task Runtime_replacement_is_verified_only_when_it_is_the_pinned_terminal_fact()
    {
        var session = Session();
        var replacement = RuntimeGenerationId.Create();
        var transition = RuntimeTransition(session, replacement, 1);
        var exact = await GraphReplacementSnapshotReaderV1.ReadAsync(new ScriptedJournal(
            new ReadAuthorityRangeResultV1.Batch(session, 1, 0, 1, [transition], false)), session);
        var verified = Assert.IsType<GraphReplacementSnapshotReadResultV1.Verified>(exact);
        Assert.Equal(replacement,
            Assert.IsType<GraphReplacementJournalFoldResultV1.RuntimeReplaced>(verified.Fold).Replacement);

        var followed = await GraphReplacementSnapshotReaderV1.ReadAsync(new ScriptedJournal(
            new ReadAuthorityRangeResultV1.Batch(session, 2, 0, 2, [transition], true),
            new ReadAuthorityRangeResultV1.Batch(session, 2, 1, 2, [Unrelated(session, 2)], false)), session,
            maximumFacts: 1);
        var unknown = Assert.IsType<GraphReplacementSnapshotReadResultV1.OutcomeUnknown>(followed);
        Assert.Equal("facts-after-runtime-replacement", unknown.SafeCode.ToString());
        Assert.Equal(1, unknown.LastVerifiedPosition);
    }

    [Fact]
    public void Result_union_rejects_invalid_coverage_and_diagnostics()
    {
        var session = Session();
        var authority = new CurrentAuthorityVectorSnapshotV1(session, [], 1);
        var fold = new GraphReplacementJournalFoldResultV1.Current(1, authority, null, []);

        Assert.Throws<ArgumentException>(() => new GraphReplacementSnapshotReadResultV1.Verified(fold, 2));
        Assert.Throws<ArgumentException>(() => new GraphReplacementSnapshotReadResultV1.Verified(
            new GraphReplacementJournalFoldResultV1.InvalidHistory(new BoundedAscii("invalid"), 0), 0));
        Assert.Throws<ArgumentException>(() => new GraphReplacementSnapshotReadResultV1.OutcomeUnknown(default, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphReplacementSnapshotReadResultV1.OutcomeUnknown(
            new BoundedAscii("invalid"), -1));
    }

    private static SessionAuthorityStampV1 Session() =>
        new(RuntimeGenerationId.Create(), LiveSessionId.Create());

    private static AuthorityFactEnvelopeV1 RuntimeTransition(
        SessionAuthorityStampV1 session, RuntimeGenerationId proposed, long sequence)
    {
        Span<byte> before = stackalloc byte[16]; Span<byte> after = stackalloc byte[16];
        Assert.True(session.RuntimeGenerationId.TryWriteBytes(before)); Assert.True(proposed.TryWriteBytes(after));
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(4); writer.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(writer, session);
        writer.WriteUInt64(2); writer.WriteByteString(before); writer.WriteUInt64(3); writer.WriteByteString(after);
        writer.WriteUInt64(4); writer.WriteUInt64((ushort)OwnerSliceId.S1); writer.WriteEndMap();
        var payload = writer.Encode();
        var schema = AuthorityGenerationTransitionCodecV1.SchemaFor(AuthorityAxisId.Runtime);
        var token = AuthorityGenerationTransitionCodecV1.SchemaTokenFor(AuthorityAxisId.Runtime);
        return Envelope(session, sequence, OwnerSliceId.S1, schema, payload,
            AuthorityPayloadHashV1.Compute(token, schema, payload));
    }

    private static AuthorityFactEnvelopeV1 Unrelated(SessionAuthorityStampV1 session, long sequence)
    {
        var payload = new byte[] { 0x80 };
        return Envelope(session, sequence, OwnerSliceId.S4, new SchemaReferenceV1(SchemaId.Create(), 1, 0),
            payload, Hash256.Compute(payload));
    }

    private static AuthorityFactEnvelopeV1 Envelope(SessionAuthorityStampV1 session, long sequence,
        OwnerSliceId owner, SchemaReferenceV1 schema, byte[] payload, Hash256 hash) => new(
        JournalFactId.Create(), new JournalPositionV1(session, sequence), null, owner, schema, payload, hash,
        new CorrelationEnvelopeV1(TenantId.Create()), new UtcInstant(sequence), new UtcInstant(sequence),
        new IntegrityEnvelopeV1(1, 1, Hash256.Compute([1]), []));

    private sealed class ThrowingJournal : IAuthorityJournalV1
    {
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,
            CancellationToken cancellationToken = default) => throw new IOException("fixture");
    }

    private sealed class CancelThenReturnJournal(SessionAuthorityStampV1 session, Action cancel) : IAuthorityJournalV1
    {
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,
            CancellationToken cancellationToken = default)
        {
            cancel();
            return ValueTask.FromResult<ReadAuthorityRangeResultV1>(
                new ReadAuthorityRangeResultV1.Batch(session, 0, 0, 0, [], false));
        }
    }

    private sealed class ItemTooLargeJournal : IAuthorityJournalV1
    {
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<ReadAuthorityRangeResultV1>(
                new ReadAuthorityRangeResultV1.ItemTooLarge(new JournalPositionV1(request.Session, 1),
                    (ulong)ProposedAuthorityFactV1.MaximumPayloadBytes + 1, request.MaximumEncodedBytes));
    }

    private sealed class ScriptedJournal(params ReadAuthorityRangeResultV1[] results) : IAuthorityJournalV1
    {
        private int _index;
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                results[Math.Min(_index++, results.Length - 1)]);
    }
}
