using System.Buffers;
using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal enum GraphMediaDiscontinuityKind : byte { Continuous, GapBefore, ResetBefore }
internal enum GraphMediaOwnerState : byte { Owned, Transferred, Disposed }
internal enum GraphMediaBorrowState : byte { Active, Returned }
internal enum GraphMediaOwnerAction : byte { Transfer = 1, Dispose = 2 }
internal enum GraphMediaOwnerTransitionResult : byte
{
    Transferred, Disposed, IdempotentAccepted, InvalidRequest, StaleGeneration,
    ContradictoryDuplicate, SourceNotFound, NotOwner, AlreadyDisposed, VersionConflict,
    BorrowOutstanding, DestinationCollision, OwnerLimitReached, OperationReceiptLimitReached
}
internal enum GraphMediaBorrowResult : byte
{
    Borrowed, IdempotentBorrowed, Returned, IdempotentReturned, InvalidRequest,
    StaleGeneration, ContradictoryDuplicate, SourceNotFound, NotOwner, AlreadyDisposed,
    BorrowNotFound, BorrowLimitReached, BorrowRowLimitReached
}
internal enum GraphMediaCompactionResult : byte
{ Compacted, IdempotentCompacted, InvalidRequest, StaleGeneration, NotEligible, FingerprintConflict, ContradictoryDuplicate }
internal enum GraphMediaOwnershipBatchCopyResult : byte
{ Copied, InvalidRequest, StaleGeneration, SourceNotFound, NotOwner, AlreadyDisposed, BorrowOutstanding, DestinationCollision, OwnerLimitReached }

internal readonly record struct GraphMediaOwnerKey(
    SessionAuthorityStampV1 Session, GraphGenerationId GraphGeneration, StableId128 MediaId)
{ internal bool IsValid => Session.IsValid && GraphGeneration.IsValid && !MediaId.Equals(default); }

internal sealed record GraphMediaBinding
{
    internal const long MaximumDurationNanoseconds = 10_000_000_000;
    internal const long MaximumByteLength = 16_777_216;
    internal const long MaximumSampleAmount = 480_000;
    private GraphMediaBinding(long start, long end, StableId128 format, uint formatRevision, uint rate,
        ushort channels, ushort bytesPerSample, StableId128 clock, uint clockRevision, ulong sequence,
        GraphMediaDiscontinuityKind discontinuity, long bytes, long frames)
    { Start = start; EndExclusive = end; FormatId = format; FormatRevision = formatRevision; SampleRateHz = rate;
      ChannelCount = channels; BytesPerSample = bytesPerSample; ClockId = clock; ClockRevision = clockRevision;
      Sequence = sequence; Discontinuity = discontinuity; ByteLength = bytes; FrameCount = frames; }
    internal long Start { get; } internal long EndExclusive { get; } internal long Duration => EndExclusive - Start;
    internal StableId128 FormatId { get; } internal uint FormatRevision { get; } internal uint SampleRateHz { get; }
    internal ushort ChannelCount { get; } internal ushort BytesPerSample { get; } internal StableId128 ClockId { get; }
    internal uint ClockRevision { get; } internal ulong Sequence { get; }
    internal GraphMediaDiscontinuityKind Discontinuity { get; } internal long ByteLength { get; }
    internal long FrameCount { get; } internal long SampleAmount => checked(FrameCount * ChannelCount);

    internal static bool TryCreate(long start, long end, StableId128 format, uint formatRevision, uint rate,
        ushort channels, ushort bytesPerSample, StableId128 clock, uint clockRevision, ulong sequence,
        GraphMediaDiscontinuityKind discontinuity, long bytes, long frames, GraphMediaBinding? prior,
        out GraphMediaBinding? value)
    {
        value = null;
        if (start < 0 || end <= start || end - start > MaximumDurationNanoseconds || format.Equals(default) ||
            formatRevision == 0 || rate is 0 or > 768_000 || channels is 0 or > 256 || bytesPerSample is 0 or > 32 ||
            clock.Equals(default) || clockRevision == 0 || discontinuity > GraphMediaDiscontinuityKind.ResetBefore ||
            bytes is <= 0 or > MaximumByteLength || frames <= 0 || frames > MaximumSampleAmount / channels ||
            !Continuity(start, format, formatRevision, rate, channels, bytesPerSample, clock, clockRevision,
                sequence, discontinuity, prior)) return false;
        value = new(start, end, format, formatRevision, rate, channels, bytesPerSample, clock, clockRevision,
            sequence, discontinuity, bytes, frames); return true;
    }
    private static bool Continuity(long start, StableId128 format, uint revision, uint rate, ushort channels,
        ushort sampleBytes, StableId128 clock, uint clockRevision, ulong sequence,
        GraphMediaDiscontinuityKind kind, GraphMediaBinding? prior)
    {
        if (kind == GraphMediaDiscontinuityKind.ResetBefore) return true;
        if (prior is null || prior.Sequence == ulong.MaxValue || sequence != prior.Sequence + 1 ||
            !clock.Equals(prior.ClockId) || clockRevision != prior.ClockRevision) return false;
        if (kind == GraphMediaDiscontinuityKind.GapBefore) return start >= prior.EndExclusive;
        return start == prior.EndExclusive && format.Equals(prior.FormatId) && revision == prior.FormatRevision &&
            rate == prior.SampleRateHz && channels == prior.ChannelCount && sampleBytes == prior.BytesPerSample;
    }
}

internal sealed record GraphMediaOwnerRecord(StableId128 OwnerId, GraphMediaOwnerKey Key,
    GraphMediaBinding Media, GraphMediaOwnerState State, ulong Version);
internal sealed record GraphMediaBorrowRecord(StableId128 OwnerId, StableId128 TokenId,
    Hash256 AcquireHash, Hash256? ReturnHash, GraphMediaBorrowState State);
internal sealed record GraphMediaOwnerReceipt(OperationId OperationId, Hash256 RequestHash,
    GraphMediaOwnerTransitionResult Result, StableId128 SourceOwnerId, StableId128? DestinationOwnerId, ulong SourcePostVersion);
internal sealed record GraphMediaCompactionTombstone(OperationId OperationId, Hash256 RequestHash,
    ushort RemovedBorrowCount, ushort RemovedReceiptCount, Hash256 PostFingerprint);
internal sealed record GraphMediaOwnerTransition(GraphMediaOwnerTransitionResult Result, GraphMediaOwnershipLedger Ledger);
internal sealed record GraphMediaBorrowTransition(GraphMediaBorrowResult Result, GraphMediaOwnershipLedger Ledger);
internal sealed record GraphMediaCompactionTransition(GraphMediaCompactionResult Result,
    GraphMediaOwnershipLedger Ledger, ushort RemovedBorrowCount, ushort RemovedReceiptCount, Hash256 PostFingerprint);
internal sealed record GraphMediaOwnershipBatchCopyTransition(GraphMediaOwnershipBatchCopyResult Result,
    GraphMediaOwnershipLedger Ledger);

internal static class GraphMediaOwnershipCodec
{
    private delegate bool IdWriter(Span<byte> destination);
    private static readonly byte[] OwnerDomain = Encoding.UTF8.GetBytes("hpd-s2-graph-media-owner-transition-v1\0");
    private static readonly byte[] FingerprintDomain = Encoding.UTF8.GetBytes("hpd-s2-graph-media-owner-ledger-fingerprint-v1\0");
    private static readonly byte[] CompactionDomain = Encoding.UTF8.GetBytes("hpd-s2-graph-media-owner-compaction-v1\0");
    internal static Hash256 OwnerTransition(OperationId operation, GraphMediaOwnerAction action,
        StableId128 sourceOwnerId, StableId128? destinationOwnerId, GraphMediaOwnerKey key,
        GraphMediaBinding media, ulong expectedVersion, out byte[] preimage)
    {
        var writer = new ArrayBufferWriter<byte>(); Raw(writer, OwnerDomain); Field(writer, 1, Id(operation.TryWriteBytes));
        Field(writer, 2, [(byte)action]); Field(writer, 3, Id(sourceOwnerId.TryWriteBytes));
        Field(writer, 4, Optional(destinationOwnerId)); Field(writer, 5, OwnerKey(key));
        Field(writer, 6, Media(media)); Field(writer, 7, U64(expectedVersion));
        preimage = writer.WrittenSpan.ToArray(); return Hash256.Compute(preimage);
    }
    internal static Hash256 Compaction(OperationId operation, SessionAuthorityStampV1 session,
        GraphGenerationId graph, Hash256 expectedFingerprint, IReadOnlyList<StableId128> returnedTokens,
        IReadOnlyList<OperationId> receipts)
    {
        var w = new ArrayBufferWriter<byte>(); Raw(w, CompactionDomain); Field(w, 1, Id(operation.TryWriteBytes));
        Field(w, 2, Id(session.LiveSessionId.TryWriteBytes)); Field(w, 3, Id(graph.TryWriteBytes));
        Field(w, 4, Id(session.RuntimeGenerationId.TryWriteBytes)); Field(w, 5, Bytes(expectedFingerprint));
        Field(w, 6, IdList(returnedTokens.Select(x => Id(x.TryWriteBytes))));
        Field(w, 7, IdList(receipts.Select(x => Id(x.TryWriteBytes)))); return Hash256.Compute(w.WrittenSpan);
    }
    internal static Hash256 Fingerprint(IEnumerable<GraphMediaOwnerRecord> owners,
        IEnumerable<GraphMediaBorrowRecord> borrows, IEnumerable<GraphMediaOwnerReceipt> receipts,
        IEnumerable<GraphMediaCompactionTombstone> tombstones)
    {
        var w = new ArrayBufferWriter<byte>(); Raw(w, FingerprintDomain);
        Section(w, 1, owners.OrderBy(x => Hex(x.OwnerId)).Select(Owner));
        Section(w, 2, borrows.OrderBy(x => Hex(x.OwnerId)).ThenBy(x => Hex(x.TokenId)).Select(Borrow));
        Section(w, 3, receipts.OrderBy(x => Hex(x.OperationId)).Select(Receipt));
        Section(w, 4, tombstones.OrderBy(x => Hex(x.OperationId)).Select(Tombstone));
        return Hash256.Compute(w.WrittenSpan);
    }
    internal static Hash256 FingerprintEncoded(IReadOnlyList<byte[]> owners, IReadOnlyList<byte[]> borrows,
        IReadOnlyList<byte[]> receipts, IReadOnlyList<byte[]> tombstones)
    {
        var w = new ArrayBufferWriter<byte>(); Raw(w, FingerprintDomain); Section(w, 1, owners);
        Section(w, 2, borrows); Section(w, 3, receipts); Section(w, 4, tombstones);
        return Hash256.Compute(w.WrittenSpan);
    }
    internal static byte[] GoldenOwner(StableId128 id, Hash256 key, Hash256 media, ulong version, GraphMediaOwnerState state) =>
        Record((1, Id(id.TryWriteBytes)), (2, Bytes(key)), (3, Bytes(media)), (4, U64(version)), (5, [(byte)state]));
    internal static byte[] GoldenBorrow(StableId128 owner, StableId128 token, Hash256 acquire, Hash256? returned,
        GraphMediaBorrowState state) => Record((1, Id(owner.TryWriteBytes)), (2, Id(token.TryWriteBytes)),
            (3, Bytes(acquire)), (4, returned is { } h ? [1, .. Bytes(h)] : [0]), (5, [(byte)state]));
    internal static byte[] GoldenReceipt(OperationId operation, Hash256 request, GraphMediaOwnerTransitionResult result,
        StableId128 source, StableId128? destination, ulong version) => Receipt(new(operation, request, result, source, destination, version));
    internal static byte[] GoldenTombstone(OperationId operation, Hash256 request, ushort borrows, ushort receipts) =>
        Tombstone(new(operation, request, borrows, receipts, default));
    private static byte[] Owner(GraphMediaOwnerRecord x) => Record((1, Id(x.OwnerId.TryWriteBytes)),
        (2, Bytes(Hash256.Compute(OwnerKey(x.Key)))), (3, Bytes(Hash256.Compute(Media(x.Media)))),
        (4, U64(x.Version)), (5, [(byte)x.State]));
    private static byte[] Borrow(GraphMediaBorrowRecord x) => Record((1, Id(x.OwnerId.TryWriteBytes)),
        (2, Id(x.TokenId.TryWriteBytes)), (3, Bytes(x.AcquireHash)),
        (4, x.ReturnHash is { } h ? [1, .. Bytes(h)] : [0]), (5, [(byte)x.State]));
    private static byte[] Receipt(GraphMediaOwnerReceipt x) => Record((1, Id(x.OperationId.TryWriteBytes)),
        (2, Bytes(x.RequestHash)), (3, [(byte)(x.Result == GraphMediaOwnerTransitionResult.Transferred ? 0 : 1)]),
        (4, Id(x.SourceOwnerId.TryWriteBytes)), (5, Optional(x.DestinationOwnerId)), (6, U64(x.SourcePostVersion)));
    private static byte[] Tombstone(GraphMediaCompactionTombstone x) => Record((1, Id(x.OperationId.TryWriteBytes)),
        (2, Bytes(x.RequestHash)), (3, U16(x.RemovedBorrowCount)), (4, U16(x.RemovedReceiptCount)));
    private static byte[] OwnerKey(GraphMediaOwnerKey x) => Record(
        (1, Id(x.Session.LiveSessionId.TryWriteBytes)), (2, Id(x.GraphGeneration.TryWriteBytes)),
        (3, Id(x.Session.RuntimeGenerationId.TryWriteBytes)), (4, Id(x.MediaId.TryWriteBytes)));
    private static byte[] Media(GraphMediaBinding x) => Record((1, I64(x.Start)), (2, I64(x.EndExclusive)),
        (3, Id(x.FormatId.TryWriteBytes)), (4, U32(x.FormatRevision)), (5, U32(x.SampleRateHz)),
        (6, U16(x.ChannelCount)), (7, U16(x.BytesPerSample)), (8, Id(x.ClockId.TryWriteBytes)),
        (9, U32(x.ClockRevision)), (10, U64(x.Sequence)), (11, [(byte)x.Discontinuity]),
        (12, I64(x.ByteLength)), (13, I64(x.FrameCount)));
    private static byte[] Record(params (byte Tag, byte[] Value)[] fields)
    { var w = new ArrayBufferWriter<byte>(); foreach (var x in fields) Field(w, x.Tag, x.Value); return w.WrittenSpan.ToArray(); }
    private static void Section(ArrayBufferWriter<byte> w, byte tag, IEnumerable<byte[]> records)
    { var all = records.ToArray(); Raw(w, [tag]); Raw(w, U16(checked((ushort)all.Length))); foreach (var r in all) { Raw(w, U32((uint)r.Length)); Raw(w, r); } }
    private static void Field(ArrayBufferWriter<byte> w, byte tag, byte[] value)
    { Raw(w, [tag]); Raw(w, U32((uint)value.Length)); Raw(w, value); }
    private static void Raw(ArrayBufferWriter<byte> w, ReadOnlySpan<byte> bytes) { var s = w.GetSpan(bytes.Length); bytes.CopyTo(s); w.Advance(bytes.Length); }
    private static byte[] Id(IdWriter write) { var b = new byte[16]; if (!write(b)) throw new ArgumentException("Invalid ID."); return b; }
    private static byte[] Bytes(Hash256 h) { var b = new byte[32]; if (!h.TryWriteBytes(b)) throw new ArgumentException("Invalid hash."); return b; }
    private static byte[] Optional(StableId128? x) => x is { } id ? [1, .. Id(id.TryWriteBytes)] : [0];
    private static byte[] IdList(IEnumerable<byte[]> source)
    { var items = source.ToArray(); var w = new ArrayBufferWriter<byte>(); Raw(w, U16(checked((ushort)items.Length))); foreach (var x in items) Raw(w, x); return w.WrittenSpan.ToArray(); }
    private static byte[] U16(ushort x) { var b = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, x); return b; }
    private static byte[] U32(uint x) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, x); return b; }
    private static byte[] U64(ulong x) { var b = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, x); return b; }
    private static byte[] I64(long x) { var b = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(b, x); return b; }
    private static string Hex(StableId128 x) => Convert.ToHexString(Id(x.TryWriteBytes));
    private static string Hex(OperationId x) => Convert.ToHexString(Id(x.TryWriteBytes));
}

internal sealed class GraphMediaOwnershipLedger
{
    internal const int MaximumOwners = 96, MaximumActiveBorrows = 64, MaximumBorrowRowsPerOwner = 256, MaximumReceipts = 256;
    private readonly Dictionary<StableId128, GraphMediaOwnerRecord> _owners;
    private readonly Dictionary<(StableId128 Owner, StableId128 Token), GraphMediaBorrowRecord> _borrows;
    private readonly Dictionary<OperationId, GraphMediaOwnerReceipt> _receipts;
    private readonly Dictionary<OperationId, GraphMediaCompactionTombstone> _tombstones;
    private GraphMediaOwnershipLedger(SessionAuthorityStampV1 session, GraphGenerationId graph,
        Dictionary<StableId128, GraphMediaOwnerRecord> owners,
        Dictionary<(StableId128, StableId128), GraphMediaBorrowRecord> borrows,
        Dictionary<OperationId, GraphMediaOwnerReceipt> receipts,
        Dictionary<OperationId, GraphMediaCompactionTombstone> tombstones)
    { Session = session; GraphGeneration = graph; _owners = owners; _borrows = borrows; _receipts = receipts; _tombstones = tombstones; }
    internal SessionAuthorityStampV1 Session { get; } internal GraphGenerationId GraphGeneration { get; }
    internal IReadOnlyDictionary<StableId128, GraphMediaOwnerRecord> Owners => new ReadOnlyDictionary<StableId128, GraphMediaOwnerRecord>(_owners);
    internal IReadOnlyCollection<GraphMediaBorrowRecord> Borrows => Array.AsReadOnly(_borrows.Values.ToArray());
    internal IReadOnlyCollection<GraphMediaOwnerReceipt> Receipts => Array.AsReadOnly(_receipts.Values.ToArray());
    internal Hash256 Fingerprint => GraphMediaOwnershipCodec.Fingerprint(_owners.Values, _borrows.Values, _receipts.Values, _tombstones.Values);
    internal static GraphMediaOwnershipLedger Create(GraphMediaOwnerKey key, StableId128 ownerId, GraphMediaBinding media)
    {
        if (!key.IsValid || ownerId.Equals(default) || media is null) throw new ArgumentException("Valid initial owner required.");
        return new(key.Session, key.GraphGeneration,
            new() { [ownerId] = new(ownerId, key, media, GraphMediaOwnerState.Owned, 1) }, [], [], []);
    }
    internal GraphMediaOwnerTransition Transition(SessionAuthorityStampV1 session, GraphGenerationId graph,
        OperationId operation, GraphMediaOwnerAction action, StableId128 sourceId, StableId128? destinationId,
        ulong expectedVersion, Hash256 requestHash)
    {
        if (!operation.IsValid || sourceId.Equals(default) || requestHash == default || !Enum.IsDefined(action) ||
            (action == GraphMediaOwnerAction.Transfer ? destinationId is null || destinationId.Value.Equals(default) || destinationId.Value.Equals(sourceId) : destinationId is not null)) return Fail(GraphMediaOwnerTransitionResult.InvalidRequest);
        if (session != Session || graph != GraphGeneration) return Fail(GraphMediaOwnerTransitionResult.StaleGeneration);
        if (_receipts.TryGetValue(operation, out var retry)) return Fail(retry.RequestHash == requestHash
            ? GraphMediaOwnerTransitionResult.IdempotentAccepted : GraphMediaOwnerTransitionResult.ContradictoryDuplicate);
        if (!_owners.TryGetValue(sourceId, out var source)) return Fail(GraphMediaOwnerTransitionResult.SourceNotFound);
        if (source.Key.Session != session || source.Key.GraphGeneration != graph) return Fail(GraphMediaOwnerTransitionResult.StaleGeneration);
        if (GraphMediaOwnershipCodec.OwnerTransition(operation, action, sourceId, destinationId, source.Key,
            source.Media, expectedVersion, out _) != requestHash) return Fail(GraphMediaOwnerTransitionResult.InvalidRequest);
        if (source.State == GraphMediaOwnerState.Transferred) return Fail(GraphMediaOwnerTransitionResult.NotOwner);
        if (source.State == GraphMediaOwnerState.Disposed) return Fail(GraphMediaOwnerTransitionResult.AlreadyDisposed);
        if (source.Version != expectedVersion || source.Version == ulong.MaxValue) return Fail(GraphMediaOwnerTransitionResult.VersionConflict);
        if (_borrows.Values.Any(x => x.OwnerId.Equals(sourceId) && x.State == GraphMediaBorrowState.Active)) return Fail(GraphMediaOwnerTransitionResult.BorrowOutstanding);
        if (destinationId is { } destination && _owners.ContainsKey(destination)) return Fail(GraphMediaOwnerTransitionResult.DestinationCollision);
        if (destinationId is not null && _owners.Count >= MaximumOwners) return Fail(GraphMediaOwnerTransitionResult.OwnerLimitReached);
        if (_receipts.Count >= MaximumReceipts) return Fail(GraphMediaOwnerTransitionResult.OperationReceiptLimitReached);
        var owners = new Dictionary<StableId128, GraphMediaOwnerRecord>(_owners);
        var result = action == GraphMediaOwnerAction.Transfer ? GraphMediaOwnerTransitionResult.Transferred : GraphMediaOwnerTransitionResult.Disposed;
        owners[sourceId] = source with { State = action == GraphMediaOwnerAction.Transfer ? GraphMediaOwnerState.Transferred : GraphMediaOwnerState.Disposed, Version = source.Version + 1 };
        if (destinationId is { } id) owners[id] = new(id, source.Key, source.Media, GraphMediaOwnerState.Owned, 1);
        var receipts = new Dictionary<OperationId, GraphMediaOwnerReceipt>(_receipts)
        { [operation] = new(operation, requestHash, result, sourceId, destinationId, source.Version + 1) };
        return new(result, Next(owners, new(_borrows), receipts, new(_tombstones)));
    }
    internal GraphMediaBorrowTransition Acquire(SessionAuthorityStampV1 session, GraphGenerationId graph,
        StableId128 ownerId, StableId128 tokenId, Hash256 acquireHash)
    {
        if (ownerId.Equals(default) || tokenId.Equals(default) || acquireHash == default) return BorrowFail(GraphMediaBorrowResult.InvalidRequest);
        if (session != Session || graph != GraphGeneration) return BorrowFail(GraphMediaBorrowResult.StaleGeneration);
        if (!_owners.TryGetValue(ownerId, out var owner)) return BorrowFail(GraphMediaBorrowResult.SourceNotFound);
        if (owner.Key.Session != session || owner.Key.GraphGeneration != graph) return BorrowFail(GraphMediaBorrowResult.StaleGeneration);
        if (_borrows.TryGetValue((ownerId, tokenId), out var prior)) return BorrowFail(prior.AcquireHash == acquireHash
            ? GraphMediaBorrowResult.IdempotentBorrowed : GraphMediaBorrowResult.ContradictoryDuplicate);
        if (owner.State == GraphMediaOwnerState.Transferred) return BorrowFail(GraphMediaBorrowResult.NotOwner);
        if (owner.State == GraphMediaOwnerState.Disposed) return BorrowFail(GraphMediaBorrowResult.AlreadyDisposed);
        var rows = _borrows.Values.Where(x => x.OwnerId.Equals(ownerId)).ToArray();
        if (rows.Length >= MaximumBorrowRowsPerOwner) return BorrowFail(GraphMediaBorrowResult.BorrowRowLimitReached);
        if (rows.Count(x => x.State == GraphMediaBorrowState.Active) >= MaximumActiveBorrows) return BorrowFail(GraphMediaBorrowResult.BorrowLimitReached);
        var borrows = new Dictionary<(StableId128, StableId128), GraphMediaBorrowRecord>(_borrows)
        { [(ownerId, tokenId)] = new(ownerId, tokenId, acquireHash, null, GraphMediaBorrowState.Active) };
        return new(GraphMediaBorrowResult.Borrowed, Next(new(_owners), borrows, new(_receipts), new(_tombstones)));
    }
    internal GraphMediaBorrowTransition Return(SessionAuthorityStampV1 session, GraphGenerationId graph,
        StableId128 ownerId, StableId128 tokenId, Hash256 returnHash)
    {
        if (ownerId.Equals(default) || tokenId.Equals(default) || returnHash == default) return BorrowFail(GraphMediaBorrowResult.InvalidRequest);
        if (session != Session || graph != GraphGeneration) return BorrowFail(GraphMediaBorrowResult.StaleGeneration);
        if (!_owners.TryGetValue(ownerId, out var owner)) return BorrowFail(GraphMediaBorrowResult.SourceNotFound);
        if (owner.Key.Session != session || owner.Key.GraphGeneration != graph) return BorrowFail(GraphMediaBorrowResult.StaleGeneration);
        if (!_borrows.TryGetValue((ownerId, tokenId), out var row)) return BorrowFail(GraphMediaBorrowResult.BorrowNotFound);
        if (row.State == GraphMediaBorrowState.Returned) return BorrowFail(row.ReturnHash == returnHash
            ? GraphMediaBorrowResult.IdempotentReturned : GraphMediaBorrowResult.ContradictoryDuplicate);
        var borrows = new Dictionary<(StableId128, StableId128), GraphMediaBorrowRecord>(_borrows)
        { [(ownerId, tokenId)] = row with { ReturnHash = returnHash, State = GraphMediaBorrowState.Returned } };
        return new(GraphMediaBorrowResult.Returned, Next(new(_owners), borrows, new(_receipts), new(_tombstones)));
    }
    internal GraphMediaCompactionTransition Compact(SessionAuthorityStampV1 session, GraphGenerationId graph,
        OperationId operation, Hash256 expectedFingerprint, IReadOnlyList<StableId128>? returnedTokens,
        IReadOnlyList<OperationId>? receiptOperations, Hash256 requestHash)
    {
        if (!session.IsValid || !graph.IsValid || !operation.IsValid || expectedFingerprint == default || requestHash == default ||
            returnedTokens is null || receiptOperations is null || returnedTokens.Count > 24_576 ||
            receiptOperations.Count > MaximumReceipts || !Strict(returnedTokens, Write) || !Strict(receiptOperations, Write) ||
            GraphMediaOwnershipCodec.Compaction(operation, session, graph, expectedFingerprint,
                returnedTokens, receiptOperations) != requestHash) return CompactFail(GraphMediaCompactionResult.InvalidRequest);
        if (session != Session || graph != GraphGeneration) return CompactFail(GraphMediaCompactionResult.StaleGeneration);
        if (_tombstones.TryGetValue(operation, out var retry)) return retry.RequestHash == requestHash
            ? new(GraphMediaCompactionResult.IdempotentCompacted, this, retry.RemovedBorrowCount,
                retry.RemovedReceiptCount, retry.PostFingerprint)
            : CompactFail(GraphMediaCompactionResult.ContradictoryDuplicate);
        if (_tombstones.Count != 0 || _owners.Values.Any(x => x.State == GraphMediaOwnerState.Owned) ||
            _borrows.Values.Any(x => x.State == GraphMediaBorrowState.Active)) return CompactFail(GraphMediaCompactionResult.NotEligible);
        if (Fingerprint != expectedFingerprint) return CompactFail(GraphMediaCompactionResult.FingerprintConflict);
        var eligibleBorrows = _borrows.Values.OrderBy(x => Bytes(x.TokenId), ByteArrayComparer.Instance).Select(x => x.TokenId).ToArray();
        var eligibleReceipts = _receipts.Values.OrderBy(x => Bytes(x.OperationId), ByteArrayComparer.Instance).Select(x => x.OperationId).ToArray();
        if (!returnedTokens.SequenceEqual(eligibleBorrows) || !receiptOperations.SequenceEqual(eligibleReceipts))
            return CompactFail(GraphMediaCompactionResult.NotEligible);
        var removedBorrows = checked((ushort)_borrows.Count); var removedReceipts = checked((ushort)_receipts.Count);
        var placeholder = new GraphMediaCompactionTombstone(operation, requestHash, removedBorrows, removedReceipts, default);
        var tombstones = new Dictionary<OperationId, GraphMediaCompactionTombstone> { [operation] = placeholder };
        var post = GraphMediaOwnershipCodec.Fingerprint(_owners.Values, [], [], tombstones.Values);
        tombstones[operation] = placeholder with { PostFingerprint = post };
        return new(GraphMediaCompactionResult.Compacted, Next(new(_owners), [], [], tombstones), removedBorrows, removedReceipts, post);
    }
    internal GraphMediaOwnershipBatchCopyTransition CopyOwners(SessionAuthorityStampV1 session,
        GraphGenerationId graph, StableId128 sourceId, IReadOnlyList<StableId128> destinationIds)
    {
        if (!session.IsValid || !graph.IsValid || sourceId.Equals(default) || destinationIds is null ||
            destinationIds.Count is < 1 or > 16 || !Strict(destinationIds, Write))
            return CopyFail(GraphMediaOwnershipBatchCopyResult.InvalidRequest);
        if (session != Session || graph != GraphGeneration)
            return CopyFail(GraphMediaOwnershipBatchCopyResult.StaleGeneration);
        if (!_owners.TryGetValue(sourceId, out var source))
            return CopyFail(GraphMediaOwnershipBatchCopyResult.SourceNotFound);
        if (source.Key.Session != session || source.Key.GraphGeneration != graph)
            return CopyFail(GraphMediaOwnershipBatchCopyResult.StaleGeneration);
        if (source.State == GraphMediaOwnerState.Transferred)
            return CopyFail(GraphMediaOwnershipBatchCopyResult.NotOwner);
        if (source.State == GraphMediaOwnerState.Disposed)
            return CopyFail(GraphMediaOwnershipBatchCopyResult.AlreadyDisposed);
        if (_borrows.Values.Any(x => x.OwnerId.Equals(sourceId) && x.State == GraphMediaBorrowState.Active))
            return CopyFail(GraphMediaOwnershipBatchCopyResult.BorrowOutstanding);
        if (destinationIds.Any(_owners.ContainsKey))
            return CopyFail(GraphMediaOwnershipBatchCopyResult.DestinationCollision);
        if (_owners.Count > MaximumOwners - destinationIds.Count)
            return CopyFail(GraphMediaOwnershipBatchCopyResult.OwnerLimitReached);
        var owners = new Dictionary<StableId128, GraphMediaOwnerRecord>(_owners);
        foreach (var destinationId in destinationIds)
            owners.Add(destinationId, new(destinationId, source.Key, source.Media, GraphMediaOwnerState.Owned, 1));
        return new(GraphMediaOwnershipBatchCopyResult.Copied,
            Next(owners, new(_borrows), new(_receipts), new(_tombstones)));
    }

    internal GraphMediaOwnershipBatchCopyTransition DeriveOwner(
        SessionAuthorityStampV1 session,
        GraphGenerationId graph,
        StableId128 sourceId,
        ulong expectedSourceVersion,
        StableId128 destinationId,
        GraphMediaOwnerKey destinationKey,
        GraphMediaBinding destinationMedia)
    {
        if (!session.IsValid || !graph.IsValid || sourceId.Equals(default) || destinationId.Equals(default) ||
            sourceId.Equals(destinationId) || !destinationKey.IsValid || destinationMedia is null)
            return CopyFail(GraphMediaOwnershipBatchCopyResult.InvalidRequest);
        if (session != Session || graph != GraphGeneration || destinationKey.Session != session ||
            destinationKey.GraphGeneration != graph)
            return CopyFail(GraphMediaOwnershipBatchCopyResult.StaleGeneration);
        if (!_owners.TryGetValue(sourceId, out var source))
            return CopyFail(GraphMediaOwnershipBatchCopyResult.SourceNotFound);
        if (source.State == GraphMediaOwnerState.Transferred)
            return CopyFail(GraphMediaOwnershipBatchCopyResult.NotOwner);
        if (source.State == GraphMediaOwnerState.Disposed)
            return CopyFail(GraphMediaOwnershipBatchCopyResult.AlreadyDisposed);
        if (source.Version != expectedSourceVersion)
            return CopyFail(GraphMediaOwnershipBatchCopyResult.NotOwner);
        if (_borrows.Values.Any(x => x.OwnerId.Equals(sourceId) && x.State == GraphMediaBorrowState.Active))
            return CopyFail(GraphMediaOwnershipBatchCopyResult.BorrowOutstanding);
        if (_owners.ContainsKey(destinationId))
            return CopyFail(GraphMediaOwnershipBatchCopyResult.DestinationCollision);
        if (_owners.Count >= MaximumOwners)
            return CopyFail(GraphMediaOwnershipBatchCopyResult.OwnerLimitReached);
        var owners = new Dictionary<StableId128, GraphMediaOwnerRecord>(_owners)
        {
            [destinationId] = new(destinationId, destinationKey, destinationMedia, GraphMediaOwnerState.Owned, 1),
        };
        return new(GraphMediaOwnershipBatchCopyResult.Copied,
            Next(owners, new(_borrows), new(_receipts), new(_tombstones)));
    }
    private static bool Strict<T>(IReadOnlyList<T> values, Func<T, byte[]> bytes)
    { for (var i = 0; i < values.Count; i++) { var current = bytes(values[i]); if (current.Length != 16 || (i > 0 && ByteArrayComparer.Instance.Compare(bytes(values[i - 1]), current) >= 0)) return false; } return true; }
    private static byte[] Write(StableId128 x) { var b = new byte[16]; return x.TryWriteBytes(b) ? b : []; }
    private static byte[] Write(OperationId x) { var b = new byte[16]; return x.TryWriteBytes(b) ? b : []; }
    private static byte[] Bytes(StableId128 x) => Write(x);
    private static byte[] Bytes(OperationId x) => Write(x);
    private sealed class ByteArrayComparer : IComparer<byte[]>
    { internal static readonly ByteArrayComparer Instance = new(); public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y); }
    private GraphMediaOwnerTransition Fail(GraphMediaOwnerTransitionResult x) => new(x, this);
    private GraphMediaBorrowTransition BorrowFail(GraphMediaBorrowResult x) => new(x, this);
    private GraphMediaCompactionTransition CompactFail(GraphMediaCompactionResult x) => new(x, this, 0, 0, default);
    private GraphMediaOwnershipBatchCopyTransition CopyFail(GraphMediaOwnershipBatchCopyResult x) => new(x, this);
    private GraphMediaOwnershipLedger Next(Dictionary<StableId128, GraphMediaOwnerRecord> owners,
        Dictionary<(StableId128, StableId128), GraphMediaBorrowRecord> borrows,
        Dictionary<OperationId, GraphMediaOwnerReceipt> receipts,
        Dictionary<OperationId, GraphMediaCompactionTombstone> tombstones) => new(Session, GraphGeneration, owners, borrows, receipts, tombstones);
}
