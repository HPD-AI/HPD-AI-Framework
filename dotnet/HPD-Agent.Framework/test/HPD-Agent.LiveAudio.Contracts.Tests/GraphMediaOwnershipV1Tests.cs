using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphMediaOwnershipV1Tests
{
    [Fact]
    public void Media_exact_maxima_and_derived_values_are_closed()
    {
        Assert.True(Media(0, 10_000_000_000, 16_777_216, 1_875, 256, 0,
            GraphMediaDiscontinuityKindV1.ResetBefore, null, out var media));
        Assert.Equal(10_000_000_000, media!.Duration); Assert.Equal(480_000, media.SampleAmount);
    }

    [Theory]
    [InlineData(-1, 1, 1, 1, 1)]
    [InlineData(0, 0, 1, 1, 1)]
    [InlineData(0, 10_000_000_001, 1, 1, 1)]
    [InlineData(0, 1, 0, 1, 1)]
    [InlineData(0, 1, 16_777_217, 1, 1)]
    [InlineData(0, 1, 1, 0, 1)]
    [InlineData(0, 1, 1, 480_001, 1)]
    [InlineData(0, 1, 1, 240_001, 2)]
    public void Media_numeric_max_plus_one_and_invalid_ranges_fail(long start, long end, long bytes, long frames, ushort channels) =>
        Assert.False(Media(start, end, bytes, frames, channels, 0, GraphMediaDiscontinuityKindV1.ResetBefore, null, out _));

    [Fact]
    public void Format_clock_and_discontinuity_bounds_fail()
    {
        Assert.False(Create(default, 1, 48_000, 1, 2, Id(6), 1, GraphMediaDiscontinuityKindV1.ResetBefore));
        Assert.False(Create(Id(5), 0, 48_000, 1, 2, Id(6), 1, GraphMediaDiscontinuityKindV1.ResetBefore));
        Assert.False(Create(Id(5), 1, 0, 1, 2, Id(6), 1, GraphMediaDiscontinuityKindV1.ResetBefore));
        Assert.False(Create(Id(5), 1, 768_001, 1, 2, Id(6), 1, GraphMediaDiscontinuityKindV1.ResetBefore));
        Assert.False(Create(Id(5), 1, 48_000, 0, 2, Id(6), 1, GraphMediaDiscontinuityKindV1.ResetBefore));
        Assert.False(Create(Id(5), 1, 48_000, 257, 2, Id(6), 1, GraphMediaDiscontinuityKindV1.ResetBefore));
        Assert.False(Create(Id(5), 1, 48_000, 1, 0, Id(6), 1, GraphMediaDiscontinuityKindV1.ResetBefore));
        Assert.False(Create(Id(5), 1, 48_000, 1, 33, Id(6), 1, GraphMediaDiscontinuityKindV1.ResetBefore));
        Assert.False(Create(Id(5), 1, 48_000, 1, 2, default, 1, GraphMediaDiscontinuityKindV1.ResetBefore));
        Assert.False(Create(Id(5), 1, 48_000, 1, 2, Id(6), 0, GraphMediaDiscontinuityKindV1.ResetBefore));
        Assert.False(Create(Id(5), 1, 48_000, 1, 2, Id(6), 1, (GraphMediaDiscontinuityKindV1)3));
    }

    [Fact]
    public void Continuity_gap_reset_and_sequence_overflow_are_exact()
    {
        Assert.False(Media(0, 1, 1, 1, 1, 0, GraphMediaDiscontinuityKindV1.Continuous, null, out _));
        Assert.False(Media(0, 1, 1, 1, 1, 0, GraphMediaDiscontinuityKindV1.GapBefore, null, out _));
        Media(0, 10, 1, 1, 1, 7, GraphMediaDiscontinuityKindV1.ResetBefore, null, out var prior);
        Assert.True(Media(10, 20, 1, 1, 1, 8, GraphMediaDiscontinuityKindV1.Continuous, prior, out _));
        Assert.False(Media(11, 20, 1, 1, 1, 8, GraphMediaDiscontinuityKindV1.Continuous, prior, out _));
        Assert.True(Media(11, 20, 1, 1, 1, 8, GraphMediaDiscontinuityKindV1.GapBefore, prior, out _));
        Assert.False(Media(9, 20, 1, 1, 1, 8, GraphMediaDiscontinuityKindV1.GapBefore, prior, out _));
        Assert.True(Media(0, 1, 1, 1, 1, 0, GraphMediaDiscontinuityKindV1.ResetBefore, prior, out _));
        Media(0, 1, 1, 1, 1, ulong.MaxValue, GraphMediaDiscontinuityKindV1.ResetBefore, null, out var max);
        Assert.False(Media(1, 2, 1, 1, 1, 0, GraphMediaDiscontinuityKindV1.Continuous, max, out _));
    }

    [Fact]
    public void Transfer_hash_and_dispose_hash_match_frozen_goldens()
    {
        const string transferPreimage = "6870642d73322d67726170682d6d656469612d6f776e65722d7472616e736974696f6e2d7631000100000010000102030405060708090a0b0c0d0e0f0200000001010300000010101112131415161718191a1b1c1d1e1f040000001101202122232425262728292a2b2c2d2e2f05000000540100000010303132333435363738393a3b3c3d3e3f0200000010404142434445464748494a4b4c4d4e4f0300000010505152535455565758595a5b5c5d5e5f0400000010606162636465666768696a6b6c6d6e6f060000009a01000000080000000000000000020000000800000000000f42400300000010707172737475767778797a7b7c7d7e7f04000000040000000105000000040000bb8006000000020002070000000200020800000010808182838485868788898a8b8c8d8e8f0900000004000000010a0000000800000000000000070b00000001000c0000000800000000000010000d0000000800000000000001e007000000080000000000000009";
        Assert.Equal("eb8413e9ed3c80dcba622dfd81467570f584848ea56e6d421b1965dc3aa84872",
            Hash256.Compute(Convert.FromHexString(transferPreimage)).ToString());

        var disposeKey = new GraphMediaOwnerKeyV1(
            new(RuntimeGenerationId.FromValue(HexId("2233445566778899aabbccddeeff0011")), LiveSessionId.FromValue(HexId("00112233445566778899aabbccddeeff"))),
            GraphGenerationId.FromValue(HexId("112233445566778899aabbccddeeff00")), HexId("33445566778899aabbccddeeff001122"));
        Assert.True(GraphMediaBindingV1.TryCreate(42, 1_000_000_042, HexId("445566778899aabbccddeeff00112233"), 2, 96_000, 1, 4,
            HexId("5566778899aabbccddeeff0011223344"), 3, ulong.MaxValue, GraphMediaDiscontinuityKindV1.ResetBefore, 8192, 960, null, out var disposeMedia));
        var dispose = GraphMediaOwnershipCodecV1.OwnerTransition(OperationHex("f0e0d0c0b0a090807060504030201000"),
            GraphMediaOwnerActionV1.Dispose, HexId("ffeeddccbbaa99887766554433221100"), null, disposeKey, disposeMedia!, ulong.MaxValue - 1, out _);
        Assert.Equal("89c7dac6eadf8795d1bfacbf76e707e2ce9eeec049289b2ed9283f839c348011", dispose.ToString());
    }

    [Fact]
    public void Ledger_fingerprint_pre_none_and_post_goldens_are_exact()
    {
        var owner = GraphMediaOwnershipCodecV1.GoldenOwner(Id(1), RepeatHash(0x11), RepeatHash(0x22), 2, GraphMediaOwnerStateV1.Disposed);
        var borrow = GraphMediaOwnershipCodecV1.GoldenBorrow(Id(1), Id(2), RepeatHash(0x33), RepeatHash(0x66), GraphMediaBorrowStateV1.Returned);
        var receipt = GraphMediaOwnershipCodecV1.GoldenReceipt(Operation(3), RepeatHash(0x44), GraphMediaOwnerTransitionResultV1.Disposed, Id(1), null, 2);
        Assert.Equal("bce92e43e5c4512b3607890af70df4c91445708ed31a282cb4202c6d1d978543",
            GraphMediaOwnershipCodecV1.FingerprintEncoded([owner], [borrow], [receipt], []).ToString());
        var activeOwner = GraphMediaOwnershipCodecV1.GoldenOwner(Id(1), RepeatHash(0x11), RepeatHash(0x22), 1, GraphMediaOwnerStateV1.Owned);
        var activeBorrow = GraphMediaOwnershipCodecV1.GoldenBorrow(Id(1), Id(2), RepeatHash(0x33), null, GraphMediaBorrowStateV1.Active);
        Assert.Equal("3e27ec64ac65c74824e0cc3ea2a580bf063c6b55f13c5e9ff3cfc35dd082e756",
            GraphMediaOwnershipCodecV1.FingerprintEncoded([activeOwner], [activeBorrow], [], []).ToString());
        var tombstone = GraphMediaOwnershipCodecV1.GoldenTombstone(Operation(4), RepeatHash(0x55), 1, 1);
        Assert.Equal("5a0d574064f509efa652da962554fb3a566ec79b685b8d61a9a9f788a3f06c4b",
            GraphMediaOwnershipCodecV1.FingerprintEncoded([owner], [], [], [tombstone]).ToString());
    }

    [Fact]
    public void Transfer_preserves_binding_and_media_and_records_only_acceptance()
    {
        var ledger = Ledger(out var source); var destination = Id(20); var operation = Operation(21);
        var hash = OwnerHash(ledger, operation, GraphMediaOwnerActionV1.Transfer, source, destination, 1);
        var moved = ledger.Transition(Session(), Graph(), operation, GraphMediaOwnerActionV1.Transfer, source, destination, 1, hash);
        Assert.Equal(GraphMediaOwnerTransitionResultV1.Transferred, moved.Result); Assert.Equal(2, moved.Ledger.Owners.Count);
        Assert.Equal(moved.Ledger.Owners[source].Key, moved.Ledger.Owners[destination].Key);
        Assert.Same(moved.Ledger.Owners[source].Media, moved.Ledger.Owners[destination].Media);
        Assert.Equal(2UL, moved.Ledger.Owners[source].Version); Assert.Equal(1UL, moved.Ledger.Owners[destination].Version);
        Assert.Equal(GraphMediaOwnerTransitionResultV1.IdempotentAccepted,
            moved.Ledger.Transition(Session(), Graph(), operation, GraphMediaOwnerActionV1.Transfer, source, destination, 1, hash).Result);
        Assert.Equal(GraphMediaOwnerTransitionResultV1.ContradictoryDuplicate,
            moved.Ledger.Transition(Session(), Graph(), operation, GraphMediaOwnerActionV1.Transfer, source, Id(22), 1, Hash(2)).Result);
    }

    [Fact]
    public void Every_failed_owner_row_returns_same_projection_and_no_receipt()
    {
        var ledger = Ledger(out var source); var op = Operation(30);
        AssertFailure(ledger, ledger.Transition(Session(), Graph(), op, GraphMediaOwnerActionV1.Dispose, source, Id(9), 1, Hash(1)), GraphMediaOwnerTransitionResultV1.InvalidRequest);
        AssertFailure(ledger, ledger.Transition(OtherSession(), Graph(), op, GraphMediaOwnerActionV1.Dispose, source, null, 1, Hash(1)), GraphMediaOwnerTransitionResultV1.StaleGeneration);
        AssertFailure(ledger, ledger.Transition(Session(), Graph(), op, GraphMediaOwnerActionV1.Dispose, Id(31), null, 1, Hash(1)), GraphMediaOwnerTransitionResultV1.SourceNotFound);
        AssertFailure(ledger, ledger.Transition(Session(), Graph(), op, GraphMediaOwnerActionV1.Dispose, source, null, 2,
            OwnerHash(ledger, op, GraphMediaOwnerActionV1.Dispose, source, null, 2)), GraphMediaOwnerTransitionResultV1.VersionConflict);
    }

    [Fact]
    public void Borrow_acquire_return_identity_bounds_and_owner_race_are_closed()
    {
        var ledger = Ledger(out var source); var token = Id(40); var acquire = Hash(1); var returned = Hash(2);
        var borrowed = ledger.Acquire(Session(), Graph(), source, token, acquire);
        Assert.Equal(GraphMediaBorrowResultV1.Borrowed, borrowed.Result);
        Assert.Equal(GraphMediaBorrowResultV1.IdempotentBorrowed, borrowed.Ledger.Acquire(Session(), Graph(), source, token, acquire).Result);
        Assert.Equal(GraphMediaBorrowResultV1.ContradictoryDuplicate, borrowed.Ledger.Acquire(Session(), Graph(), source, token, Hash(3)).Result);
        var op = Operation(41); Assert.Equal(GraphMediaOwnerTransitionResultV1.BorrowOutstanding,
            borrowed.Ledger.Transition(Session(), Graph(), op, GraphMediaOwnerActionV1.Dispose, source, null, 1,
                OwnerHash(borrowed.Ledger, op, GraphMediaOwnerActionV1.Dispose, source, null, 1)).Result);
        var done = borrowed.Ledger.Return(Session(), Graph(), source, token, returned);
        Assert.Equal(GraphMediaBorrowResultV1.Returned, done.Result);
        Assert.Equal(GraphMediaBorrowResultV1.IdempotentReturned, done.Ledger.Return(Session(), Graph(), source, token, returned).Result);
        Assert.Equal(GraphMediaBorrowResultV1.ContradictoryDuplicate, done.Ledger.Return(Session(), Graph(), source, token, Hash(4)).Result);
        Assert.Null(borrowed.Ledger.Borrows.Single().ReturnHash); Assert.Equal(returned, done.Ledger.Borrows.Single().ReturnHash);
    }

    [Fact]
    public void Active_borrow_limit_is_64_and_returned_rows_remain_retained()
    {
        var ledger = Ledger(out var source);
        for (byte i = 1; i <= 64; i++) ledger = ledger.Acquire(Session(), Graph(), source, Id(i), Hash(i)).Ledger;
        Assert.Equal(GraphMediaBorrowResultV1.BorrowLimitReached, ledger.Acquire(Session(), Graph(), source, Id(100), Hash(100)).Result);
        ledger = ledger.Return(Session(), Graph(), source, Id(1), Hash(101)).Ledger;
        Assert.Equal(GraphMediaBorrowResultV1.Borrowed, ledger.Acquire(Session(), Graph(), source, Id(100), Hash(100)).Result);
        Assert.Equal(64, ledger.Borrows.Count);
    }

    [Fact]
    public void Batch_copy_is_atomic_ordered_and_preserves_exact_owner_identity()
    {
        var ledger = Ledger(out var source); var before = ledger.Fingerprint;
        var destinations = new[] { Id(70), Id(71), Id(72) };
        var copied = ledger.CopyOwners(Session(), Graph(), source, destinations);
        Assert.Equal(GraphMediaOwnershipBatchCopyResultV1.Copied, copied.Result);
        Assert.NotSame(ledger, copied.Ledger); Assert.NotEqual(before, copied.Ledger.Fingerprint);
        var original = ledger.Owners[source];
        Assert.Equal(4, copied.Ledger.Owners.Count);
        foreach (var destination in destinations)
        {
            var owner = copied.Ledger.Owners[destination];
            Assert.Equal(destination, owner.OwnerId); Assert.Equal(original.Key, owner.Key);
            Assert.Same(original.Media, owner.Media); Assert.Equal(GraphMediaOwnerStateV1.Owned, owner.State); Assert.Equal(1UL, owner.Version);
        }
        Assert.Equal(GraphMediaOwnershipBatchCopyResultV1.InvalidRequest,
            ledger.CopyOwners(Session(), Graph(), source, [destinations[1], destinations[0]]).Result);
        Assert.Equal(GraphMediaOwnershipBatchCopyResultV1.DestinationCollision,
            ledger.CopyOwners(Session(), Graph(), source, [source]).Result);
        Assert.Equal(before, ledger.Fingerprint); Assert.Single(ledger.Owners);
    }

    [Fact]
    public void Compaction_is_exhaustive_one_shot_and_tombstoned()
    {
        var ledger = Ledger(out var source); var token = Id(50);
        ledger = ledger.Acquire(Session(), Graph(), source, token, Hash(1)).Ledger;
        ledger = ledger.Return(Session(), Graph(), source, token, Hash(2)).Ledger;
        var disposeOp = Operation(51); var disposeHash = OwnerHash(ledger, disposeOp, GraphMediaOwnerActionV1.Dispose, source, null, 1);
        ledger = ledger.Transition(Session(), Graph(), disposeOp, GraphMediaOwnerActionV1.Dispose, source, null, 1, disposeHash).Ledger;
        var expected = ledger.Fingerprint; var compactOp = Operation(52);
        var request = GraphMediaOwnershipCodecV1.Compaction(compactOp, Session(), Graph(), expected, [token], [disposeOp]);
        Assert.Equal(GraphMediaCompactionResultV1.NotEligible,
            ledger.Compact(Session(), Graph(), compactOp, expected, [], [disposeOp],
                GraphMediaOwnershipCodecV1.Compaction(compactOp, Session(), Graph(), expected, [], [disposeOp])).Result);
        var compacted = ledger.Compact(Session(), Graph(), compactOp, expected, [token], [disposeOp], request);
        Assert.Equal(GraphMediaCompactionResultV1.Compacted, compacted.Result); Assert.Empty(compacted.Ledger.Borrows); Assert.Empty(compacted.Ledger.Receipts);
        var retry = compacted.Ledger.Compact(Session(), Graph(), compactOp, expected, [token], [disposeOp], request);
        Assert.Equal(GraphMediaCompactionResultV1.IdempotentCompacted, retry.Result); Assert.Equal(compacted.PostFingerprint, retry.PostFingerprint);
        Assert.Equal(GraphMediaCompactionResultV1.ContradictoryDuplicate,
            compacted.Ledger.Compact(Session(), Graph(), compactOp, expected, [], [],
                GraphMediaOwnershipCodecV1.Compaction(compactOp, Session(), Graph(), expected, [], [])).Result);
    }

    [Fact]
    public void Compaction_hostile_ids_fences_and_counts_are_closed_failures()
    {
        var ledger = Ledger(out _); var op = Operation(60); var fingerprint = ledger.Fingerprint;
        Assert.Equal(GraphMediaCompactionResultV1.InvalidRequest,
            ledger.Compact(default, Graph(), op, fingerprint, [], [], Hash(1)).Result);
        Assert.Equal(GraphMediaCompactionResultV1.InvalidRequest,
            ledger.Compact(Session(), default, op, fingerprint, [], [], Hash(1)).Result);
        Assert.Equal(GraphMediaCompactionResultV1.InvalidRequest,
            ledger.Compact(Session(), Graph(), op, fingerprint, [default], [], Hash(1)).Result);
        Assert.Equal(GraphMediaCompactionResultV1.InvalidRequest,
            ledger.Compact(Session(), Graph(), op, fingerprint, [], [default], Hash(1)).Result);
        Assert.Equal(GraphMediaCompactionResultV1.InvalidRequest,
            ledger.Compact(Session(), Graph(), op, fingerprint, Enumerable.Repeat(Id(1), 24_577).ToArray(), [], Hash(1)).Result);
        Assert.Equal(GraphMediaCompactionResultV1.InvalidRequest,
            ledger.Compact(Session(), Graph(), op, fingerprint, [], Enumerable.Repeat(Operation(1), 257).ToArray(), Hash(1)).Result);
    }

    [Fact]
    public void Batch_one_is_internal_pure_and_has_no_escape_surface()
    {
        Assert.False(typeof(GraphMediaOwnershipLedgerV1).IsPublic); Assert.False(typeof(GraphMediaBindingV1).IsPublic);
        Assert.All(typeof(GraphMediaOwnershipLedgerV1).GetMethods(System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly), method =>
            Assert.DoesNotContain("View", method.ReturnType.Name, StringComparison.Ordinal));
    }

    private static void AssertFailure(GraphMediaOwnershipLedgerV1 original, GraphMediaOwnerTransitionV1 actual, GraphMediaOwnerTransitionResultV1 expected)
    { Assert.Equal(expected, actual.Result); Assert.Same(original, actual.Ledger); Assert.Empty(original.Receipts); }
    private static Hash256 OwnerHash(GraphMediaOwnershipLedgerV1 ledger, OperationId operation, GraphMediaOwnerActionV1 action,
        StableId128 source, StableId128? destination, ulong version) => GraphMediaOwnershipCodecV1.OwnerTransition(operation,
            action, source, destination, ledger.Owners[source].Key, ledger.Owners[source].Media, version, out _);
    private static GraphMediaOwnershipLedgerV1 Ledger(out StableId128 owner)
    { Media(0, 1, 1, 1, 1, 0, GraphMediaDiscontinuityKindV1.ResetBefore, null, out var media); owner = Id(10); return GraphMediaOwnershipLedgerV1.Create(Key(), owner, media!); }
    private static GraphMediaOwnerKeyV1 Key() => new(Session(), Graph(), Id(9));
    private static bool Media(long start, long end, long bytes, long frames, ushort channels, ulong sequence,
        GraphMediaDiscontinuityKindV1 kind, GraphMediaBindingV1? prior, out GraphMediaBindingV1? value) =>
        GraphMediaBindingV1.TryCreate(start, end, Id(5), 1, 48_000, channels, 2, Id(6), 1, sequence, kind, bytes, frames, prior, out value);
    private static bool Create(StableId128 format, uint revision, uint rate, ushort channels, ushort sampleBytes,
        StableId128 clock, uint clockRevision, GraphMediaDiscontinuityKindV1 kind) => GraphMediaBindingV1.TryCreate(
            0, 1, format, revision, rate, channels, sampleBytes, clock, clockRevision, 0, kind, 1, 1, null, out _);
    private static SessionAuthorityStampV1 Session() => new(RuntimeGenerationId.FromValue(Id(1)), LiveSessionId.FromValue(Id(2)));
    private static SessionAuthorityStampV1 OtherSession() => new(RuntimeGenerationId.FromValue(Id(99)), LiveSessionId.FromValue(Id(2)));
    private static GraphGenerationId Graph() => GraphGenerationId.FromValue(Id(3));
    private static OperationId Operation(byte x) => OperationId.FromValue(Id(x));
    private static OperationId OperationHex(string x) => OperationId.FromValue(HexId(x));
    private static StableId128 Id(byte x) { var b = new byte[16]; b[^1] = x; return StableId128.FromBytes(b); }
    private static StableId128 HexId(string x) => StableId128.FromBytes(Convert.FromHexString(x));
    private static Hash256 Hash(byte x) { var b = new byte[32]; b[^1] = x; return Hash256.FromBytes(b); }
    private static Hash256 RepeatHash(byte x) => Hash256.FromBytes(Enumerable.Repeat(x, 32).ToArray());
}
