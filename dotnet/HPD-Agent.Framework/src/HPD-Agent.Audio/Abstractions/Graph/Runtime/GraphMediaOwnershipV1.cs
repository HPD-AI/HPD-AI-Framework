using System.Buffers;
using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal enum GraphMediaDiscontinuityKindV1 : byte { Continuous, GapBefore, ResetBefore }
internal enum GraphMediaOwnerStateV1 : byte { Owned, Transferred, Disposed }
internal enum GraphMediaBorrowStateV1 : byte { Active, Returned }
internal enum GraphMediaOwnerActionV1 : byte { Transfer = 1, Dispose = 2 }
internal enum GraphMediaOwnerTransitionResultV1 : byte
{
    Transferred, Disposed, IdempotentAccepted, InvalidRequest, StaleGeneration,
    ContradictoryDuplicate, SourceNotFound, NotOwner, AlreadyDisposed, VersionConflict,
    BorrowOutstanding, DestinationCollision, OwnerLimitReached, OperationReceiptLimitReached
}
internal enum GraphMediaBorrowResultV1 : byte
{
    Borrowed, IdempotentBorrowed, Returned, IdempotentReturned, InvalidRequest,
    StaleGeneration, ContradictoryDuplicate, SourceNotFound, NotOwner, AlreadyDisposed,
    BorrowNotFound, BorrowLimitReached, BorrowRowLimitReached
}
internal enum GraphMediaCompactionResultV1 : byte
{ Compacted, IdempotentCompacted, InvalidRequest, StaleGeneration, NotEligible, FingerprintConflict, ContradictoryDuplicate }
internal enum GraphMediaOwnershipBatchCopyResultV1 : byte
{ Copied, InvalidRequest, StaleGeneration, SourceNotFound, NotOwner, AlreadyDisposed, BorrowOutstanding, DestinationCollision, OwnerLimitReached }

internal readonly record struct GraphMediaOwnerKeyV1(
    SessionAuthorityStampV1 Session, GraphGenerationId GraphGeneration, StableId128 MediaId)
{ internal bool IsValid => Session.IsValid && GraphGeneration.IsValid && !MediaId.Equals(default); }

internal sealed record GraphMediaBindingV1
{
    internal const long MaximumDurationNanoseconds = 10_000_000_000;
    internal const long MaximumByteLength = 16_777_216;
    internal const long MaximumSampleAmount = 480_000;
    private GraphMediaBindingV1(long start, long end, StableId128 format, uint formatRevision, uint rate,
        ushort channels, ushort bytesPerSample, StableId128 clock, uint clockRevision, ulong sequence,
        GraphMediaDiscontinuityKindV1 discontinuity, long bytes, long frames)
    { Start = start; EndExclusive = end; FormatId = format; FormatRevision = formatRevision; SampleRateHz = rate;
      ChannelCount = channels; BytesPerSample = bytesPerSample; ClockId = clock; ClockRevision = clockRevision;
      Sequence = sequence; Discontinuity = discontinuity; ByteLength = bytes; FrameCount = frames; }
    internal long Start { get; } internal long EndExclusive { get; } internal long Duration => EndExclusive - Start;
    internal StableId128 FormatId { get; } internal uint FormatRevision { get; } internal uint SampleRateHz { get; }
    internal ushort ChannelCount { get; } internal ushort BytesPerSample { get; } internal StableId128 ClockId { get; }
    internal uint ClockRevision { get; } internal ulong Sequence { get; }
    internal GraphMediaDiscontinuityKindV1 Discontinuity { get; } internal long ByteLength { get; }
    internal long FrameCount { get; } internal long SampleAmount => checked(FrameCount * ChannelCount);

    internal static bool TryCreate(long start, long end, StableId128 format, uint formatRevision, uint rate,
        ushort channels, ushort bytesPerSample, StableId128 clock, uint clockRevision, ulong sequence,
        GraphMediaDiscontinuityKindV1 discontinuity, long bytes, long frames, GraphMediaBindingV1? prior,
        out GraphMediaBindingV1? value)
    {
        value = null;
        if (start < 0 || end <= start || end - start > MaximumDurationNanoseconds || format.Equals(default) ||
            formatRevision == 0 || rate is 0 or > 768_000 || channels is 0 or > 256 || bytesPerSample is 0 or > 32 ||
            clock.Equals(default) || clockRevision == 0 || discontinuity > GraphMediaDiscontinuityKindV1.ResetBefore ||
            bytes is <= 0 or > MaximumByteLength || frames <= 0 || frames > MaximumSampleAmount / channels ||
            !Continuity(start, format, formatRevision, rate, channels, bytesPerSample, clock, clockRevision,
                sequence, discontinuity, prior)) return false;
        value = new(start, end, format, formatRevision, rate, channels, bytesPerSample, clock, clockRevision,
            sequence, discontinuity, bytes, frames); return true;
    }
    private static bool Continuity(long start, StableId128 format, uint revision, uint rate, ushort channels,
        ushort sampleBytes, StableId128 clock, uint clockRevision, ulong sequence,
        GraphMediaDiscontinuityKindV1 kind, GraphMediaBindingV1? prior)
    {
        if (kind == GraphMediaDiscontinuityKindV1.ResetBefore) return true;
        if (prior is null || prior.Sequence == ulong.MaxValue || sequence != prior.Sequence + 1 ||
            !clock.Equals(prior.ClockId) || clockRevision != prior.ClockRevision) return false;
        if (kind == GraphMediaDiscontinuityKindV1.GapBefore) return start >= prior.EndExclusive;
        return start == prior.EndExclusive && format.Equals(prior.FormatId) && revision == prior.FormatRevision &&
            rate == prior.SampleRateHz && channels == prior.ChannelCount && sampleBytes == prior.BytesPerSample;
    }
}

internal sealed record GraphMediaOwnerRecordV1(StableId128 OwnerId, GraphMediaOwnerKeyV1 Key,
    GraphMediaBindingV1 Media, GraphMediaOwnerStateV1 State, ulong Version);
internal sealed record GraphMediaBorrowRecordV1(StableId128 OwnerId, StableId128 TokenId,
    Hash256 AcquireHash, Hash256? ReturnHash, GraphMediaBorrowStateV1 State);
internal sealed record GraphMediaOwnerReceiptV1(OperationId OperationId, Hash256 RequestHash,
    GraphMediaOwnerTransitionResultV1 Result, StableId128 SourceOwnerId, StableId128? DestinationOwnerId, ulong SourcePostVersion);
internal sealed record GraphMediaCompactionTombstoneV1(OperationId OperationId, Hash256 RequestHash,
    ushort RemovedBorrowCount, ushort RemovedReceiptCount, Hash256 PostFingerprint);
internal sealed record GraphMediaOwnerTransitionV1(GraphMediaOwnerTransitionResultV1 Result, GraphMediaOwnershipLedgerV1 Ledger);
internal sealed record GraphMediaBorrowTransitionV1(GraphMediaBorrowResultV1 Result, GraphMediaOwnershipLedgerV1 Ledger);
internal sealed record GraphMediaCompactionTransitionV1(GraphMediaCompactionResultV1 Result,
    GraphMediaOwnershipLedgerV1 Ledger, ushort RemovedBorrowCount, ushort RemovedReceiptCount, Hash256 PostFingerprint);
internal sealed record GraphMediaOwnershipBatchCopyTransitionV1(GraphMediaOwnershipBatchCopyResultV1 Result,
    GraphMediaOwnershipLedgerV1 Ledger);

internal static class GraphMediaOwnershipCodecV1
{
    private delegate bool IdWriter(Span<byte> destination);
    private static readonly byte[] OwnerDomain = Encoding.UTF8.GetBytes("hpd-s2-graph-media-owner-transition-v1\0");
    private static readonly byte[] FingerprintDomain = Encoding.UTF8.GetBytes("hpd-s2-graph-media-owner-ledger-fingerprint-v1\0");
    private static readonly byte[] CompactionDomain = Encoding.UTF8.GetBytes("hpd-s2-graph-media-owner-compaction-v1\0");
    internal static Hash256 OwnerTransition(OperationId operation, GraphMediaOwnerActionV1 action,
        StableId128 sourceOwnerId, StableId128? destinationOwnerId, GraphMediaOwnerKeyV1 key,
        GraphMediaBindingV1 media, ulong expectedVersion, out byte[] preimage)
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
    internal static Hash256 Fingerprint(IEnumerable<GraphMediaOwnerRecordV1> owners,
        IEnumerable<GraphMediaBorrowRecordV1> borrows, IEnumerable<GraphMediaOwnerReceiptV1> receipts,
        IEnumerable<GraphMediaCompactionTombstoneV1> tombstones)
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
    internal static byte[] GoldenOwner(StableId128 id, Hash256 key, Hash256 media, ulong version, GraphMediaOwnerStateV1 state) =>
        Record((1, Id(id.TryWriteBytes)), (2, Bytes(key)), (3, Bytes(media)), (4, U64(version)), (5, [(byte)state]));
    internal static byte[] GoldenBorrow(StableId128 owner, StableId128 token, Hash256 acquire, Hash256? returned,
        GraphMediaBorrowStateV1 state) => Record((1, Id(owner.TryWriteBytes)), (2, Id(token.TryWriteBytes)),
            (3, Bytes(acquire)), (4, returned is { } h ? [1, .. Bytes(h)] : [0]), (5, [(byte)state]));
    internal static byte[] GoldenReceipt(OperationId operation, Hash256 request, GraphMediaOwnerTransitionResultV1 result,
        StableId128 source, StableId128? destination, ulong version) => Receipt(new(operation, request, result, source, destination, version));
    internal static byte[] GoldenTombstone(OperationId operation, Hash256 request, ushort borrows, ushort receipts) =>
        Tombstone(new(operation, request, borrows, receipts, default));
    private static byte[] Owner(GraphMediaOwnerRecordV1 x) => Record((1, Id(x.OwnerId.TryWriteBytes)),
        (2, Bytes(Hash256.Compute(OwnerKey(x.Key)))), (3, Bytes(Hash256.Compute(Media(x.Media)))),
        (4, U64(x.Version)), (5, [(byte)x.State]));
    private static byte[] Borrow(GraphMediaBorrowRecordV1 x) => Record((1, Id(x.OwnerId.TryWriteBytes)),
        (2, Id(x.TokenId.TryWriteBytes)), (3, Bytes(x.AcquireHash)),
        (4, x.ReturnHash is { } h ? [1, .. Bytes(h)] : [0]), (5, [(byte)x.State]));
    private static byte[] Receipt(GraphMediaOwnerReceiptV1 x) => Record((1, Id(x.OperationId.TryWriteBytes)),
        (2, Bytes(x.RequestHash)), (3, [(byte)(x.Result == GraphMediaOwnerTransitionResultV1.Transferred ? 0 : 1)]),
        (4, Id(x.SourceOwnerId.TryWriteBytes)), (5, Optional(x.DestinationOwnerId)), (6, U64(x.SourcePostVersion)));
    private static byte[] Tombstone(GraphMediaCompactionTombstoneV1 x) => Record((1, Id(x.OperationId.TryWriteBytes)),
        (2, Bytes(x.RequestHash)), (3, U16(x.RemovedBorrowCount)), (4, U16(x.RemovedReceiptCount)));
    private static byte[] OwnerKey(GraphMediaOwnerKeyV1 x) => Record(
        (1, Id(x.Session.LiveSessionId.TryWriteBytes)), (2, Id(x.GraphGeneration.TryWriteBytes)),
        (3, Id(x.Session.RuntimeGenerationId.TryWriteBytes)), (4, Id(x.MediaId.TryWriteBytes)));
    private static byte[] Media(GraphMediaBindingV1 x) => Record((1, I64(x.Start)), (2, I64(x.EndExclusive)),
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

internal sealed class GraphMediaOwnershipLedgerV1
{
    internal const int MaximumOwners = 96, MaximumActiveBorrows = 64, MaximumBorrowRowsPerOwner = 256, MaximumReceipts = 256;
    private readonly Dictionary<StableId128, GraphMediaOwnerRecordV1> _owners;
    private readonly Dictionary<(StableId128 Owner, StableId128 Token), GraphMediaBorrowRecordV1> _borrows;
    private readonly Dictionary<OperationId, GraphMediaOwnerReceiptV1> _receipts;
    private readonly Dictionary<OperationId, GraphMediaCompactionTombstoneV1> _tombstones;
    private GraphMediaOwnershipLedgerV1(SessionAuthorityStampV1 session, GraphGenerationId graph,
        Dictionary<StableId128, GraphMediaOwnerRecordV1> owners,
        Dictionary<(StableId128, StableId128), GraphMediaBorrowRecordV1> borrows,
        Dictionary<OperationId, GraphMediaOwnerReceiptV1> receipts,
        Dictionary<OperationId, GraphMediaCompactionTombstoneV1> tombstones)
    { Session = session; GraphGeneration = graph; _owners = owners; _borrows = borrows; _receipts = receipts; _tombstones = tombstones; }
    internal SessionAuthorityStampV1 Session { get; } internal GraphGenerationId GraphGeneration { get; }
    internal IReadOnlyDictionary<StableId128, GraphMediaOwnerRecordV1> Owners => new ReadOnlyDictionary<StableId128, GraphMediaOwnerRecordV1>(_owners);
    internal IReadOnlyCollection<GraphMediaBorrowRecordV1> Borrows => Array.AsReadOnly(_borrows.Values.ToArray());
    internal IReadOnlyCollection<GraphMediaOwnerReceiptV1> Receipts => Array.AsReadOnly(_receipts.Values.ToArray());
    internal Hash256 Fingerprint => GraphMediaOwnershipCodecV1.Fingerprint(_owners.Values, _borrows.Values, _receipts.Values, _tombstones.Values);
    internal static GraphMediaOwnershipLedgerV1 Create(GraphMediaOwnerKeyV1 key, StableId128 ownerId, GraphMediaBindingV1 media)
    {
        if (!key.IsValid || ownerId.Equals(default) || media is null) throw new ArgumentException("Valid initial owner required.");
        return new(key.Session, key.GraphGeneration,
            new() { [ownerId] = new(ownerId, key, media, GraphMediaOwnerStateV1.Owned, 1) }, [], [], []);
    }
    internal GraphMediaOwnerTransitionV1 Transition(SessionAuthorityStampV1 session, GraphGenerationId graph,
        OperationId operation, GraphMediaOwnerActionV1 action, StableId128 sourceId, StableId128? destinationId,
        ulong expectedVersion, Hash256 requestHash)
    {
        if (!operation.IsValid || sourceId.Equals(default) || requestHash == default || !Enum.IsDefined(action) ||
            (action == GraphMediaOwnerActionV1.Transfer ? destinationId is null || destinationId.Value.Equals(default) || destinationId.Value.Equals(sourceId) : destinationId is not null)) return Fail(GraphMediaOwnerTransitionResultV1.InvalidRequest);
        if (session != Session || graph != GraphGeneration) return Fail(GraphMediaOwnerTransitionResultV1.StaleGeneration);
        if (_receipts.TryGetValue(operation, out var retry)) return Fail(retry.RequestHash == requestHash
            ? GraphMediaOwnerTransitionResultV1.IdempotentAccepted : GraphMediaOwnerTransitionResultV1.ContradictoryDuplicate);
        if (!_owners.TryGetValue(sourceId, out var source)) return Fail(GraphMediaOwnerTransitionResultV1.SourceNotFound);
        if (source.Key.Session != session || source.Key.GraphGeneration != graph) return Fail(GraphMediaOwnerTransitionResultV1.StaleGeneration);
        if (GraphMediaOwnershipCodecV1.OwnerTransition(operation, action, sourceId, destinationId, source.Key,
            source.Media, expectedVersion, out _) != requestHash) return Fail(GraphMediaOwnerTransitionResultV1.InvalidRequest);
        if (source.State == GraphMediaOwnerStateV1.Transferred) return Fail(GraphMediaOwnerTransitionResultV1.NotOwner);
        if (source.State == GraphMediaOwnerStateV1.Disposed) return Fail(GraphMediaOwnerTransitionResultV1.AlreadyDisposed);
        if (source.Version != expectedVersion || source.Version == ulong.MaxValue) return Fail(GraphMediaOwnerTransitionResultV1.VersionConflict);
        if (_borrows.Values.Any(x => x.OwnerId.Equals(sourceId) && x.State == GraphMediaBorrowStateV1.Active)) return Fail(GraphMediaOwnerTransitionResultV1.BorrowOutstanding);
        if (destinationId is { } destination && _owners.ContainsKey(destination)) return Fail(GraphMediaOwnerTransitionResultV1.DestinationCollision);
        if (destinationId is not null && _owners.Count >= MaximumOwners) return Fail(GraphMediaOwnerTransitionResultV1.OwnerLimitReached);
        if (_receipts.Count >= MaximumReceipts) return Fail(GraphMediaOwnerTransitionResultV1.OperationReceiptLimitReached);
        var owners = new Dictionary<StableId128, GraphMediaOwnerRecordV1>(_owners);
        var result = action == GraphMediaOwnerActionV1.Transfer ? GraphMediaOwnerTransitionResultV1.Transferred : GraphMediaOwnerTransitionResultV1.Disposed;
        owners[sourceId] = source with { State = action == GraphMediaOwnerActionV1.Transfer ? GraphMediaOwnerStateV1.Transferred : GraphMediaOwnerStateV1.Disposed, Version = source.Version + 1 };
        if (destinationId is { } id) owners[id] = new(id, source.Key, source.Media, GraphMediaOwnerStateV1.Owned, 1);
        var receipts = new Dictionary<OperationId, GraphMediaOwnerReceiptV1>(_receipts)
        { [operation] = new(operation, requestHash, result, sourceId, destinationId, source.Version + 1) };
        return new(result, Next(owners, new(_borrows), receipts, new(_tombstones)));
    }
    internal GraphMediaBorrowTransitionV1 Acquire(SessionAuthorityStampV1 session, GraphGenerationId graph,
        StableId128 ownerId, StableId128 tokenId, Hash256 acquireHash)
    {
        if (ownerId.Equals(default) || tokenId.Equals(default) || acquireHash == default) return BorrowFail(GraphMediaBorrowResultV1.InvalidRequest);
        if (session != Session || graph != GraphGeneration) return BorrowFail(GraphMediaBorrowResultV1.StaleGeneration);
        if (!_owners.TryGetValue(ownerId, out var owner)) return BorrowFail(GraphMediaBorrowResultV1.SourceNotFound);
        if (owner.Key.Session != session || owner.Key.GraphGeneration != graph) return BorrowFail(GraphMediaBorrowResultV1.StaleGeneration);
        if (_borrows.TryGetValue((ownerId, tokenId), out var prior)) return BorrowFail(prior.AcquireHash == acquireHash
            ? GraphMediaBorrowResultV1.IdempotentBorrowed : GraphMediaBorrowResultV1.ContradictoryDuplicate);
        if (owner.State == GraphMediaOwnerStateV1.Transferred) return BorrowFail(GraphMediaBorrowResultV1.NotOwner);
        if (owner.State == GraphMediaOwnerStateV1.Disposed) return BorrowFail(GraphMediaBorrowResultV1.AlreadyDisposed);
        var rows = _borrows.Values.Where(x => x.OwnerId.Equals(ownerId)).ToArray();
        if (rows.Length >= MaximumBorrowRowsPerOwner) return BorrowFail(GraphMediaBorrowResultV1.BorrowRowLimitReached);
        if (rows.Count(x => x.State == GraphMediaBorrowStateV1.Active) >= MaximumActiveBorrows) return BorrowFail(GraphMediaBorrowResultV1.BorrowLimitReached);
        var borrows = new Dictionary<(StableId128, StableId128), GraphMediaBorrowRecordV1>(_borrows)
        { [(ownerId, tokenId)] = new(ownerId, tokenId, acquireHash, null, GraphMediaBorrowStateV1.Active) };
        return new(GraphMediaBorrowResultV1.Borrowed, Next(new(_owners), borrows, new(_receipts), new(_tombstones)));
    }
    internal GraphMediaBorrowTransitionV1 Return(SessionAuthorityStampV1 session, GraphGenerationId graph,
        StableId128 ownerId, StableId128 tokenId, Hash256 returnHash)
    {
        if (ownerId.Equals(default) || tokenId.Equals(default) || returnHash == default) return BorrowFail(GraphMediaBorrowResultV1.InvalidRequest);
        if (session != Session || graph != GraphGeneration) return BorrowFail(GraphMediaBorrowResultV1.StaleGeneration);
        if (!_owners.TryGetValue(ownerId, out var owner)) return BorrowFail(GraphMediaBorrowResultV1.SourceNotFound);
        if (owner.Key.Session != session || owner.Key.GraphGeneration != graph) return BorrowFail(GraphMediaBorrowResultV1.StaleGeneration);
        if (!_borrows.TryGetValue((ownerId, tokenId), out var row)) return BorrowFail(GraphMediaBorrowResultV1.BorrowNotFound);
        if (row.State == GraphMediaBorrowStateV1.Returned) return BorrowFail(row.ReturnHash == returnHash
            ? GraphMediaBorrowResultV1.IdempotentReturned : GraphMediaBorrowResultV1.ContradictoryDuplicate);
        var borrows = new Dictionary<(StableId128, StableId128), GraphMediaBorrowRecordV1>(_borrows)
        { [(ownerId, tokenId)] = row with { ReturnHash = returnHash, State = GraphMediaBorrowStateV1.Returned } };
        return new(GraphMediaBorrowResultV1.Returned, Next(new(_owners), borrows, new(_receipts), new(_tombstones)));
    }
    internal GraphMediaCompactionTransitionV1 Compact(SessionAuthorityStampV1 session, GraphGenerationId graph,
        OperationId operation, Hash256 expectedFingerprint, IReadOnlyList<StableId128>? returnedTokens,
        IReadOnlyList<OperationId>? receiptOperations, Hash256 requestHash)
    {
        if (!session.IsValid || !graph.IsValid || !operation.IsValid || expectedFingerprint == default || requestHash == default ||
            returnedTokens is null || receiptOperations is null || returnedTokens.Count > 24_576 ||
            receiptOperations.Count > MaximumReceipts || !Strict(returnedTokens, Write) || !Strict(receiptOperations, Write) ||
            GraphMediaOwnershipCodecV1.Compaction(operation, session, graph, expectedFingerprint,
                returnedTokens, receiptOperations) != requestHash) return CompactFail(GraphMediaCompactionResultV1.InvalidRequest);
        if (session != Session || graph != GraphGeneration) return CompactFail(GraphMediaCompactionResultV1.StaleGeneration);
        if (_tombstones.TryGetValue(operation, out var retry)) return retry.RequestHash == requestHash
            ? new(GraphMediaCompactionResultV1.IdempotentCompacted, this, retry.RemovedBorrowCount,
                retry.RemovedReceiptCount, retry.PostFingerprint)
            : CompactFail(GraphMediaCompactionResultV1.ContradictoryDuplicate);
        if (_tombstones.Count != 0 || _owners.Values.Any(x => x.State == GraphMediaOwnerStateV1.Owned) ||
            _borrows.Values.Any(x => x.State == GraphMediaBorrowStateV1.Active)) return CompactFail(GraphMediaCompactionResultV1.NotEligible);
        if (Fingerprint != expectedFingerprint) return CompactFail(GraphMediaCompactionResultV1.FingerprintConflict);
        var eligibleBorrows = _borrows.Values.OrderBy(x => Bytes(x.TokenId), ByteArrayComparer.Instance).Select(x => x.TokenId).ToArray();
        var eligibleReceipts = _receipts.Values.OrderBy(x => Bytes(x.OperationId), ByteArrayComparer.Instance).Select(x => x.OperationId).ToArray();
        if (!returnedTokens.SequenceEqual(eligibleBorrows) || !receiptOperations.SequenceEqual(eligibleReceipts))
            return CompactFail(GraphMediaCompactionResultV1.NotEligible);
        var removedBorrows = checked((ushort)_borrows.Count); var removedReceipts = checked((ushort)_receipts.Count);
        var placeholder = new GraphMediaCompactionTombstoneV1(operation, requestHash, removedBorrows, removedReceipts, default);
        var tombstones = new Dictionary<OperationId, GraphMediaCompactionTombstoneV1> { [operation] = placeholder };
        var post = GraphMediaOwnershipCodecV1.Fingerprint(_owners.Values, [], [], tombstones.Values);
        tombstones[operation] = placeholder with { PostFingerprint = post };
        return new(GraphMediaCompactionResultV1.Compacted, Next(new(_owners), [], [], tombstones), removedBorrows, removedReceipts, post);
    }
    internal GraphMediaOwnershipBatchCopyTransitionV1 CopyOwners(SessionAuthorityStampV1 session,
        GraphGenerationId graph, StableId128 sourceId, IReadOnlyList<StableId128> destinationIds)
    {
        if (!session.IsValid || !graph.IsValid || sourceId.Equals(default) || destinationIds is null ||
            destinationIds.Count is < 1 or > 16 || !Strict(destinationIds, Write))
            return CopyFail(GraphMediaOwnershipBatchCopyResultV1.InvalidRequest);
        if (session != Session || graph != GraphGeneration)
            return CopyFail(GraphMediaOwnershipBatchCopyResultV1.StaleGeneration);
        if (!_owners.TryGetValue(sourceId, out var source))
            return CopyFail(GraphMediaOwnershipBatchCopyResultV1.SourceNotFound);
        if (source.Key.Session != session || source.Key.GraphGeneration != graph)
            return CopyFail(GraphMediaOwnershipBatchCopyResultV1.StaleGeneration);
        if (source.State == GraphMediaOwnerStateV1.Transferred)
            return CopyFail(GraphMediaOwnershipBatchCopyResultV1.NotOwner);
        if (source.State == GraphMediaOwnerStateV1.Disposed)
            return CopyFail(GraphMediaOwnershipBatchCopyResultV1.AlreadyDisposed);
        if (_borrows.Values.Any(x => x.OwnerId.Equals(sourceId) && x.State == GraphMediaBorrowStateV1.Active))
            return CopyFail(GraphMediaOwnershipBatchCopyResultV1.BorrowOutstanding);
        if (destinationIds.Any(_owners.ContainsKey))
            return CopyFail(GraphMediaOwnershipBatchCopyResultV1.DestinationCollision);
        if (_owners.Count > MaximumOwners - destinationIds.Count)
            return CopyFail(GraphMediaOwnershipBatchCopyResultV1.OwnerLimitReached);
        var owners = new Dictionary<StableId128, GraphMediaOwnerRecordV1>(_owners);
        foreach (var destinationId in destinationIds)
            owners.Add(destinationId, new(destinationId, source.Key, source.Media, GraphMediaOwnerStateV1.Owned, 1));
        return new(GraphMediaOwnershipBatchCopyResultV1.Copied,
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
    private GraphMediaOwnerTransitionV1 Fail(GraphMediaOwnerTransitionResultV1 x) => new(x, this);
    private GraphMediaBorrowTransitionV1 BorrowFail(GraphMediaBorrowResultV1 x) => new(x, this);
    private GraphMediaCompactionTransitionV1 CompactFail(GraphMediaCompactionResultV1 x) => new(x, this, 0, 0, default);
    private GraphMediaOwnershipBatchCopyTransitionV1 CopyFail(GraphMediaOwnershipBatchCopyResultV1 x) => new(x, this);
    private GraphMediaOwnershipLedgerV1 Next(Dictionary<StableId128, GraphMediaOwnerRecordV1> owners,
        Dictionary<(StableId128, StableId128), GraphMediaBorrowRecordV1> borrows,
        Dictionary<OperationId, GraphMediaOwnerReceiptV1> receipts,
        Dictionary<OperationId, GraphMediaCompactionTombstoneV1> tombstones) => new(Session, GraphGeneration, owners, borrows, receipts, tombstones);
}
