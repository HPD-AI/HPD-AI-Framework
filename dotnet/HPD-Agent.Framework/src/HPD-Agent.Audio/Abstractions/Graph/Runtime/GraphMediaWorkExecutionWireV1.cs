using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal enum GraphMediaWorkExecutionOutcomeV1 : byte { Completed = 1, Unknown = 2, Rejected = 3 }

internal sealed record GraphMediaWorkAuthorityV1
{
    internal GraphMediaWorkAuthorityV1(StableId128 workId, Hash256 requestHash,
        StableId128 residenceId, OperationId residenceOperationId, Hash256 residenceRequestHash,
        StableId128 ownerId, GraphMediaOwnerKeyV1 ownerKey, GraphMediaBindingV1 media,
        ParticipantId participantId, JournalPositionV1 bindingFactPosition,
        CapacityGrantId grantId, JournalPositionV1 currentFact, Hash256 coverageHashV2,
        GraphMediaCapacityAssignmentV1 assignment)
    {
        ArgumentNullException.ThrowIfNull(media); ArgumentNullException.ThrowIfNull(assignment);
        var session = ownerKey.Session;
        if (workId.Equals(default) || requestHash == default || residenceId.Equals(default) ||
            !residenceOperationId.IsValid || residenceRequestHash == default || ownerId.Equals(default) ||
            !ownerKey.IsValid || !participantId.IsValid || !bindingFactPosition.IsValid ||
            !grantId.IsValid || !currentFact.IsValid || coverageHashV2 == default ||
            bindingFactPosition.Session != session || currentFact.Session != session)
            throw new ArgumentException("A valid durable work authority is required.");
        WorkId = workId; RequestHash = requestHash; ResidenceId = residenceId;
        ResidenceOperationId = residenceOperationId; ResidenceRequestHash = residenceRequestHash;
        OwnerId = ownerId; OwnerKey = ownerKey; Media = media; ParticipantId = participantId;
        BindingFactPosition = bindingFactPosition; GrantId = grantId; CurrentFact = currentFact;
        CoverageHashV2 = coverageHashV2; Assignment = assignment;
    }
    internal StableId128 WorkId { get; }
    internal Hash256 RequestHash { get; }
    internal StableId128 ResidenceId { get; }
    internal OperationId ResidenceOperationId { get; }
    internal Hash256 ResidenceRequestHash { get; }
    internal StableId128 OwnerId { get; }
    internal GraphMediaOwnerKeyV1 OwnerKey { get; }
    internal GraphMediaBindingV1 Media { get; }
    internal ParticipantId ParticipantId { get; }
    internal JournalPositionV1 BindingFactPosition { get; }
    internal CapacityGrantId GrantId { get; }
    internal JournalPositionV1 CurrentFact { get; }
    internal Hash256 CoverageHashV2 { get; }
    internal GraphMediaCapacityAssignmentV1 Assignment { get; }
    internal static GraphMediaWorkAuthorityV1 FromRecord(GraphMediaWorkRecordV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(value.WorkId, value.RequestHash, value.ResidenceId, value.ResidenceOperationId,
            value.ResidenceRequestHash, value.OwnerId, value.OwnerKey, value.Media,
            value.ParticipantId, value.BindingFactPosition, value.GrantId, value.CurrentFact,
            value.CoverageHashV2, value.Assignment);
    }
}

internal sealed record GraphMediaWorkExecutionCommandBodyV1
{
    private readonly GraphMediaCleanupRegistrationV1[] _cleanups;
    internal GraphMediaWorkExecutionCommandBodyV1(OperationId operationId,
        GraphMediaWorkAuthorityV1 work, IReadOnlyList<GraphMediaCleanupRegistrationV1> cleanups,
        JournalPositionV1? expectedWorkFact, MonotonicStampV1 observedAt)
    {
        ArgumentNullException.ThrowIfNull(work); ArgumentNullException.ThrowIfNull(cleanups);
        if (!operationId.IsValid || !observedAt.IsValid || cleanups.Count is < 1 or > 16 ||
            expectedWorkFact is { } prior && prior.Session != work.OwnerKey.Session ||
            cleanups.Any(x => x is null || x.CleanupId.Equals(default) || x.RequestHash == default) ||
            cleanups.Select(x => x.CleanupId).Distinct().Count() != cleanups.Count)
            throw new ArgumentException("A valid durable work command is required.");
        _cleanups = cleanups.ToArray();
        for (var i = 1; i < _cleanups.Length; i++)
            if (Compare(_cleanups[i - 1].CleanupId, _cleanups[i].CleanupId) >= 0)
                throw new ArgumentException("Cleanup identities must be strictly ordered.");
        OperationId = operationId; Work = work; Cleanups = Array.AsReadOnly(_cleanups);
        ExpectedWorkFact = expectedWorkFact; ObservedAt = observedAt;
    }
    internal OperationId OperationId { get; }
    internal GraphMediaWorkAuthorityV1 Work { get; }
    internal IReadOnlyList<GraphMediaCleanupRegistrationV1> Cleanups { get; }
    internal JournalPositionV1? ExpectedWorkFact { get; }
    internal MonotonicStampV1 ObservedAt { get; }
    private static int Compare(StableId128 left, StableId128 right)
    { Span<byte> a = stackalloc byte[16]; Span<byte> b = stackalloc byte[16]; left.TryWriteBytes(a); right.TryWriteBytes(b); return a.SequenceCompareTo(b); }
}

internal sealed record GraphMediaWorkExecutionFactBodyV1
{
    private static readonly HashSet<string> RejectionCodes = new(StringComparer.Ordinal)
    { "work-authority-stale", "work-residence-mismatch", "work-owner-mismatch", "work-predecessor-conflict", "work-effect-rejected" };
    internal GraphMediaWorkExecutionFactBodyV1(JournalPositionV1 commandPosition,
        StableId128 workId, Hash256 workRequestHash, GraphMediaWorkExecutionOutcomeV1 outcome,
        Hash256? evidenceHash, BoundedAscii? safeCode, MonotonicStampV1 observedAt)
    {
        var code = safeCode?.ToString();
        if (!commandPosition.IsValid || workId.Equals(default) || workRequestHash == default ||
            !Enum.IsDefined(outcome) || !observedAt.IsValid ||
            outcome == GraphMediaWorkExecutionOutcomeV1.Completed &&
                (evidenceHash is null || evidenceHash.Value == default || safeCode is not null) ||
            outcome == GraphMediaWorkExecutionOutcomeV1.Unknown && (evidenceHash is not null || safeCode is not null) ||
            outcome == GraphMediaWorkExecutionOutcomeV1.Rejected &&
                (evidenceHash is not null || safeCode is null || !RejectionCodes.Contains(code!)))
            throw new ArgumentException("The durable work fact arm is invalid.");
        CommandPosition = commandPosition; WorkId = workId; WorkRequestHash = workRequestHash;
        Outcome = outcome; EvidenceHash = evidenceHash; SafeCode = safeCode; ObservedAt = observedAt;
    }
    internal JournalPositionV1 CommandPosition { get; }
    internal StableId128 WorkId { get; }
    internal Hash256 WorkRequestHash { get; }
    internal GraphMediaWorkExecutionOutcomeV1 Outcome { get; }
    internal Hash256? EvidenceHash { get; }
    internal BoundedAscii? SafeCode { get; }
    internal MonotonicStampV1 ObservedAt { get; }
}

internal sealed class GraphMediaWorkExecutionOuterV1
{
    private readonly byte[] _body;
    internal GraphMediaWorkExecutionOuterV1(SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    {
        ArgumentNullException.ThrowIfNull(expectedAuthority);
        if (!session.IsValid || expectedAuthority.Session != session || body.Length > GraphMediaWorkExecutionCodecsV1.MaximumBodyBytes)
            throw new ArgumentException("A valid durable work outer is required.");
        Session = session; ExpectedAuthority = expectedAuthority; _body = body.ToArray();
        BodyBytes = Array.AsReadOnly(_body);
    }
    internal SessionAuthorityStampV1 Session { get; }
    internal ExpectedAuthorityVectorV1 ExpectedAuthority { get; }
    internal IReadOnlyList<byte> BodyBytes { get; }
    internal ReadOnlyMemory<byte> BodyMemory => _body;
}

internal static class GraphMediaWorkExecutionCodecsV1
{
    internal const ushort Major = 1, Minor = 0;
    internal const int MaximumBodyBytes = 32_768, MaximumOuterBytes = 49_152;
    internal const string CommandSchemaId = "hpd.authority-payload-graph-media-work-execution-command.v1";
    internal const string FactSchemaId = "hpd.authority-payload-graph-media-work-execution-fact.v1";
    internal const string CommandBodySchemaId = "hpd.graph-media-work-execution-command-body.v1";
    internal const string FactBodySchemaId = "hpd.graph-media-work-execution-fact-body.v1";
    internal const string WorkAuthoritySchemaId = "hpd.graph-media-work-authority.v1";

    internal static byte[] EncodeOuter(GraphMediaWorkExecutionOuterV1 value)
    { ArgumentNullException.ThrowIfNull(value); var w = Map(3); Tag(w, 1); SessionAuthorityStampV1Codec.Write(w, value.Session); Tag(w, 2); AuthorityVectorCodecsV1.WriteVector(w, value.ExpectedAuthority); Tag(w, 3); w.WriteByteString(value.BodyMemory.Span); return Finish(w); }
    internal static bool TryDecodeOuter(ReadOnlyMemory<byte> bytes, out GraphMediaWorkExecutionOuterV1? value)
    {
        value = null; if (bytes.Length > MaximumOuterBytes) return false;
        try { var r = Reader(bytes); Start(r, 3); Need(r, 1); var session = SessionAuthorityStampV1Codec.Read(r); Need(r, 2); var authority = AuthorityVectorCodecsV1.ReadVector(r); Need(r, 3); var body = r.ReadByteString(); r.ReadEndMap(); if (r.BytesRemaining != 0 || body.Length > MaximumBodyBytes) return false; var x = new GraphMediaWorkExecutionOuterV1(session, authority, body); if (!bytes.Span.SequenceEqual(EncodeOuter(x))) return false; value = x; return true; } catch (Exception e) when (BadException(e)) { return false; }
    }
    internal static byte[] EncodeCommandBody(GraphMediaWorkExecutionCommandBodyV1 value)
    { ArgumentNullException.ThrowIfNull(value); var w = Map(5); Tag(w, 1); WriteOperation(w, value.OperationId); Tag(w, 2); WriteWork(w, value.Work); Tag(w, 3); WriteCleanups(w, value.Cleanups); Tag(w, 4); WritePositionOptional(w, value.ExpectedWorkFact); Tag(w, 5); w.WriteEncodedValue(MonotonicStampV1Codec.Encode(value.ObservedAt)); return Finish(w); }
    internal static bool TryDecodeCommandBody(ReadOnlyMemory<byte> bytes, out GraphMediaWorkExecutionCommandBodyV1? value) => TryBody(bytes, ReadCommand, EncodeCommandBody, out value);
    internal static byte[] EncodeFactBody(GraphMediaWorkExecutionFactBodyV1 value)
    { ArgumentNullException.ThrowIfNull(value); var w = Map(7); Tag(w, 1); AuthorityPositionCodecsV1.Write(w, value.CommandPosition); Tag(w, 2); WriteStable(w, value.WorkId); Tag(w, 3); WriteHash(w, value.WorkRequestHash); Tag(w, 4); w.WriteUInt64((byte)value.Outcome); Tag(w, 5); WriteHashOptional(w, value.EvidenceHash); Tag(w, 6); WriteAsciiOptional(w, value.SafeCode); Tag(w, 7); w.WriteEncodedValue(MonotonicStampV1Codec.Encode(value.ObservedAt)); return Finish(w); }
    internal static bool TryDecodeFactBody(ReadOnlyMemory<byte> bytes, out GraphMediaWorkExecutionFactBodyV1? value) => TryBody(bytes, ReadFact, EncodeFactBody, out value);
    internal static byte[] Encode(GraphMediaWorkAuthorityV1 value){var writer=new CborWriter(CborConformanceMode.Ctap2Canonical);WriteWork(writer,value);return writer.Encode();}
    internal static Hash256 ComputeHash(GraphMediaWorkExecutionCommandBodyV1 value)=>AuthorityIntegrityHashV1.Compute(CommandBodySchemaId,Major,Minor,EncodeCommandBody(value));
    internal static Hash256 ComputeHash(GraphMediaWorkExecutionFactBodyV1 value)=>AuthorityIntegrityHashV1.Compute(FactBodySchemaId,Major,Minor,EncodeFactBody(value));
    internal static Hash256 ComputeHash(GraphMediaWorkAuthorityV1 value)=>AuthorityIntegrityHashV1.Compute(WorkAuthoritySchemaId,Major,Minor,Encode(value));

    private static GraphMediaWorkExecutionCommandBodyV1 ReadCommand(CborReader r)
    { Start(r, 5); Need(r, 1); var op = ReadOperation(r); Need(r, 2); var work = ReadWork(r); Need(r, 3); var cleanup = ReadCleanups(r); Need(r, 4); var prior = ReadPositionOptional(r); Need(r, 5); var stamp = ReadStamp(r); r.ReadEndMap(); return new(op, work, cleanup, prior, stamp); }
    private static GraphMediaWorkExecutionFactBodyV1 ReadFact(CborReader r)
    { Start(r, 7); Need(r, 1); var command = AuthorityPositionCodecsV1.ReadJournal(r); Need(r, 2); var work = ReadStable(r); Need(r, 3); var request = ReadHash(r); Need(r, 4); var outcome = checked((GraphMediaWorkExecutionOutcomeV1)r.ReadUInt64()); Need(r, 5); var evidence = ReadHashOptional(r); Need(r, 6); var code = ReadAsciiOptional(r); Need(r, 7); var stamp = ReadStamp(r); r.ReadEndMap(); return new(command, work, request, outcome, evidence, code, stamp); }
    private static void WriteWork(CborWriter w, GraphMediaWorkAuthorityV1 x)
    { w.WriteStartMap(14); Tag(w, 1); WriteStable(w, x.WorkId); Tag(w, 2); WriteHash(w, x.RequestHash); Tag(w, 3); WriteStable(w, x.ResidenceId); Tag(w, 4); WriteOperation(w, x.ResidenceOperationId); Tag(w, 5); WriteHash(w, x.ResidenceRequestHash); Tag(w, 6); WriteStable(w, x.OwnerId); Tag(w, 7); WriteOwnerKey(w, x.OwnerKey); Tag(w, 8); WriteMedia(w, x.Media); Tag(w, 9); WriteParticipant(w, x.ParticipantId); Tag(w, 10); AuthorityPositionCodecsV1.Write(w, x.BindingFactPosition); Tag(w, 11); WriteGrant(w, x.GrantId); Tag(w, 12); AuthorityPositionCodecsV1.Write(w, x.CurrentFact); Tag(w, 13); WriteHash(w, x.CoverageHashV2); Tag(w, 14); WriteAssignment(w, x.Assignment); w.WriteEndMap(); }
    private static GraphMediaWorkAuthorityV1 ReadWork(CborReader r)
    { Start(r, 14); Need(r, 1); var work = ReadStable(r); Need(r, 2); var request = ReadHash(r); Need(r, 3); var residence = ReadStable(r); Need(r, 4); var operation = ReadOperation(r); Need(r, 5); var residenceRequest = ReadHash(r); Need(r, 6); var owner = ReadStable(r); Need(r, 7); var key = ReadOwnerKey(r); Need(r, 8); var media = ReadMedia(r); Need(r, 9); var participant = ReadParticipant(r); Need(r, 10); var binding = AuthorityPositionCodecsV1.ReadJournal(r); Need(r, 11); var grant = ReadGrant(r); Need(r, 12); var current = AuthorityPositionCodecsV1.ReadJournal(r); Need(r, 13); var coverage = ReadHash(r); Need(r, 14); var assignment = ReadAssignment(r); r.ReadEndMap(); return new(work, request, residence, operation, residenceRequest, owner, key, media, participant, binding, grant, current, coverage, assignment); }
    private static void WriteCleanups(CborWriter w, IReadOnlyList<GraphMediaCleanupRegistrationV1> values)
    { w.WriteStartArray(values.Count); foreach (var x in values) { w.WriteStartArray(2); WriteStable(w, x.CleanupId); WriteHash(w, x.RequestHash); w.WriteEndArray(); } w.WriteEndArray(); }
    private static GraphMediaCleanupRegistrationV1[] ReadCleanups(CborReader r)
    { var count = r.ReadStartArray(); if (count is null or < 1 or > 16) throw Bad(); var result = new GraphMediaCleanupRegistrationV1[count.Value]; for (var i = 0; i < result.Length; i++) { if (r.ReadStartArray() != 2) throw Bad(); result[i] = new(ReadStable(r), ReadHash(r)); r.ReadEndArray(); } r.ReadEndArray(); return result; }
    private static void WriteOwnerKey(CborWriter w, GraphMediaOwnerKeyV1 x)
    { w.WriteStartMap(3); Tag(w, 1); SessionAuthorityStampV1Codec.Write(w, x.Session); Tag(w, 2); WriteGraph(w, x.GraphGeneration); Tag(w, 3); WriteStable(w, x.MediaId); w.WriteEndMap(); }
    private static GraphMediaOwnerKeyV1 ReadOwnerKey(CborReader r)
    { Start(r, 3); Need(r, 1); var session = SessionAuthorityStampV1Codec.Read(r); Need(r, 2); var graph = ReadGraph(r); Need(r, 3); var media = ReadStable(r); r.ReadEndMap(); return new(session, graph, media); }
    private static void WriteMedia(CborWriter w, GraphMediaBindingV1 x)
    { w.WriteStartArray(13); w.WriteInt64(x.Start); w.WriteInt64(x.EndExclusive); WriteStable(w, x.FormatId); w.WriteUInt64(x.FormatRevision); w.WriteUInt64(x.SampleRateHz); w.WriteUInt64(x.ChannelCount); w.WriteUInt64(x.BytesPerSample); WriteStable(w, x.ClockId); w.WriteUInt64(x.ClockRevision); w.WriteUInt64(x.Sequence); w.WriteUInt64((byte)x.Discontinuity); w.WriteInt64(x.ByteLength); w.WriteInt64(x.FrameCount); w.WriteEndArray(); }
    private static GraphMediaBindingV1 ReadMedia(CborReader r)
    { if (r.ReadStartArray() != 13) throw Bad(); var start = r.ReadInt64(); var end = r.ReadInt64(); var format = ReadStable(r); var revision = checked((uint)r.ReadUInt64()); var rate = checked((uint)r.ReadUInt64()); var channels = checked((ushort)r.ReadUInt64()); var bytes = checked((ushort)r.ReadUInt64()); var clock = ReadStable(r); var clockRevision = checked((uint)r.ReadUInt64()); var sequence = r.ReadUInt64(); var discontinuity = checked((GraphMediaDiscontinuityKindV1)r.ReadUInt64()); var length = r.ReadInt64(); var frames = r.ReadInt64(); r.ReadEndArray(); if (!GraphMediaBindingV1.TryCreate(start, end, format, revision, rate, channels, bytes, clock, clockRevision, sequence, discontinuity, length, frames, null, out var value)) throw Bad(); return value!; }
    private static void WriteAssignment(CborWriter w, GraphMediaCapacityAssignmentV1 x)
    { w.WriteStartArray(2); var c = x.Charge; w.WriteStartMap(5); Tag(w, 1); w.WriteUInt64(c.DimensionId.Value); Tag(w, 2); w.WriteEncodedValue(CapacityScopeCanonicalCodecV1.Encode(c.Scope)); Tag(w, 3); w.WriteInt64(c.Amount); Tag(w, 4); WritePurpose(w, c.Purpose); Tag(w, 5); WriteWindow(w, c.Window); w.WriteEndMap(); w.WriteUInt64((byte)x.Arm); w.WriteEndArray(); }
    private static GraphMediaCapacityAssignmentV1 ReadAssignment(CborReader r)
    { if (r.ReadStartArray() != 2) throw Bad(); Start(r, 5); Need(r, 1); var dimension = new CapacityDimensionId(checked((ushort)r.ReadUInt64())); Need(r, 2); if (!CapacityScopeCanonicalCodecV1.TryDecode(r.ReadEncodedValue(), out var scope)) throw Bad(); Need(r, 3); var amount = r.ReadInt64(); Need(r, 4); var purpose = CapacityPurposeId.FromValue(ReadStable(r)); Need(r, 5); var window = ReadWindow(r); r.ReadEndMap(); var arm = checked((GraphMediaRepresentationArmV1)r.ReadUInt64()); r.ReadEndArray(); return new(new(dimension, scope!, amount, purpose, window), arm); }
    private static void WriteWindow(CborWriter w, CapacityChargeWindowV1 x)
    { w.WriteStartMap(x is CapacityChargeWindowV1.EndsAt ? 2 : 1); Tag(w, 1); w.WriteUInt64((ushort)x.Kind); if (x is CapacityChargeWindowV1.EndsAt at) { Tag(w, 2); w.WriteEncodedValue(MonotonicStampV1Codec.Encode(at.Value)); } w.WriteEndMap(); }
    private static CapacityChargeWindowV1 ReadWindow(CborReader r)
    { var count = r.ReadStartMap(); Need(r, 1); var kind = checked((CapacityChargeWindowKindV1)r.ReadUInt64()); CapacityChargeWindowV1 result = kind switch { CapacityChargeWindowKindV1.NoWindow when count == 1 => new CapacityChargeWindowV1.NoWindow(), CapacityChargeWindowKindV1.EndsAt when count == 2 => ReadEndsAt(r), _ => throw Bad() }; r.ReadEndMap(); return result; }
    private static CapacityChargeWindowV1 ReadEndsAt(CborReader r) { Need(r, 2); return new CapacityChargeWindowV1.EndsAt(ReadStamp(r)); }
    private static bool TryBody<T>(ReadOnlyMemory<byte> bytes, Func<CborReader, T> read, Func<T, byte[]> write, out T? value) where T : class
    { value = null; if (bytes.Length > MaximumBodyBytes) return false; try { var r = Reader(bytes); var x = read(r); if (r.BytesRemaining != 0 || !bytes.Span.SequenceEqual(write(x))) return false; value = x; return true; } catch (Exception e) when (BadException(e)) { return false; } }
    private static bool BadException(Exception e) => e is CborContentException or InvalidOperationException or ArgumentException or OverflowException;
    private static CborReader Reader(ReadOnlyMemory<byte> x) => new(x, CborConformanceMode.Ctap2Canonical, false);
    private static CborWriter Map(int n) { var w = new CborWriter(CborConformanceMode.Ctap2Canonical); w.WriteStartMap(n); return w; }
    private static byte[] Finish(CborWriter w) { w.WriteEndMap(); return w.Encode(); }
    private static void Start(CborReader r, int n) { if (r.ReadStartMap() != n) throw Bad(); }
    private static void Tag(CborWriter w, ulong x) => w.WriteUInt64(x);
    private static void Need(CborReader r, ulong x) { if (r.ReadUInt64() != x) throw Bad(); }
    private static ArgumentException Bad() => new("Unexpected durable work wire shape.");
    private static MonotonicStampV1 ReadStamp(CborReader r) { if (!MonotonicStampV1Codec.TryDecode(r.ReadEncodedValue(), out var x)) throw Bad(); return x; }
    private static void WritePositionOptional(CborWriter w, JournalPositionV1? x) { w.WriteStartArray(x is null ? 1 : 2); w.WriteUInt64(x is null ? 0UL : 1UL); if (x is { } p) AuthorityPositionCodecsV1.Write(w, p); w.WriteEndArray(); }
    private static JournalPositionV1? ReadPositionOptional(CborReader r) { var n = r.ReadStartArray(); var arm = r.ReadUInt64(); if (n == 1 && arm == 0) { r.ReadEndArray(); return null; } if (n != 2 || arm != 1) throw Bad(); var x = AuthorityPositionCodecsV1.ReadJournal(r); r.ReadEndArray(); return x; }
    private static void WriteHashOptional(CborWriter w, Hash256? x) { w.WriteStartArray(x is null ? 1 : 2); w.WriteUInt64(x is null ? 0UL : 1UL); if (x is { } h) WriteHash(w, h); w.WriteEndArray(); }
    private static Hash256? ReadHashOptional(CborReader r) { var n = r.ReadStartArray(); var arm = r.ReadUInt64(); if (n == 1 && arm == 0) { r.ReadEndArray(); return null; } if (n != 2 || arm != 1) throw Bad(); var x = ReadHash(r); r.ReadEndArray(); return x; }
    private static void WriteAsciiOptional(CborWriter w, BoundedAscii? x) { w.WriteStartArray(x is null ? 1 : 2); w.WriteUInt64(x is null ? 0UL : 1UL); if (x is { } a) BoundedAsciiCodec.Write(w, a); w.WriteEndArray(); }
    private static BoundedAscii? ReadAsciiOptional(CborReader r) { var n = r.ReadStartArray(); var arm = r.ReadUInt64(); if (n == 1 && arm == 0) { r.ReadEndArray(); return null; } if (n != 2 || arm != 1) throw Bad(); var x = BoundedAsciiCodec.Read(r); r.ReadEndArray(); return x; }
    private static void WriteHash(CborWriter w, Hash256 x) { Span<byte> b = stackalloc byte[32]; if (!x.TryWriteBytes(b)) throw Bad(); w.WriteByteString(b); }
    private static Hash256 ReadHash(CborReader r) { Span<byte> b = stackalloc byte[32]; if (!r.TryReadByteString(b, out var n) || n != 32) throw Bad(); return Hash256.FromBytes(b); }
    private static void WriteStable(CborWriter w, StableId128 x) { Span<byte> b = stackalloc byte[16]; if (!x.TryWriteBytes(b)) throw Bad(); w.WriteByteString(b); }
    private static StableId128 ReadStable(CborReader r) { Span<byte> b = stackalloc byte[16]; if (!r.TryReadByteString(b, out var n) || n != 16) throw Bad(); return StableId128.FromBytes(b); }
    private static void WriteOperation(CborWriter w, OperationId x) { Span<byte> b = stackalloc byte[16]; if (!x.TryWriteBytes(b)) throw Bad(); w.WriteByteString(b); }
    private static OperationId ReadOperation(CborReader r) => OperationId.FromValue(ReadStable(r));
    private static void WriteParticipant(CborWriter w, ParticipantId x) { Span<byte> b = stackalloc byte[16]; if (!x.TryWriteBytes(b)) throw Bad(); w.WriteByteString(b); }
    private static ParticipantId ReadParticipant(CborReader r) => ParticipantId.FromValue(ReadStable(r));
    private static void WriteGraph(CborWriter w, GraphGenerationId x) { Span<byte> b = stackalloc byte[16]; if (!x.TryWriteBytes(b)) throw Bad(); w.WriteByteString(b); }
    private static GraphGenerationId ReadGraph(CborReader r) => GraphGenerationId.FromValue(ReadStable(r));
    private static void WriteGrant(CborWriter w, CapacityGrantId x) { Span<byte> b = stackalloc byte[16]; if (!x.TryWriteBytes(b)) throw Bad(); w.WriteByteString(b); }
    private static CapacityGrantId ReadGrant(CborReader r) => CapacityGrantId.FromValue(ReadStable(r));
    private static void WritePurpose(CborWriter w, CapacityPurposeId x) { Span<byte> b = stackalloc byte[16]; if (!x.TryWriteBytes(b)) throw Bad(); w.WriteByteString(b); }
}

internal static class GraphMediaWorkExecutionFactIdsV1
{
    private static ReadOnlySpan<byte> CommandDomain => "hpd-graph-media-work-execution-command-fact-id-v1\0"u8;
    private static ReadOnlySpan<byte> FactDomain => "hpd-graph-media-work-execution-result-fact-id-v1\0"u8;
    internal static JournalFactId Command(SessionAuthorityStampV1 session, OperationId operation) => Derive(CommandDomain, session, operation, null);
    internal static JournalFactId Fact(JournalPositionV1 command) => Derive(FactDomain, command.Session, default, command);
    private static JournalFactId Derive(ReadOnlySpan<byte> domain, SessionAuthorityStampV1 session, OperationId operation, JournalPositionV1? position)
    { if (!session.IsValid || position is null && !operation.IsValid || position is { IsValid: false }) throw new ArgumentException("Valid work identity is required."); var second = position is null ? 16 : 8; var p = new byte[domain.Length + 1 + 4 + 16 + 1 + 4 + second]; domain.CopyTo(p); var o = domain.Length; p[o++] = 1; BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(o), 16); o += 4; session.LiveSessionId.TryWriteBytes(p.AsSpan(o)); o += 16; p[o++] = 2; BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(o), (uint)second); o += 4; if (position is { } at) BinaryPrimitives.WriteInt64BigEndian(p.AsSpan(o), at.Sequence); else operation.TryWriteBytes(p.AsSpan(o)); Span<byte> d = stackalloc byte[32]; SHA256.HashData(p, d); Span<byte> id = stackalloc byte[16]; d[..16].CopyTo(id); if (id.IndexOfAnyExcept((byte)0) < 0) id[^1] = 1; return JournalFactId.FromValue(StableId128.FromBytes(id)); }
}

internal static class GraphMediaWorkExecutionPayloadRegistrationsV1
{
    internal const ushort CommandDiscriminator = 47, FactDiscriminator = 48;
    internal static readonly AuthorityPayloadRegistrationV1 Command = AuthorityPayloadRegistrationV1.CreateOwnerRegistration(new(GraphMediaWorkExecutionCodecsV1.CommandSchemaId), 1, 0, OwnerSliceId.S1, GraphMediaWorkExecutionCodecsV1.MaximumOuterBytes, ValidateCommand);
    internal static readonly AuthorityPayloadRegistrationV1 Fact = AuthorityPayloadRegistrationV1.CreateOwnerRegistration(new(GraphMediaWorkExecutionCodecsV1.FactSchemaId), 1, 0, OwnerSliceId.S1, GraphMediaWorkExecutionCodecsV1.MaximumOuterBytes, ValidateFact);
    private static bool ValidateCommand(ReadOnlyMemory<byte> payload, SessionAuthorityStampV1 session) => GraphMediaWorkExecutionCodecsV1.TryDecodeOuter(payload, out var outer) && outer!.Session == session && GraphMediaWorkExecutionCodecsV1.TryDecodeCommandBody(outer.BodyMemory, out var body) && outer.ExpectedAuthority.Axes.Length == 1 && outer.ExpectedAuthority.Axes[0].Value is AuthorityAxisValueV1.Graph graph && graph.Value == body!.Work.OwnerKey.GraphGeneration;
    private static bool ValidateFact(ReadOnlyMemory<byte> payload, SessionAuthorityStampV1 session) => GraphMediaWorkExecutionCodecsV1.TryDecodeOuter(payload, out var outer) && outer!.Session == session && GraphMediaWorkExecutionCodecsV1.TryDecodeFactBody(outer.BodyMemory, out var body) && body!.CommandPosition.Session == session;
}
