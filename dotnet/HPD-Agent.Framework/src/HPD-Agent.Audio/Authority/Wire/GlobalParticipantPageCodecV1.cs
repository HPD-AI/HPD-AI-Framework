using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal static class GlobalParticipantPageCodecV1
{
    internal const string SchemaId = "hpd.global-participant-page.v1";
    private static readonly byte[] PageHashDomain = Encoding.UTF8.GetBytes("hpd-s1-global-participant-page-hash-v1\0");
    private static readonly byte[] AbsentLeafDomain = Encoding.UTF8.GetBytes("hpd-s1-global-participant-absent-leaf-v1\0");
    private static readonly byte[] NodeDomain = Encoding.UTF8.GetBytes("hpd-s1-global-participant-owner-node-v1\0");

    internal static readonly Hash256 DefaultIndexRoot = ComputeDefaultIndexRoot();

    internal static byte[] Encode(GlobalParticipantPageV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var writer = Writer();
        writer.WriteStartMap(10);
        writer.WriteUInt64(1); WriteId(writer, value.JournalId);
        writer.WriteUInt64(2); WriteOptionalHead(writer, value.PinnedHead);
        writer.WriteUInt64(3); WriteHash(writer, value.IndexRoot);
        writer.WriteUInt64(4); writer.WriteUInt64(value.PageOrdinal);
        writer.WriteUInt64(5); WriteOptionalHash(writer, value.PreviousPageHash);
        writer.WriteUInt64(6); writer.WriteByteString(value.RecordsBytes);
        writer.WriteUInt64(7); writer.WriteUInt64(value.IsFinal);
        writer.WriteUInt64(8); writer.WriteUInt64(value.TotalPages);
        writer.WriteUInt64(9); writer.WriteUInt64(value.TotalRecords);
        writer.WriteUInt64(10); writer.WriteUInt64(value.TotalCanonicalBytes);
        writer.WriteEndMap();
        var encoded = writer.Encode();
        if (encoded.Length > GlobalParticipantPageV1.MaximumCanonicalBytes)
            throw new ArgumentException("The canonical page exceeds 65,536 bytes.", nameof(value));
        return encoded;
    }

    internal static bool TryDecode(ReadOnlyMemory<byte> encoded, out GlobalParticipantPageV1? value)
    {
        value = null;
        if (encoded.Length is 0 or > GlobalParticipantPageV1.MaximumCanonicalBytes)
            return false;
        try
        {
            var reader = Reader(encoded);
            RequireMap(reader, 10, 1);
            var journalId = ReadId(reader);
            RequireTag(reader, 2); var pinnedHead = ReadOptionalHead(reader);
            RequireTag(reader, 3); var indexRoot = ReadHash(reader);
            RequireTag(reader, 4); var pageOrdinal = ReadPositiveUInt16(reader, GlobalParticipantPageV1.MaximumPages);
            RequireTag(reader, 5); var previousPageHash = ReadOptionalHash(reader);
            RequireTag(reader, 6); var records = ReadBoundedByteString(reader, encoded.Span, GlobalParticipantPageV1.MaximumCanonicalBytes, allowEmpty: false);
            RequireTag(reader, 7); var isFinal = ReadUInt16(reader);
            RequireTag(reader, 8); var totalPages = ReadPositiveUInt16(reader, GlobalParticipantPageV1.MaximumPages);
            RequireTag(reader, 9); var totalRecords = reader.ReadUInt64();
            RequireTag(reader, 10); var totalCanonicalBytes = reader.ReadUInt64();
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0)
                return false;
            var candidate = new GlobalParticipantPageV1(journalId, pinnedHead, indexRoot, pageOrdinal,
                previousPageHash, records, isFinal, totalPages, totalRecords, totalCanonicalBytes);
            if (!Encode(candidate).AsSpan().SequenceEqual(encoded.Span))
                return false;
            value = candidate;
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or
                                          ArgumentException or OverflowException)
        {
            return false;
        }
    }

    internal static Hash256 ComputePageHash(GlobalParticipantPageV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var pinnedHead = EncodeOptionalHead(value.PinnedHead);
        var previousPageHash = EncodeOptionalHash(value.PreviousPageHash);
        var length = checked(PageHashDomain.Length + 16 + 4 + pinnedHead.Length + 32 + 2 + 4 +
            previousPageHash.Length + 1 + 2 + 8 + 8 + 4 + value.RecordsBytes.Length);
        var preimage = new byte[length];
        var offset = 0;
        Append(PageHashDomain, preimage, ref offset);
        WriteId(value.JournalId, preimage.AsSpan(offset, 16)); offset += 16;
        BinaryPrimitives.WriteUInt32BigEndian(preimage.AsSpan(offset, 4), checked((uint)pinnedHead.Length)); offset += 4;
        Append(pinnedHead, preimage, ref offset);
        WriteHash(value.IndexRoot, preimage.AsSpan(offset, 32)); offset += 32;
        BinaryPrimitives.WriteUInt16BigEndian(preimage.AsSpan(offset, 2), value.PageOrdinal); offset += 2;
        BinaryPrimitives.WriteUInt32BigEndian(preimage.AsSpan(offset, 4), checked((uint)previousPageHash.Length)); offset += 4;
        Append(previousPageHash, preimage, ref offset);
        preimage[offset++] = checked((byte)value.IsFinal);
        BinaryPrimitives.WriteUInt16BigEndian(preimage.AsSpan(offset, 2), value.TotalPages); offset += 2;
        BinaryPrimitives.WriteUInt64BigEndian(preimage.AsSpan(offset, 8), value.TotalRecords); offset += 8;
        BinaryPrimitives.WriteUInt64BigEndian(preimage.AsSpan(offset, 8), value.TotalCanonicalBytes); offset += 8;
        BinaryPrimitives.WriteUInt32BigEndian(preimage.AsSpan(offset, 4), checked((uint)value.RecordsBytes.Length)); offset += 4;
        value.RecordsBytes.CopyTo(preimage.AsSpan(offset));
        return Hash256.FromBytes(SHA256.HashData(preimage));
    }

    internal static Hash256 ComputeHash(GlobalParticipantPageV1 value) =>
        AuthorityIntegrityHashV1.Compute(SchemaId, 1, 0, Encode(value));

    internal static byte[] EncodeRecordsField(IReadOnlyList<ReadOnlyMemory<byte>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count > GlobalParticipantPageV1.MaximumRecordsPerPage)
            throw new ArgumentOutOfRangeException(nameof(records));
        var length = 2;
        foreach (var record in records)
        {
            ValidateCanonicalRecord(record.Span);
            length = checked(length + 4 + record.Length);
            if (length > GlobalParticipantPageV1.MaximumCanonicalBytes)
                throw new ArgumentException("The framed records field exceeds 65,536 bytes.", nameof(records));
        }
        var encoded = new byte[length];
        BinaryPrimitives.WriteUInt16BigEndian(encoded, checked((ushort)records.Count));
        var offset = 2;
        foreach (var record in records)
        {
            BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(offset, 4), checked((uint)record.Length)); offset += 4;
            record.Span.CopyTo(encoded.AsSpan(offset)); offset += record.Length;
        }
        return encoded;
    }

    internal static bool TryDecodeRecordsField(ReadOnlyMemory<byte> encoded, out IReadOnlyList<ReadOnlyMemory<byte>> records)
    {
        records = Array.Empty<ReadOnlyMemory<byte>>();
        try
        {
            var summary = InspectRecordsField(encoded.Span);
            var result = new ReadOnlyMemory<byte>[summary.Count];
            var offset = 2;
            for (var index = 0; index < result.Length; index++)
            {
                var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(encoded.Span.Slice(offset, 4))); offset += 4;
                result[index] = encoded.Slice(offset, length).ToArray(); offset += length;
            }
            records = Array.AsReadOnly(result);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            return false;
        }
    }

    internal static RecordsFieldSummary InspectRecordsField(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length is < 2 or > GlobalParticipantPageV1.MaximumCanonicalBytes)
            throw new ArgumentException("A records field must contain its UInt16 count framing.", nameof(encoded));
        var count = BinaryPrimitives.ReadUInt16BigEndian(encoded);
        if (count > GlobalParticipantPageV1.MaximumRecordsPerPage)
            throw new ArgumentException("A records field contains too many records.", nameof(encoded));
        var offset = 2;
        ulong canonicalBytes = 0;
        for (var index = 0; index < count; index++)
        {
            if (encoded.Length - offset < 4)
                throw new ArgumentException("A record length prefix is truncated.", nameof(encoded));
            var declared = BinaryPrimitives.ReadUInt32BigEndian(encoded.Slice(offset, 4)); offset += 4;
            if (declared is 0 or > GlobalParticipantPageV1.MaximumRecordBytes || declared > (uint)(encoded.Length - offset))
                throw new ArgumentException("A record length is outside the frozen bound or truncated.", nameof(encoded));
            var length = checked((int)declared);
            ValidateCanonicalRecord(encoded.Slice(offset, length));
            canonicalBytes = checked(canonicalBytes + declared);
            offset += length;
        }
        if (offset != encoded.Length)
            throw new ArgumentException("The records field contains trailing or uncounted bytes.", nameof(encoded));
        return new RecordsFieldSummary(count, canonicalBytes);
    }

    private static void ValidateCanonicalRecord(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length is 0 or > GlobalParticipantPageV1.MaximumRecordBytes)
            throw new ArgumentException("A canonical outer record must contain 1 through 8,192 bytes.", nameof(encoded));
        try
        {
            var copy = encoded.ToArray();
            if (!GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(copy, out var outer) || !GlobalParticipantAllocatorCodecsV1.Encode(outer!).AsSpan().SequenceEqual(encoded))
                throw new ArgumentException("A framed record must be one exact canonical global participant claim record.", nameof(encoded));
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException)
        {
            throw new ArgumentException("A framed record must be one exact canonical CBOR value.", nameof(encoded), exception);
        }
    }

    private static Hash256 ComputeDefaultIndexRoot()
    {
        var current = SHA256.HashData(AbsentLeafDomain);
        for (var depth = 1; depth <= 128; depth++)
        {
            var preimage = new byte[NodeDomain.Length + 1 + 64];
            NodeDomain.CopyTo(preimage, 0);
            preimage[NodeDomain.Length] = checked((byte)depth);
            current.CopyTo(preimage, NodeDomain.Length + 1);
            current.CopyTo(preimage, NodeDomain.Length + 33);
            current = SHA256.HashData(preimage);
        }
        return Hash256.FromBytes(current);
    }

    private static void WriteOptionalHead(CborWriter writer, GlobalParticipantAuthorityHeadV1? value)
    {
        writer.WriteStartMap(value is null ? 1 : 2);
        writer.WriteUInt64(1); writer.WriteUInt64(value is null ? 0UL : 1UL);
        if (value is { } head) { writer.WriteUInt64(2); WriteHead(writer, head); }
        writer.WriteEndMap();
    }

    private static GlobalParticipantAuthorityHeadV1? ReadOptionalHead(CborReader reader)
    {
        var count = reader.ReadStartMap();
        RequireTag(reader, 1); var kind = reader.ReadUInt64();
        if (count == 1 && kind == 0) { reader.ReadEndMap(); return null; }
        if (count != 2 || kind != 1) throw Invalid();
        RequireTag(reader, 2); var value = ReadHead(reader); reader.ReadEndMap(); return value;
    }

    private static byte[] EncodeOptionalHead(GlobalParticipantAuthorityHeadV1? value)
    {
        var writer = Writer(); WriteOptionalHead(writer, value); return writer.Encode();
    }

    private static void WriteHead(CborWriter writer, GlobalParticipantAuthorityHeadV1 value)
    {
        writer.WriteStartMap(2);
        writer.WriteUInt64(1); WritePosition(writer, value.Position);
        writer.WriteUInt64(2); WriteHash(writer, value.RecordHash);
        writer.WriteEndMap();
    }

    private static GlobalParticipantAuthorityHeadV1 ReadHead(CborReader reader)
    {
        RequireMap(reader, 2, 1); var position = ReadPosition(reader);
        RequireTag(reader, 2); var hash = ReadHash(reader); reader.ReadEndMap();
        return new GlobalParticipantAuthorityHeadV1(position, hash);
    }

    private static void WritePosition(CborWriter writer, GlobalParticipantAuthorityPositionV1 value)
    {
        if (!value.IsValid) throw Invalid();
        writer.WriteStartMap(2);
        writer.WriteUInt64(1); WriteId(writer, value.JournalId);
        writer.WriteUInt64(2); writer.WriteUInt64(value.Sequence);
        writer.WriteEndMap();
    }

    private static GlobalParticipantAuthorityPositionV1 ReadPosition(CborReader reader)
    {
        RequireMap(reader, 2, 1); var journalId = ReadId(reader);
        RequireTag(reader, 2); var sequence = reader.ReadUInt64(); reader.ReadEndMap();
        return new GlobalParticipantAuthorityPositionV1(journalId, sequence);
    }

    private static void WriteOptionalHash(CborWriter writer, Hash256? value)
    {
        writer.WriteStartMap(value is null ? 1 : 2);
        writer.WriteUInt64(1); writer.WriteUInt64(value is null ? 0UL : 1UL);
        if (value is { } hash) { writer.WriteUInt64(2); WriteHash(writer, hash); }
        writer.WriteEndMap();
    }

    private static Hash256? ReadOptionalHash(CborReader reader)
    {
        var count = reader.ReadStartMap();
        RequireTag(reader, 1); var kind = reader.ReadUInt64();
        if (count == 1 && kind == 0) { reader.ReadEndMap(); return null; }
        if (count != 2 || kind != 1) throw Invalid();
        RequireTag(reader, 2); var value = ReadHash(reader); reader.ReadEndMap(); return value;
    }

    private static byte[] EncodeOptionalHash(Hash256? value)
    {
        var writer = Writer(); WriteOptionalHash(writer, value); return writer.Encode();
    }

    private static void WriteId(CborWriter writer, GlobalParticipantAllocatorJournalId value)
    {
        Span<byte> bytes = stackalloc byte[16]; WriteId(value, bytes); writer.WriteByteString(bytes);
    }

    private static void WriteId(GlobalParticipantAllocatorJournalId value, Span<byte> destination)
    {
        if (!value.TryWriteBytes(destination)) throw Invalid();
    }

    private static GlobalParticipantAllocatorJournalId ReadId(CborReader reader)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!reader.TryReadByteString(bytes, out var written) || written != 16 || bytes.IndexOfAnyExcept((byte)0) < 0)
            throw Invalid();
        return GlobalParticipantAllocatorJournalId.FromValue(StableId128.FromBytes(bytes));
    }

    private static void WriteHash(CborWriter writer, Hash256 value)
    {
        Span<byte> bytes = stackalloc byte[32]; WriteHash(value, bytes); writer.WriteByteString(bytes);
    }

    private static void WriteHash(Hash256 value, Span<byte> destination)
    {
        if (!value.TryWriteBytes(destination)) throw Invalid();
    }

    private static Hash256 ReadHash(CborReader reader)
    {
        Span<byte> bytes = stackalloc byte[32];
        if (!reader.TryReadByteString(bytes, out var written) || written != 32) throw Invalid();
        return Hash256.FromBytes(bytes);
    }

    private static byte[] ReadBoundedByteString(CborReader reader, ReadOnlySpan<byte> source, int maximum, bool allowEmpty)
    {
        var declared = ReadDeclaredByteStringLength(source, source.Length - reader.BytesRemaining);
        if (declared < (allowEmpty ? 0 : 1) || declared > maximum) throw Invalid();
        var bytes = new byte[declared];
        if (!reader.TryReadByteString(bytes, out var written) || written != declared) throw Invalid();
        return bytes;
    }

    private static int ReadDeclaredByteStringLength(ReadOnlySpan<byte> source, int offset)
    {
        if ((uint)offset >= (uint)source.Length || (source[offset] >> 5) != 2) throw Invalid();
        var span = source[offset..];
        var additional = span[0] & 31;
        ulong length;
        switch (additional)
        {
            case < 24: length = (ulong)additional; break;
            case 24 when span.Length >= 2: length = span[1]; break;
            case 25 when span.Length >= 3: length = BinaryPrimitives.ReadUInt16BigEndian(span[1..]); break;
            case 26 when span.Length >= 5: length = BinaryPrimitives.ReadUInt32BigEndian(span[1..]); break;
            case 27 when span.Length >= 9: length = BinaryPrimitives.ReadUInt64BigEndian(span[1..]); break;
            default: throw Invalid();
        }
        if (length > int.MaxValue) throw Invalid();
        return checked((int)length);
    }

    private static CborWriter Writer() => new(CborConformanceMode.Ctap2Canonical);
    private static CborReader Reader(ReadOnlyMemory<byte> encoded) => new(encoded, CborConformanceMode.Ctap2Canonical, false);
    private static void RequireMap(CborReader reader, int count, ulong firstTag)
    { if (reader.ReadStartMap() != count || reader.ReadUInt64() != firstTag) throw Invalid(); }
    private static void RequireTag(CborReader reader, ulong tag) { if (reader.ReadUInt64() != tag) throw Invalid(); }
    private static ushort ReadUInt16(CborReader reader)
    { var value = reader.ReadUInt64(); if (value > ushort.MaxValue) throw Invalid(); return (ushort)value; }
    private static ushort ReadPositiveUInt16(CborReader reader, int maximum)
    { var value = reader.ReadUInt64(); if (value is 0 || value > (ulong)maximum) throw Invalid(); return (ushort)value; }
    private static CborContentException Invalid() => new("Invalid canonical global participant page.");
    private static void Append(ReadOnlySpan<byte> source, Span<byte> destination, ref int offset)
    { source.CopyTo(destination[offset..]); offset += source.Length; }

    internal readonly record struct RecordsFieldSummary(ushort Count, ulong CanonicalRecordBytes);
}
