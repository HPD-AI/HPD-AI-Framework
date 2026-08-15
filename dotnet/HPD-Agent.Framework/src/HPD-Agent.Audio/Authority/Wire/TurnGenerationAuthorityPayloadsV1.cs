using System.Buffers;
using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal sealed record TurnDecisionFinalizedV1
{
    internal TurnDecisionFinalizedV1(OperationId operationId, JournalPositionV1 sourcePosition, ExpectedAuthorityVectorV1 authority, ushort disposition)
    {
        if (!operationId.IsValid || !sourcePosition.IsValid || authority is null || authority.Session != sourcePosition.Session || disposition == 0)
            throw new ArgumentException("Invalid finalized turn decision.");
        OperationId = operationId; SourcePosition = sourcePosition; Authority = authority; Disposition = disposition;
    }
    internal OperationId OperationId { get; }
    internal JournalPositionV1 SourcePosition { get; }
    internal ExpectedAuthorityVectorV1 Authority { get; }
    internal ushort Disposition { get; }
}

internal sealed record ProviderGenerationChangedV1
{
    internal ProviderGenerationChangedV1(SessionAuthorityStampV1 session, ProviderGenerationId expectedPrevious, ProviderGenerationId proposedNext, OwnerSliceId owner)
    {
        if (!session.IsValid || !expectedPrevious.IsValid || !proposedNext.IsValid || expectedPrevious == proposedNext || owner != OwnerSliceId.S5)
            throw new ArgumentException("Invalid provider generation transition.");
        Session = session; ExpectedPrevious = expectedPrevious; ProposedNext = proposedNext; Owner = owner;
    }
    internal SessionAuthorityStampV1 Session { get; }
    internal ProviderGenerationId ExpectedPrevious { get; }
    internal ProviderGenerationId ProposedNext { get; }
    internal OwnerSliceId Owner { get; }
}

internal sealed record RouteGenerationChangedV1
{
    internal RouteGenerationChangedV1(SessionAuthorityStampV1 session, RouteGenerationId expectedPrevious, RouteGenerationId proposedNext, OwnerSliceId owner)
    {
        if (!session.IsValid || !expectedPrevious.IsValid || !proposedNext.IsValid || expectedPrevious == proposedNext || owner != OwnerSliceId.S8)
            throw new ArgumentException("Invalid route generation transition.");
        Session = session; ExpectedPrevious = expectedPrevious; ProposedNext = proposedNext; Owner = owner;
    }
    internal SessionAuthorityStampV1 Session { get; }
    internal RouteGenerationId ExpectedPrevious { get; }
    internal RouteGenerationId ProposedNext { get; }
    internal OwnerSliceId Owner { get; }
}

internal sealed record TransportGenerationChangedV1
{
    internal TransportGenerationChangedV1(SessionAuthorityStampV1 session, TransportGenerationId expectedPrevious, TransportGenerationId proposedNext, OwnerSliceId owner)
    {
        if (!session.IsValid || !expectedPrevious.IsValid || !proposedNext.IsValid || expectedPrevious == proposedNext || owner != OwnerSliceId.S11)
            throw new ArgumentException("Invalid transport generation transition.");
        Session = session; ExpectedPrevious = expectedPrevious; ProposedNext = proposedNext; Owner = owner;
    }
    internal SessionAuthorityStampV1 Session { get; }
    internal TransportGenerationId ExpectedPrevious { get; }
    internal TransportGenerationId ProposedNext { get; }
    internal OwnerSliceId Owner { get; }
}

internal abstract class TurnGenerationAuthorityOuterV1
{
    private readonly byte[] _body;
    protected TurnGenerationAuthorityOuterV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    {
        TurnGenerationAuthorityOuterCodecV1.Validate(session, expectedAuthority, body);
        Session = session; ExpectedAuthority = expectedAuthority; _body = body.ToArray(); Body = Array.AsReadOnly(_body);
    }
    internal SessionAuthorityStampV1 Session { get; }
    internal ExpectedAuthorityVectorV1 ExpectedAuthority { get; }
    internal IReadOnlyList<byte> Body { get; }
    internal ReadOnlySpan<byte> BodyBytes => _body;
}

internal sealed class TurnDecisionFinalizedOuterV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    : TurnGenerationAuthorityOuterV1(session, expectedAuthority, body);
internal sealed class GraphGenerationChangedOuterV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    : TurnGenerationAuthorityOuterV1(session, expectedAuthority, body);
internal sealed class ProviderGenerationChangedOuterV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    : TurnGenerationAuthorityOuterV1(session, expectedAuthority, body);
internal sealed class RouteGenerationChangedOuterV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    : TurnGenerationAuthorityOuterV1(session, expectedAuthority, body);
internal sealed class TransportGenerationChangedOuterV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    : TurnGenerationAuthorityOuterV1(session, expectedAuthority, body);

internal static class TurnGenerationAuthorityPayloadRegistrationsV1
{
    internal const ushort GraphGenerationChangedDiscriminator = 4;
    internal const ushort TurnDecisionFinalizedDiscriminator = 10;
    internal const ushort ProviderGenerationChangedDiscriminator = 15;
    internal const ushort RouteGenerationChangedDiscriminator = 24;
    internal const ushort TransportGenerationChangedDiscriminator = 33;
    internal static readonly AuthorityPayloadRegistrationV1 GraphGenerationChanged = Register(TurnGenerationAuthorityOuterCodecV1.GraphOuterSchemaId, OwnerSliceId.S2,
        static (p, s) => TurnGenerationAuthorityOuterCodecV1.TryDecodeGraph(p, out var v) && v!.Session == s);
    internal static readonly AuthorityPayloadRegistrationV1 TurnDecisionFinalized = Register(TurnGenerationAuthorityOuterCodecV1.TurnOuterSchemaId, OwnerSliceId.S4,
        static (p, s) => TurnGenerationAuthorityOuterCodecV1.TryDecodeTurn(p, out var v) && v!.Session == s);
    internal static readonly AuthorityPayloadRegistrationV1 ProviderGenerationChanged = Register(TurnGenerationAuthorityOuterCodecV1.ProviderOuterSchemaId, OwnerSliceId.S5,
        static (p, s) => TurnGenerationAuthorityOuterCodecV1.TryDecodeProvider(p, out var v) && v!.Session == s);
    internal static readonly AuthorityPayloadRegistrationV1 RouteGenerationChanged = Register(TurnGenerationAuthorityOuterCodecV1.RouteOuterSchemaId, OwnerSliceId.S8,
        static (p, s) => TurnGenerationAuthorityOuterCodecV1.TryDecodeRoute(p, out var v) && v!.Session == s);
    internal static readonly AuthorityPayloadRegistrationV1 TransportGenerationChanged = Register(TurnGenerationAuthorityOuterCodecV1.TransportOuterSchemaId, OwnerSliceId.S11,
        static (p, s) => TurnGenerationAuthorityOuterCodecV1.TryDecodeTransport(p, out var v) && v!.Session == s);
    private static AuthorityPayloadRegistrationV1 Register(string schema, OwnerSliceId owner, Func<ReadOnlyMemory<byte>, SessionAuthorityStampV1, bool> validator) =>
        AuthorityPayloadRegistrationV1.CreateOwnerRegistration(new BoundedAscii(schema), 1, 0, owner, TurnGenerationAuthorityOuterCodecV1.MaximumEncodedBytes, validator);
}

internal static class TurnGenerationAuthorityOuterCodecV1
{
    private delegate T Factory<out T>(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 authority, ReadOnlySpan<byte> body) where T : TurnGenerationAuthorityOuterV1;
    internal const string TurnOuterSchemaId = "hpd.authority-payload-turn-decision-finalized.v1";
    internal const string GraphOuterSchemaId = "hpd.authority-payload-graph-generation-changed.v1";
    internal const string ProviderOuterSchemaId = "hpd.authority-payload-provider-generation-changed.v1";
    internal const string RouteOuterSchemaId = "hpd.authority-payload-route-generation-changed.v1";
    internal const string TransportOuterSchemaId = "hpd.authority-payload-transport-generation-changed.v1";
    internal const int MaximumBodyBytes = 65_536, MaximumEncodedBytes = 66_560;
    internal static byte[] Encode(TurnDecisionFinalizedOuterV1 value) => EncodeValue(value);
    internal static byte[] Encode(GraphGenerationChangedOuterV1 value) => EncodeValue(value);
    internal static byte[] Encode(ProviderGenerationChangedOuterV1 value) => EncodeValue(value);
    internal static byte[] Encode(RouteGenerationChangedOuterV1 value) => EncodeValue(value);
    internal static byte[] Encode(TransportGenerationChangedOuterV1 value) => EncodeValue(value);
    internal static bool TryDecodeTurn(ReadOnlyMemory<byte> e, out TurnDecisionFinalizedOuterV1? v) => TryDecode(e, static (s, a, b) => new(s, a, b), out v);
    internal static bool TryDecodeGraph(ReadOnlyMemory<byte> e, out GraphGenerationChangedOuterV1? v) => TryDecode(e, static (s, a, b) => new(s, a, b), out v);
    internal static bool TryDecodeProvider(ReadOnlyMemory<byte> e, out ProviderGenerationChangedOuterV1? v) => TryDecode(e, static (s, a, b) => new(s, a, b), out v);
    internal static bool TryDecodeRoute(ReadOnlyMemory<byte> e, out RouteGenerationChangedOuterV1? v) => TryDecode(e, static (s, a, b) => new(s, a, b), out v);
    internal static bool TryDecodeTransport(ReadOnlyMemory<byte> e, out TransportGenerationChangedOuterV1? v) => TryDecode(e, static (s, a, b) => new(s, a, b), out v);
    internal static Hash256 ComputeHash(TurnDecisionFinalizedOuterV1 v) => Hash(TurnOuterSchemaId, v);
    internal static Hash256 ComputeHash(GraphGenerationChangedOuterV1 v) => Hash(GraphOuterSchemaId, v);
    internal static Hash256 ComputeHash(ProviderGenerationChangedOuterV1 v) => Hash(ProviderOuterSchemaId, v);
    internal static Hash256 ComputeHash(RouteGenerationChangedOuterV1 v) => Hash(RouteOuterSchemaId, v);
    internal static Hash256 ComputeHash(TransportGenerationChangedOuterV1 v) => Hash(TransportOuterSchemaId, v);
    internal static void Validate(SessionAuthorityStampV1 s, ExpectedAuthorityVectorV1 a, ReadOnlySpan<byte> b)
    { if (!s.IsValid || a is null || a.Session != s || b.Length > MaximumBodyBytes) throw new ArgumentException("Invalid authority outer."); }
    private static Hash256 Hash(string schema, TurnGenerationAuthorityOuterV1 v) => AuthorityIntegrityHashV1.Compute(schema, 1, 0, EncodeValue(v));
    private static byte[] EncodeValue(TurnGenerationAuthorityOuterV1 v)
    {
        ArgumentNullException.ThrowIfNull(v); Validate(v.Session, v.ExpectedAuthority, v.BodyBytes); var w = new CborWriter(CborConformanceMode.Ctap2Canonical); w.WriteStartMap(3);
        w.WriteUInt64(1); w.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(v.Session)); w.WriteUInt64(2); w.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(v.ExpectedAuthority));
        w.WriteUInt64(3); w.WriteByteString(v.BodyBytes); w.WriteEndMap(); var r = w.Encode(); if (r.Length > MaximumEncodedBytes) throw new ArgumentOutOfRangeException(nameof(v)); return r;
    }
    private static bool TryDecode<T>(ReadOnlyMemory<byte> e, Factory<T> f, out T? v) where T : TurnGenerationAuthorityOuterV1
    {
        v = null; if (e.Length is 0 or > MaximumEncodedBytes) return false; byte[]? rented = null;
        try { var r = new CborReader(e, CborConformanceMode.Ctap2Canonical, false); if (r.ReadStartMap() != 3 || r.ReadUInt64() != 1 || !SessionAuthorityStampV1Codec.TryDecode(r.ReadEncodedValue(), out var s) || r.ReadUInt64() != 2 || !AuthorityVectorCodecsV1.TryDecodeVector(r.ReadEncodedValue(), out var a) || r.ReadUInt64() != 3) return false;
            rented = ArrayPool<byte>.Shared.Rent(MaximumBodyBytes); if (!r.TryReadByteString(rented, out var n) || n > MaximumBodyBytes) return false; r.ReadEndMap(); if (r.BytesRemaining != 0 || a!.Session != s) return false;
            v = f(s, a, rented.AsSpan(0, n)); return e.Span.SequenceEqual(EncodeValue(v)); }
        catch (Exception x) when (x is CborContentException or InvalidOperationException or ArgumentException or OverflowException) { v = null; return false; }
        finally { if (rented is not null) ArrayPool<byte>.Shared.Return(rented, true); }
    }
}

internal static class TurnGenerationRecordCodecsV1
{
    internal const string TurnSchemaId = "hpd.turn-decision-finalized.v1";
    internal const string ProviderSchemaId = "hpd.provider-generation-changed.v1";
    internal const string RouteSchemaId = "hpd.route-generation-changed.v1";
    internal const string TransportSchemaId = "hpd.transport-generation-changed.v1";
    internal static byte[] Encode(TurnDecisionFinalizedV1 v)
    { ArgumentNullException.ThrowIfNull(v); var w = new CborWriter(CborConformanceMode.Ctap2Canonical); w.WriteStartMap(4); w.WriteUInt64(1); WriteId(w, v.OperationId); w.WriteUInt64(2); w.WriteEncodedValue(AuthorityPositionCodecsV1.Encode(v.SourcePosition)); w.WriteUInt64(3); w.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(v.Authority)); w.WriteUInt64(4); w.WriteUInt64(v.Disposition); w.WriteEndMap(); return w.Encode(); }
    internal static bool TryDecodeTurn(ReadOnlyMemory<byte> e, out TurnDecisionFinalizedV1? v)
    { v = null; try { var r = Reader(e); if (r.ReadStartMap() != 4 || r.ReadUInt64() != 1) return false; var op = OperationId.FromValue(ReadId(r)); if (r.ReadUInt64() != 2 || !AuthorityPositionCodecsV1.TryDecodeJournal(r.ReadEncodedValue(), out var pos) || r.ReadUInt64() != 3 || !AuthorityVectorCodecsV1.TryDecodeVector(r.ReadEncodedValue(), out var a) || r.ReadUInt64() != 4) return false; var d = checked((ushort)r.ReadUInt64()); r.ReadEndMap(); if (r.BytesRemaining != 0) return false; v = new(op, pos, a!, d); return e.Span.SequenceEqual(Encode(v)); } catch (Exception x) when (x is CborContentException or InvalidOperationException or ArgumentException or OverflowException) { return false; } }
    internal static byte[] Encode(ProviderGenerationChangedV1 v) => EncodeGeneration(v.Session, v.ExpectedPrevious, v.ProposedNext, v.Owner);
    internal static byte[] Encode(RouteGenerationChangedV1 v) => EncodeGeneration(v.Session, v.ExpectedPrevious, v.ProposedNext, v.Owner);
    internal static byte[] Encode(TransportGenerationChangedV1 v) => EncodeGeneration(v.Session, v.ExpectedPrevious, v.ProposedNext, v.Owner);
    internal static bool TryDecodeProvider(ReadOnlyMemory<byte> e, out ProviderGenerationChangedV1? v) => DecodeGeneration(e, static (s, p, n, o) => new(s, ProviderGenerationId.FromValue(p), ProviderGenerationId.FromValue(n), o), Encode, out v);
    internal static bool TryDecodeRoute(ReadOnlyMemory<byte> e, out RouteGenerationChangedV1? v) => DecodeGeneration(e, static (s, p, n, o) => new(s, RouteGenerationId.FromValue(p), RouteGenerationId.FromValue(n), o), Encode, out v);
    internal static bool TryDecodeTransport(ReadOnlyMemory<byte> e, out TransportGenerationChangedV1? v) => DecodeGeneration(e, static (s, p, n, o) => new(s, TransportGenerationId.FromValue(p), TransportGenerationId.FromValue(n), o), Encode, out v);
    internal static Hash256 ComputeHash(TurnDecisionFinalizedV1 v) => AuthorityIntegrityHashV1.Compute(TurnSchemaId, 1, 0, Encode(v));
    internal static Hash256 ComputeHash(ProviderGenerationChangedV1 v) => AuthorityIntegrityHashV1.Compute(ProviderSchemaId, 1, 0, Encode(v));
    internal static Hash256 ComputeHash(RouteGenerationChangedV1 v) => AuthorityIntegrityHashV1.Compute(RouteSchemaId, 1, 0, Encode(v));
    internal static Hash256 ComputeHash(TransportGenerationChangedV1 v) => AuthorityIntegrityHashV1.Compute(TransportSchemaId, 1, 0, Encode(v));
    private delegate T GenFactory<out T>(SessionAuthorityStampV1 s, StableId128 p, StableId128 n, OwnerSliceId o);
    private delegate byte[] GenEncoder<in T>(T value);
    private static bool DecodeGeneration<T>(ReadOnlyMemory<byte> e, GenFactory<T> f, GenEncoder<T> encode, out T? v) where T : class
    { v = null; try { var r = Reader(e); if (r.ReadStartMap() != 4 || r.ReadUInt64() != 1 || !SessionAuthorityStampV1Codec.TryDecode(r.ReadEncodedValue(), out var s) || r.ReadUInt64() != 2) return false; var p = ReadId(r); if (r.ReadUInt64() != 3) return false; var n = ReadId(r); if (r.ReadUInt64() != 4) return false; var o = (OwnerSliceId)checked((ushort)r.ReadUInt64()); r.ReadEndMap(); if (r.BytesRemaining != 0) return false; v = f(s, p, n, o); return e.Span.SequenceEqual(encode(v)); } catch (Exception x) when (x is CborContentException or InvalidOperationException or ArgumentException or OverflowException) { return false; } }
    private static byte[] EncodeGeneration<T>(SessionAuthorityStampV1 s, T p, T n, OwnerSliceId o) where T : struct
    { var w = new CborWriter(CborConformanceMode.Ctap2Canonical); w.WriteStartMap(4); w.WriteUInt64(1); w.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(s)); w.WriteUInt64(2); WriteId(w, p); w.WriteUInt64(3); WriteId(w, n); w.WriteUInt64(4); w.WriteUInt64((ushort)o); w.WriteEndMap(); return w.Encode(); }
    private static CborReader Reader(ReadOnlyMemory<byte> e) => new(e, CborConformanceMode.Ctap2Canonical, false);
    private static StableId128 ReadId(CborReader r) { var b = r.ReadByteString(); if (b.Length != 16) throw new CborContentException("ID length."); return StableId128.FromBytes(b); }
    private static void WriteId<T>(CborWriter w, T v) where T : struct
    { Span<byte> b = stackalloc byte[16]; var ok = v switch { OperationId x => x.TryWriteBytes(b), ProviderGenerationId x => x.TryWriteBytes(b), RouteGenerationId x => x.TryWriteBytes(b), TransportGenerationId x => x.TryWriteBytes(b), _ => false }; if (!ok) throw new ArgumentException("Invalid ID."); w.WriteByteString(b); }
}
