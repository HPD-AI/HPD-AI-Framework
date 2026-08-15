using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal abstract record RouteLifecycleRecordV1
{
    protected RouteLifecycleRecordV1(OperationId operationId, JournalPositionV1 sourcePosition,
        ExpectedAuthorityVectorV1 authority, ushort disposition)
    {
        if (!operationId.IsValid || !sourcePosition.IsValid || authority is null ||
            authority.Session != sourcePosition.Session || disposition == 0)
            throw new ArgumentException("Invalid route lifecycle record.");
        OperationId = operationId;
        SourcePosition = sourcePosition;
        Authority = authority;
        Disposition = disposition;
    }
    internal OperationId OperationId { get; }
    internal JournalPositionV1 SourcePosition { get; }
    internal ExpectedAuthorityVectorV1 Authority { get; }
    internal ushort Disposition { get; }
}

internal sealed record RouteRequestAdmittedV1 : RouteLifecycleRecordV1 { internal RouteRequestAdmittedV1(OperationId o, JournalPositionV1 p, ExpectedAuthorityVectorV1 a, ushort d) : base(o,p,a,d) { } }
internal sealed record RoutePreparationOwnerClaimedV1 : RouteLifecycleRecordV1 { internal RoutePreparationOwnerClaimedV1(OperationId o, JournalPositionV1 p, ExpectedAuthorityVectorV1 a, ushort d) : base(o,p,a,d) { } }
internal sealed record RouteCutoverAuthorizedV1 : RouteLifecycleRecordV1 { internal RouteCutoverAuthorizedV1(OperationId o, JournalPositionV1 p, ExpectedAuthorityVectorV1 a, ushort d) : base(o,p,a,d) { } }
internal sealed record RouteAuthorityCommittedV1 : RouteLifecycleRecordV1 { internal RouteAuthorityCommittedV1(OperationId o, JournalPositionV1 p, ExpectedAuthorityVectorV1 a, ushort d) : base(o,p,a,d) { } }
internal sealed record RouteAxisAppliedV1 : RouteLifecycleRecordV1 { internal RouteAxisAppliedV1(OperationId o, JournalPositionV1 p, ExpectedAuthorityVectorV1 a, ushort d) : base(o,p,a,d) { } }
internal sealed record RouteRegistrationClosedV1 : RouteLifecycleRecordV1 { internal RouteRegistrationClosedV1(OperationId o, JournalPositionV1 p, ExpectedAuthorityVectorV1 a, ushort d) : base(o,p,a,d) { } }
internal sealed record RouteTransitionTerminalizedV1 : RouteLifecycleRecordV1 { internal RouteTransitionTerminalizedV1(OperationId o, JournalPositionV1 p, ExpectedAuthorityVectorV1 a, ushort d) : base(o,p,a,d) { } }

internal static class RouteLifecycleAuthorityRecordCodecsV1
{
    internal static byte[] Encode(RouteRequestAdmittedV1 value) => EncodeValue(value);
    internal static byte[] Encode(RoutePreparationOwnerClaimedV1 value) => EncodeValue(value);
    internal static byte[] Encode(RouteCutoverAuthorizedV1 value) => EncodeValue(value);
    internal static byte[] Encode(RouteAuthorityCommittedV1 value) => EncodeValue(value);
    internal static byte[] Encode(RouteAxisAppliedV1 value) => EncodeValue(value);
    internal static byte[] Encode(RouteRegistrationClosedV1 value) => EncodeValue(value);
    internal static byte[] Encode(RouteTransitionTerminalizedV1 value) => EncodeValue(value);

    internal static bool TryDecodeRequest(ReadOnlyMemory<byte> bytes, out RouteRequestAdmittedV1? value) => Decode(bytes, static (o,p,a,d) => new(o,p,a,d), Encode, out value);
    internal static bool TryDecodePreparation(ReadOnlyMemory<byte> bytes, out RoutePreparationOwnerClaimedV1? value) => Decode(bytes, static (o,p,a,d) => new(o,p,a,d), Encode, out value);
    internal static bool TryDecodeCutover(ReadOnlyMemory<byte> bytes, out RouteCutoverAuthorizedV1? value) => Decode(bytes, static (o,p,a,d) => new(o,p,a,d), Encode, out value);
    internal static bool TryDecodeAuthority(ReadOnlyMemory<byte> bytes, out RouteAuthorityCommittedV1? value) => Decode(bytes, static (o,p,a,d) => new(o,p,a,d), Encode, out value);
    internal static bool TryDecodeAxis(ReadOnlyMemory<byte> bytes, out RouteAxisAppliedV1? value) => Decode(bytes, static (o,p,a,d) => new(o,p,a,d), Encode, out value);
    internal static bool TryDecodeRegistration(ReadOnlyMemory<byte> bytes, out RouteRegistrationClosedV1? value) => Decode(bytes, static (o,p,a,d) => new(o,p,a,d), Encode, out value);
    internal static bool TryDecodeTerminal(ReadOnlyMemory<byte> bytes, out RouteTransitionTerminalizedV1? value) => Decode(bytes, static (o,p,a,d) => new(o,p,a,d), Encode, out value);

    internal static Hash256 ComputeHash(RouteRequestAdmittedV1 value) => Hash("hpd.route-request-admitted.v1", Encode(value));
    internal static Hash256 ComputeHash(RoutePreparationOwnerClaimedV1 value) => Hash("hpd.route-preparation-owner-claimed.v1", Encode(value));
    internal static Hash256 ComputeHash(RouteCutoverAuthorizedV1 value) => Hash("hpd.route-cutover-authorized.v1", Encode(value));
    internal static Hash256 ComputeHash(RouteAuthorityCommittedV1 value) => Hash("hpd.route-authority-committed.v1", Encode(value));
    internal static Hash256 ComputeHash(RouteAxisAppliedV1 value) => Hash("hpd.route-axis-applied.v1", Encode(value));
    internal static Hash256 ComputeHash(RouteRegistrationClosedV1 value) => Hash("hpd.route-registration-closed.v1", Encode(value));
    internal static Hash256 ComputeHash(RouteTransitionTerminalizedV1 value) => Hash("hpd.route-transition-terminalized.v1", Encode(value));

    private static byte[] EncodeValue(RouteLifecycleRecordV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(4);
        writer.WriteUInt64(1); WriteOperation(writer, value.OperationId);
        writer.WriteUInt64(2); writer.WriteEncodedValue(AuthorityPositionCodecsV1.Encode(value.SourcePosition));
        writer.WriteUInt64(3); writer.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(value.Authority));
        writer.WriteUInt64(4); writer.WriteUInt64(value.Disposition);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static bool Decode<T>(ReadOnlyMemory<byte> bytes,
        Func<OperationId, JournalPositionV1, ExpectedAuthorityVectorV1, ushort, T> create,
        Func<T, byte[]> encode, out T? value) where T : class
    {
        value = null;
        if (bytes.Length is 0 or > 16_384) return false;
        try
        {
            var reader = new CborReader(bytes, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 4 || reader.ReadUInt64() != 1) return false;
            var operation = ReadOperation(reader);
            if (reader.ReadUInt64() != 2) return false;
            var position = AuthorityPositionCodecsV1.ReadJournal(reader);
            if (reader.ReadUInt64() != 3 || !AuthorityVectorCodecsV1.TryDecodeVector(reader.ReadEncodedValue(), out var authority)) return false;
            if (reader.ReadUInt64() != 4) return false;
            var rawDisposition = reader.ReadUInt64();
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0 || rawDisposition is 0 or > ushort.MaxValue) return false;
            var candidate = create(operation, position, authority!, (ushort)rawDisposition);
            if (!encode(candidate).AsSpan().SequenceEqual(bytes.Span)) return false;
            value = candidate;
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException) { return false; }
    }

    private static void WriteOperation(CborWriter writer, OperationId value)
    { Span<byte> bytes = stackalloc byte[16]; if (!value.TryWriteBytes(bytes)) throw new ArgumentException("An operation is required."); writer.WriteByteString(bytes); }
    private static OperationId ReadOperation(CborReader reader)
    { Span<byte> bytes = stackalloc byte[16]; if (!reader.TryReadByteString(bytes, out var written) || written != 16) throw new CborContentException("An operation identifier is exactly 16 bytes."); return OperationId.FromValue(StableId128.FromBytes(bytes)); }
    private static Hash256 Hash(string schema, byte[] bytes) => AuthorityIntegrityHashV1.Compute(schema, 1, 0, bytes);
}
