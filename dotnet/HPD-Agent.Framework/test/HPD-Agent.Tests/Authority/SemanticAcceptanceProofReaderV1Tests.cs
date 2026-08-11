using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class SemanticAcceptanceProofReaderV1Tests
{
    [Fact]
    public async Task Reader_ProvesOnlyAClaimWhoseRelevantAxesRemainCurrent()
    {
        var session = Session();
        var expected = ExpectedAuthorityVectorV1.Create(session,
            [new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(First))]);
        var claim = new SubmissionDispositionChosenV1(OperationId.Create(), new JournalPositionV1(session, 1),
            expected, SubmissionDispositionV1.SubmissionClaimed);
        var facts = new[]
        {
            Fact(1, session, OwnerSliceId.S2, AuthorityGenerationInitializationCodecV1.SchemaFor(AuthorityAxisId.Graph), EncodeInitialization(session, First, OwnerSliceId.S2)),
            Fact(2, session, OwnerSliceId.S1, DispositionSchema(), SubmissionDispositionChosenV1Codec.Encode(claim)),
        };

        var result = await SemanticAcceptanceProofReaderV1.ReadAsync(
            new OneBatchJournal(new ReadAuthorityRangeResultV1.Batch(session, 2, 0, 2, facts, false)),
            new JournalPositionV1(session, 2));

        var proven = Assert.IsType<SemanticAcceptanceProofResultV1.Proven>(result);
        Assert.Equal(claim, proven.Claim);
        Assert.Equal(new JournalPositionV1(session, 2), proven.DispositionPosition);
        Assert.Equal(2, proven.SnapshotThrough);
        Assert.Equal(GraphGenerationId.FromValue(First),
            Assert.IsType<AuthorityAxisValueV1.Graph>(Assert.Single(proven.Current.Axes).Value).Value);
    }

    [Fact]
    public async Task Reader_RejectsStaleRelevantAxisAndNonclaimDisposition()
    {
        var session = Session();
        var staleExpected = ExpectedAuthorityVectorV1.Create(session,
            [new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(Second))]);
        var staleClaim = new SubmissionDispositionChosenV1(OperationId.Create(), new JournalPositionV1(session, 1),
            staleExpected, SubmissionDispositionV1.SubmissionClaimed);
        var withdrawn = new SubmissionDispositionChosenV1(OperationId.Create(), new JournalPositionV1(session, 1),
            ExpectedAuthorityVectorV1.Create(session, []), SubmissionDispositionV1.WithdrawalTombstoned);

        var stale = await ReadWithClaim(session, staleClaim);
        var ineligible = await ReadWithClaim(session, withdrawn);

        Assert.IsType<SemanticAcceptanceProofResultV1.StaleAuthority>(stale);
        Assert.Equal(SubmissionDispositionV1.WithdrawalTombstoned,
            Assert.IsType<SemanticAcceptanceProofResultV1.Ineligible>(ineligible).Disposition);
    }

    [Fact]
    public async Task Reader_DistinguishesUnobservedPositionFromMalformedFactAtObservedPosition()
    {
        var session = Session();
        var empty = new OneBatchJournal(new ReadAuthorityRangeResultV1.Batch(session, 0, 0, 0, [], false));
        var malformed = Fact(1, session, OwnerSliceId.S1, DispositionSchema(), [0xff]);

        var absent = await SemanticAcceptanceProofReaderV1.ReadAsync(empty, new JournalPositionV1(session, 2));
        var invalid = await SemanticAcceptanceProofReaderV1.ReadAsync(
            new OneBatchJournal(new ReadAuthorityRangeResultV1.Batch(session, 1, 0, 1, [malformed], false)),
            new JournalPositionV1(session, 1));

        Assert.Equal(0, Assert.IsType<SemanticAcceptanceProofResultV1.NotObservedThrough>(absent).SnapshotThrough);
        Assert.IsType<SemanticAcceptanceProofResultV1.InvalidHistory>(invalid);
    }

    [Fact]
    public async Task Reader_IgnoresUnrelatedAxisChangesAndPinsAcrossConcurrentGrowth()
    {
        var session = Session();
        var expected = ExpectedAuthorityVectorV1.Create(session,
            [new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(First))]);
        var claim = new SubmissionDispositionChosenV1(OperationId.Create(), new JournalPositionV1(session, 1),
            expected, SubmissionDispositionV1.SubmissionClaimed);
        var first = Fact(1, session, OwnerSliceId.S2, AuthorityGenerationInitializationCodecV1.SchemaFor(AuthorityAxisId.Graph), EncodeInitialization(session, First, OwnerSliceId.S2));
        var second = Fact(2, session, OwnerSliceId.S1, DispositionSchema(), SubmissionDispositionChosenV1Codec.Encode(claim));
        var third = Fact(3, session, OwnerSliceId.S8, AuthorityGenerationInitializationCodecV1.SchemaFor(AuthorityAxisId.Route), EncodeInitialization(session, Second, OwnerSliceId.S8));
        var journal = new ScriptedJournal(
            new ReadAuthorityRangeResultV1.Batch(session, 3, 0, 3, [first, second], true),
            new ReadAuthorityRangeResultV1.Batch(session, 4, 2, 3, [third], false));

        var result = await SemanticAcceptanceProofReaderV1.ReadAsync(journal, new JournalPositionV1(session, 2), 2, 4096);

        var proven = Assert.IsType<SemanticAcceptanceProofResultV1.Proven>(result);
        Assert.Equal([long.MaxValue, 3], journal.Requests.Select(static request => request.ThroughInclusive));
        Assert.Equal([AuthorityAxisId.Graph, AuthorityAxisId.Route], proven.Current.Axes.Select(static axis => axis.AxisId));
    }

    [Fact]
    public async Task Reader_MapsAvailabilityFailuresAndEnforcesCancellationItself()
    {
        var session = Session();
        var position = new JournalPositionV1(session, 1);
        var unavailable = await SemanticAcceptanceProofReaderV1.ReadAsync(
            new OneBatchJournal(new ReadAuthorityRangeResultV1.StoreUnavailable(new BoundedAscii("offline"))), position);
        var oversized = await SemanticAcceptanceProofReaderV1.ReadAsync(
            new OneBatchJournal(new ReadAuthorityRangeResultV1.ItemTooLarge(position, 9, 8)), position, maximumEncodedBytes: 8);
        var thrown = await SemanticAcceptanceProofReaderV1.ReadAsync(new ThrowingJournal(), position);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Equal("offline", Assert.IsType<SemanticAcceptanceProofResultV1.OutcomeUnknown>(unavailable).SafeCode.ToString());
        Assert.Equal("item-too-large", Assert.IsType<SemanticAcceptanceProofResultV1.OutcomeUnknown>(oversized).SafeCode.ToString());
        Assert.Equal("store-exception", Assert.IsType<SemanticAcceptanceProofResultV1.OutcomeUnknown>(thrown).SafeCode.ToString());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await SemanticAcceptanceProofReaderV1.ReadAsync(new IgnoringCancellationJournal(), position, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task Reader_ReportsReplacementMalformedGenerationAndWrongDispositionOwnership()
    {
        var session = Session();
        var claim = new SubmissionDispositionChosenV1(OperationId.Create(), new JournalPositionV1(session, 1),
            ExpectedAuthorityVectorV1.Create(session, []), SubmissionDispositionV1.SubmissionClaimed);
        var claimFact = Fact(2, session, OwnerSliceId.S1, DispositionSchema(), SubmissionDispositionChosenV1Codec.Encode(claim));
        var replacement = StableId128.FromBytes(Convert.FromHexString("303132333435363738393a3b3c3d3e3f"));
        var runtime = Fact(3, session, OwnerSliceId.S1, AuthorityGenerationTransitionCodecV1.SchemaFor(AuthorityAxisId.Runtime),
            EncodeTransition(session, Stable(session.RuntimeGenerationId), replacement, OwnerSliceId.S1));
        var replaced = await SemanticAcceptanceProofReaderV1.ReadAsync(
            new OneBatchJournal(new ReadAuthorityRangeResultV1.Batch(session, 3, 0, 3,
                [Fact(1, session, OwnerSliceId.S4, new SchemaReferenceV1(SchemaId.Create(), 1, 0), [0x80]), claimFact, runtime], false)),
            new JournalPositionV1(session, 2));
        var malformed = await SemanticAcceptanceProofReaderV1.ReadAsync(
            new OneBatchJournal(new ReadAuthorityRangeResultV1.Batch(session, 2, 0, 2,
                [Fact(1, session, OwnerSliceId.S2, AuthorityGenerationInitializationCodecV1.SchemaFor(AuthorityAxisId.Graph), [0xff]), claimFact], false)),
            new JournalPositionV1(session, 2));
        var wrongOwner = Fact(2, session, OwnerSliceId.S4, DispositionSchema(), SubmissionDispositionChosenV1Codec.Encode(claim));
        var wrong = await SemanticAcceptanceProofReaderV1.ReadAsync(
            new OneBatchJournal(new ReadAuthorityRangeResultV1.Batch(session, 2, 0, 2,
                [Fact(1, session, OwnerSliceId.S4, new SchemaReferenceV1(SchemaId.Create(), 1, 0), [0x80]), wrongOwner], false)),
            new JournalPositionV1(session, 2));

        Assert.Equal(RuntimeGenerationId.FromValue(replacement), Assert.IsType<SemanticAcceptanceProofResultV1.GenerationReplaced>(replaced).ReplacedBy);
        Assert.IsType<SemanticAcceptanceProofResultV1.InvalidHistory>(malformed);
        Assert.IsType<SemanticAcceptanceProofResultV1.InvalidHistory>(wrong);
    }

    [Fact]
    public async Task Reader_RejectsAClaimThatNamesItselfAsItsPredecessor()
    {
        var session = Session();
        var claim = new SubmissionDispositionChosenV1(OperationId.Create(), new JournalPositionV1(session, 2),
            ExpectedAuthorityVectorV1.Create(session, []), SubmissionDispositionV1.SubmissionClaimed);
        var facts = new[]
        {
            Fact(1, session, OwnerSliceId.S4, new SchemaReferenceV1(SchemaId.Create(), 1, 0), [0x80]),
            Fact(2, session, OwnerSliceId.S1, DispositionSchema(), SubmissionDispositionChosenV1Codec.Encode(claim)),
        };
        var result = await SemanticAcceptanceProofReaderV1.ReadAsync(
            new OneBatchJournal(new ReadAuthorityRangeResultV1.Batch(session, 2, 0, 2, facts, false)),
            new JournalPositionV1(session, 2));
        Assert.IsType<SemanticAcceptanceProofResultV1.InvalidHistory>(result);
    }

    private static async Task<SemanticAcceptanceProofResultV1> ReadWithClaim(
        SessionAuthorityStampV1 session,
        SubmissionDispositionChosenV1 claim)
    {
        var facts = new[]
        {
            Fact(1, session, OwnerSliceId.S2, AuthorityGenerationInitializationCodecV1.SchemaFor(AuthorityAxisId.Graph), EncodeInitialization(session, First, OwnerSliceId.S2)),
            Fact(2, session, OwnerSliceId.S1, DispositionSchema(), SubmissionDispositionChosenV1Codec.Encode(claim)),
        };
        return await SemanticAcceptanceProofReaderV1.ReadAsync(
            new OneBatchJournal(new ReadAuthorityRangeResultV1.Batch(session, 2, 0, 2, facts, false)),
            new JournalPositionV1(session, 2));
    }

    private static readonly StableId128 First = StableId128.FromBytes(Convert.FromHexString("0102030405060708090a0b0c0d0e0f10"));
    private static readonly StableId128 Second = StableId128.FromBytes(Convert.FromHexString("1112131415161718191a1b1c1d1e1f20"));

    private static SessionAuthorityStampV1 Session() => new(
        RuntimeGenerationId.FromValue(StableId128.FromBytes(Convert.FromHexString("000102030405060708090a0b0c0d0e0f"))),
        LiveSessionId.FromValue(StableId128.FromBytes(Convert.FromHexString("202122232425262728292a2b2c2d2e2f"))));

    private static SchemaReferenceV1 DispositionSchema() => new(
        AuthoritySchemaIdentityV1.Derive(new BoundedAscii(SubmissionDispositionChosenV1Codec.SchemaId)), 1, 0);

    private static AuthorityFactEnvelopeV1 Fact(long sequence, SessionAuthorityStampV1 session, OwnerSliceId owner,
        SchemaReferenceV1 schema, byte[] payload) => new(
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

    private static StableId128 Stable(RuntimeGenerationId value)
    {
        Span<byte> bytes = stackalloc byte[16];
        Assert.True(value.TryWriteBytes(bytes));
        return StableId128.FromBytes(bytes);
    }

    private static byte[] EncodeTransition(SessionAuthorityStampV1 session, StableId128 expected, StableId128 proposed, OwnerSliceId owner)
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

    private sealed class OneBatchJournal(ReadAuthorityRangeResultV1 result) : IAuthorityJournalV1
    {
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request, CancellationToken cancellationToken = default) => ValueTask.FromResult(result);
    }

    private sealed class ScriptedJournal(params ReadAuthorityRangeResultV1[] results) : IAuthorityJournalV1
    {
        private readonly Queue<ReadAuthorityRangeResultV1> _results = new(results);
        internal List<ReadAuthorityRangeV1> Requests { get; } = [];
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

    private sealed class IgnoringCancellationJournal : IAuthorityJournalV1
    {
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("must not be called");
    }
}
