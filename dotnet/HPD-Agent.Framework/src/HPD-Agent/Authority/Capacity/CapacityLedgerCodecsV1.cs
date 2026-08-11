using System.Formats.Cbor;

namespace HPD.Agent.Authority;

internal static class CapacityLedgerCodecsV1
{
    internal const string ReservationSchemaId = "hpd.capacity-reservation-fact-body.v1";
    internal const string SettlementSchemaId = "hpd.capacity-settlement-fact-body.v1";
    internal const ushort Major = 1;
    internal const ushort Minor = 0;

    internal static byte[] EncodeReservation(CapacityReservationFactBodyV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var writer = Writer();
        writer.WriteStartMap(3);
        writer.WriteUInt64(1); WriteId(writer, value.GrantId.TryWriteBytes);
        writer.WriteUInt64(2); WriteRequest(writer, value.Request);
        writer.WriteUInt64(3); WriteExpiry(writer, value.ExpiresAt);
        writer.WriteEndMap();
        return writer.Encode();
    }

    internal static bool TryDecodeReservation(ReadOnlyMemory<byte> encoded, out CapacityReservationFactBodyV1? value) =>
        TryDecode(encoded, reader =>
        {
            RequireMap(reader, 3, 1);
            var grant = CapacityGrantId.FromValue(ReadStableId(reader));
            RequireTag(reader, 2);
            var request = ReadRequest(reader);
            RequireTag(reader, 3);
            var expiry = ReadExpiry(reader);
            reader.ReadEndMap();
            return new CapacityReservationFactBodyV1(grant, request, expiry);
        }, out value);

    internal static byte[] EncodeSettlement(CapacitySettlementFactBodyV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var writer = Writer();
        writer.WriteStartMap(6);
        writer.WriteUInt64(1); WriteId(writer, value.GrantId.TryWriteBytes);
        writer.WriteUInt64(2); WriteId(writer, value.OperationId.TryWriteBytes);
        writer.WriteUInt64(3); writer.WriteEncodedValue(AuthorityPositionCodecsV1.Encode(value.ExpectedFact));
        writer.WriteUInt64(4); writer.WriteUInt64((ushort)value.Kind);
        writer.WriteUInt64(5); writer.WriteStartArray(value.Charges.Count);
        foreach (var charge in value.Charges) WriteSettlementCharge(writer, charge);
        writer.WriteEndArray();
        writer.WriteUInt64(6); writer.WriteEncodedValue(MonotonicStampV1Codec.Encode(value.EvidenceAt));
        writer.WriteEndMap();
        return writer.Encode();
    }

    internal static bool TryDecodeSettlement(ReadOnlyMemory<byte> encoded, out CapacitySettlementFactBodyV1? value) =>
        TryDecode(encoded, reader =>
        {
            RequireMap(reader, 6, 1);
            var grant = CapacityGrantId.FromValue(ReadStableId(reader));
            RequireTag(reader, 2);
            var operation = OperationId.FromValue(ReadStableId(reader));
            RequireTag(reader, 3);
            if (!AuthorityPositionCodecsV1.TryDecodeJournal(reader.ReadEncodedValue(), out var expected))
                throw new CborContentException("Invalid capacity predecessor position.");
            RequireTag(reader, 4);
            var kind = checked((CapacitySettlementKindV1)reader.ReadUInt64());
            RequireTag(reader, 5);
            var count = reader.ReadStartArray();
            if (count is null or < 1 or > CapacityRequestV1.MaximumCharges)
                throw new CborContentException("Settlement charges must be a definite array of 1..256 items.");
            var charges = new CapacitySettlementChargeV1[count.Value];
            for (var index = 0; index < charges.Length; index++) charges[index] = ReadSettlementCharge(reader);
            EnsureStrictlySorted(charges, CapacitySettlementChargeComparerV1.Instance);
            reader.ReadEndArray();
            RequireTag(reader, 6);
            if (!MonotonicStampV1Codec.TryDecode(reader.ReadEncodedValue(), out var evidenceAt))
                throw new CborContentException("Invalid capacity settlement evidence instant.");
            reader.ReadEndMap();
            return new CapacitySettlementFactBodyV1(grant, operation, expected, kind, charges, evidenceAt);
        }, out value);

    internal static Hash256 ComputeReservationHash(CapacityReservationFactBodyV1 value) =>
        AuthorityIntegrityHashV1.Compute(ReservationSchemaId, Major, Minor, EncodeReservation(value));

    internal static Hash256 ComputeSettlementHash(CapacitySettlementFactBodyV1 value) =>
        AuthorityIntegrityHashV1.Compute(SettlementSchemaId, Major, Minor, EncodeSettlement(value));

    private static void WriteRequest(CborWriter writer, CapacityRequestV1 value)
    {
        writer.WriteStartMap(5);
        writer.WriteUInt64(1); WriteId(writer, value.OperationId.TryWriteBytes);
        writer.WriteUInt64(2); writer.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(value.Authority));
        writer.WriteUInt64(3); writer.WriteStartArray(value.Charges.Count);
        foreach (var charge in value.Charges) WriteCharge(writer, charge);
        writer.WriteEndArray();
        writer.WriteUInt64(4); writer.WriteEncodedValue(MonotonicStampV1Codec.Encode(value.Deadline));
        writer.WriteUInt64(5); writer.WriteUInt64((ushort)value.Priority);
        writer.WriteEndMap();
    }

    private static CapacityRequestV1 ReadRequest(CborReader reader)
    {
        RequireMap(reader, 5, 1);
        var operation = OperationId.FromValue(ReadStableId(reader));
        RequireTag(reader, 2);
        if (!AuthorityVectorCodecsV1.TryDecodeVector(reader.ReadEncodedValue(), out var authority))
            throw new CborContentException("Invalid capacity authority vector.");
        RequireTag(reader, 3);
        var count = reader.ReadStartArray();
        if (count is null or < 1 or > CapacityRequestV1.MaximumCharges)
            throw new CborContentException("Capacity charges must be a definite array of 1..256 items.");
        var charges = new CapacityChargeV1[count.Value];
        for (var index = 0; index < charges.Length; index++) charges[index] = ReadCharge(reader);
        EnsureStrictlySorted(charges, CapacityChargeComparerV1.Instance);
        reader.ReadEndArray();
        RequireTag(reader, 4);
        if (!MonotonicStampV1Codec.TryDecode(reader.ReadEncodedValue(), out var deadline))
            throw new CborContentException("Invalid capacity deadline.");
        RequireTag(reader, 5);
        var priority = checked((CapacityPriorityV1)reader.ReadUInt64());
        reader.ReadEndMap();
        return new CapacityRequestV1(operation, authority!, charges, deadline, priority);
    }

    private static void WriteCharge(CborWriter writer, CapacityChargeV1 value)
    {
        writer.WriteStartMap(5);
        writer.WriteUInt64(1); writer.WriteUInt64(value.DimensionId.Value);
        writer.WriteUInt64(2); writer.WriteEncodedValue(CapacityScopeCanonicalCodecV1.Encode(value.Scope));
        writer.WriteUInt64(3); writer.WriteInt64(value.Amount);
        writer.WriteUInt64(4); WriteId(writer, value.Purpose.TryWriteBytes);
        writer.WriteUInt64(5); WriteWindow(writer, value.Window);
        writer.WriteEndMap();
    }

    private static CapacityChargeV1 ReadCharge(CborReader reader)
    {
        RequireMap(reader, 5, 1);
        var dimension = new CapacityDimensionId(checked((ushort)reader.ReadUInt64()));
        RequireTag(reader, 2);
        if (!CapacityScopeCanonicalCodecV1.TryDecode(reader.ReadEncodedValue(), out var scope))
            throw new CborContentException("Invalid capacity scope.");
        RequireTag(reader, 3); var amount = reader.ReadInt64();
        RequireTag(reader, 4); var purpose = CapacityPurposeId.FromValue(ReadStableId(reader));
        RequireTag(reader, 5); var window = ReadWindow(reader);
        reader.ReadEndMap();
        return new CapacityChargeV1(dimension, scope!, amount, purpose, window);
    }

    private static void WriteSettlementCharge(CborWriter writer, CapacitySettlementChargeV1 value)
    {
        writer.WriteStartMap(4);
        writer.WriteUInt64(1); writer.WriteUInt64(value.DimensionId.Value);
        writer.WriteUInt64(2); writer.WriteEncodedValue(CapacityScopeCanonicalCodecV1.Encode(value.Scope));
        writer.WriteUInt64(3); WriteId(writer, value.Purpose.TryWriteBytes);
        writer.WriteUInt64(4); writer.WriteInt64(value.Amount);
        writer.WriteEndMap();
    }

    private static CapacitySettlementChargeV1 ReadSettlementCharge(CborReader reader)
    {
        RequireMap(reader, 4, 1);
        var dimension = new CapacityDimensionId(checked((ushort)reader.ReadUInt64()));
        RequireTag(reader, 2);
        if (!CapacityScopeCanonicalCodecV1.TryDecode(reader.ReadEncodedValue(), out var scope))
            throw new CborContentException("Invalid capacity settlement scope.");
        RequireTag(reader, 3); var purpose = CapacityPurposeId.FromValue(ReadStableId(reader));
        RequireTag(reader, 4); var amount = reader.ReadInt64();
        reader.ReadEndMap();
        return new CapacitySettlementChargeV1(dimension, scope!, purpose, amount);
    }

    private static void WriteExpiry(CborWriter writer, CapacityGrantExpiryV1 value)
    {
        writer.WriteStartMap(value is CapacityGrantExpiryV1.NoExpiry ? 1 : 2);
        writer.WriteUInt64(1); writer.WriteUInt64((ushort)value.Kind);
        if (value is CapacityGrantExpiryV1.At at)
        {
            writer.WriteUInt64(2); writer.WriteEncodedValue(MonotonicStampV1Codec.Encode(at.Value));
        }
        writer.WriteEndMap();
    }

    private static CapacityGrantExpiryV1 ReadExpiry(CborReader reader)
    {
        var count = reader.ReadStartMap();
        if (count is not (1 or 2) || reader.ReadUInt64() != 1) throw new CborContentException("Invalid grant expiry union.");
        var kind = checked((CapacityGrantExpiryKindV1)reader.ReadUInt64());
        CapacityGrantExpiryV1 result = kind switch
        {
            CapacityGrantExpiryKindV1.NoExpiry when count == 1 => new CapacityGrantExpiryV1.NoExpiry(),
            CapacityGrantExpiryKindV1.At when count == 2 && reader.ReadUInt64() == 2 &&
                MonotonicStampV1Codec.TryDecode(reader.ReadEncodedValue(), out var at) => new CapacityGrantExpiryV1.At(at),
            _ => throw new CborContentException("Invalid grant expiry arm."),
        };
        reader.ReadEndMap();
        return result;
    }

    private static void WriteWindow(CborWriter writer, CapacityChargeWindowV1 value)
    {
        writer.WriteStartMap(value is CapacityChargeWindowV1.NoWindow ? 1 : 2);
        writer.WriteUInt64(1); writer.WriteUInt64((ushort)value.Kind);
        if (value is CapacityChargeWindowV1.EndsAt endsAt)
        {
            writer.WriteUInt64(2); writer.WriteEncodedValue(MonotonicStampV1Codec.Encode(endsAt.Value));
        }
        writer.WriteEndMap();
    }

    private static CapacityChargeWindowV1 ReadWindow(CborReader reader)
    {
        var count = reader.ReadStartMap();
        if (count is not (1 or 2) || reader.ReadUInt64() != 1) throw new CborContentException("Invalid capacity charge window union.");
        var kind = checked((CapacityChargeWindowKindV1)reader.ReadUInt64());
        CapacityChargeWindowV1 result = kind switch
        {
            CapacityChargeWindowKindV1.NoWindow when count == 1 => new CapacityChargeWindowV1.NoWindow(),
            CapacityChargeWindowKindV1.EndsAt when count == 2 && reader.ReadUInt64() == 2 &&
                MonotonicStampV1Codec.TryDecode(reader.ReadEncodedValue(), out var at) => new CapacityChargeWindowV1.EndsAt(at),
            _ => throw new CborContentException("Invalid capacity charge window arm."),
        };
        reader.ReadEndMap();
        return result;
    }

    private static bool TryDecode<T>(ReadOnlyMemory<byte> encoded, Func<CborReader, T> read, out T? value) where T : class
    {
        value = null;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            value = read(reader);
            if (reader.BytesRemaining != 0) { value = null; return false; }
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException)
        {
            value = null;
            return false;
        }
    }

    private static CborWriter Writer() => new(CborConformanceMode.Ctap2Canonical);
    private static void RequireMap(CborReader reader, int count, ulong firstTag)
    {
        if (reader.ReadStartMap() != count || reader.ReadUInt64() != firstTag) throw new CborContentException("Invalid canonical capacity map.");
    }
    private static void RequireTag(CborReader reader, ulong tag)
    {
        if (reader.ReadUInt64() != tag) throw new CborContentException("Invalid canonical capacity tag.");
    }
    private static StableId128 ReadStableId(CborReader reader)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!reader.TryReadByteString(bytes, out var written) || written != 16)
            throw new CborContentException("A capacity identity is exactly 16 bytes.");
        return StableId128.FromBytes(bytes);
    }
    private static void EnsureStrictlySorted<T>(IReadOnlyList<T> values, IComparer<T> comparer)
    {
        for (var index = 1; index < values.Count; index++)
            if (comparer.Compare(values[index - 1], values[index]) >= 0)
                throw new CborContentException("Capacity charge arrays must be strictly sorted and duplicate-free.");
    }
    private static void WriteId(CborWriter writer, TryWriteId write)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!write(bytes)) throw new ArgumentException("A capacity identity is invalid.");
        writer.WriteByteString(bytes);
    }
    private delegate bool TryWriteId(Span<byte> destination);
}
