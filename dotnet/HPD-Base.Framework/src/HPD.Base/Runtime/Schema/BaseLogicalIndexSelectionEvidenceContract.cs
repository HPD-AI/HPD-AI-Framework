using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;

namespace HPD.Base;

/// <summary>Seals and validates canonical required logical-index selection evidence.</summary>
internal static class BaseLogicalIndexSelectionEvidenceContract
{
    private static readonly byte[] Purpose = "base.logicalIndex.selectionEvidence.v1\0"u8.ToArray();

    internal static BaseLogicalIndexSelectionEvidence Seal(BaseLogicalIndexSelectionEvidence value)
    {
        ArgumentNullException.ThrowIfNull(value);
        BaseLogicalIndexSelectionEvidence owned = Clone(value) with { Checksum = [] };
        byte[] encoding = Encode(owned);
        if (!ValidMembers(owned, encoding.LongLength))
            throw new InvalidOperationException(BaseSchemaErrorCodes.ProviderEvidenceInvalid);
        return owned with { Checksum = SHA256.HashData([.. Purpose, .. encoding]).ToImmutableArray() };
    }

    internal static bool Validate(BaseLogicalIndexSelectionEvidence value)
    {
        if (value is null || value.Checksum.Length != 32) return false;
        try
        {
            BaseLogicalIndexSelectionEvidence owned = Clone(value) with { Checksum = [] };
            byte[] encoding = Encode(owned);
            return ValidMembers(owned, encoding.LongLength)
                && CryptographicOperations.FixedTimeEquals(
                    value.Checksum.AsSpan(), SHA256.HashData([.. Purpose, .. encoding]));
        }
        catch (Exception exception) when (exception is OverflowException or ArgumentException
            or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    internal static BaseLogicalIndexSelectionEvidence Clone(BaseLogicalIndexSelectionEvidence value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value with
        {
            IndexId = BaseLogicalIndexId.Create(value.IndexId.ToString()),
            IndexChecksum = BaseLogicalIndexChecksum.Create(value.IndexChecksum.ToArray()),
            DirectoryPublicationChecksum = value.DirectoryPublicationChecksum.ToArray().ToImmutableArray(),
            MemberSetChecksum = value.MemberSetChecksum.ToArray().ToImmutableArray(),
            EqualityKeyChecksum = value.EqualityKeyChecksum.ToArray().ToImmutableArray(),
            MatchedPredicateChecksum = value.MatchedPredicateChecksum.ToArray().ToImmutableArray(),
            ReadInterval = value.ReadInterval with
            {
                LogicalAccessPathId = new string(value.ReadInterval.LogicalAccessPathId.AsSpan()),
                CanonicalLowerBound = value.ReadInterval.CanonicalLowerBound.ToArray().ToImmutableArray(),
                CanonicalUpperBound = value.ReadInterval.CanonicalUpperBound.ToArray().ToImmutableArray(),
            },
            Checksum = value.Checksum.ToArray().ToImmutableArray(),
        };
    }

    internal static byte[] Encode(BaseLogicalIndexSelectionEvidence value)
    {
        var writer = new ArrayBufferWriter<byte>();
        Text(writer, value.IndexId.ToString());
        I64(writer, value.IndexVersion);
        Bytes(writer, value.IndexChecksum.ToArray());
        I32(writer, (int)value.AccessShape);
        I64(writer, value.DirectoryGeneration);
        Bytes(writer, value.DirectoryPublicationChecksum.AsSpan());
        Bytes(writer, value.MemberSetChecksum.AsSpan());
        Bytes(writer, value.EqualityKeyChecksum.AsSpan());
        Bytes(writer, value.MatchedPredicateChecksum.AsSpan());
        Text(writer, value.ReadInterval.LogicalAccessPathId);
        Bytes(writer, value.ReadInterval.CanonicalLowerBound.AsSpan());
        writer.Write([value.ReadInterval.LowerInclusive ? (byte)1 : (byte)0]);
        Bytes(writer, value.ReadInterval.CanonicalUpperBound.AsSpan());
        writer.Write([value.ReadInterval.UpperInclusive ? (byte)1 : (byte)0]);
        I32(writer, value.ExaminedPostings);
        I32(writer, value.Candidates);
        I32(writer, value.Comparisons);
        I64(writer, value.EvidenceBytes);
        return writer.WrittenSpan.ToArray();
    }

    private static bool ValidMembers(BaseLogicalIndexSelectionEvidence value, long encodedBytes) =>
        value.IndexId.IsValid && value.IndexVersion > 0 && value.IndexChecksum.IsValid
        && value.AccessShape == BaseIndexAccessShape.LogicalIndexPoint
        && value.DirectoryGeneration > 0
        && value.DirectoryPublicationChecksum.Length == 32
        && value.MemberSetChecksum.Length == 32
        && value.EqualityKeyChecksum.Length == 32
        && value.MatchedPredicateChecksum.Length == 32
        && !string.IsNullOrWhiteSpace(value.ReadInterval.LogicalAccessPathId)
        && !value.ReadInterval.CanonicalLowerBound.IsDefault
        && !value.ReadInterval.CanonicalUpperBound.IsDefault
        && value.ReadInterval.LowerInclusive && value.ReadInterval.UpperInclusive
        && value.ReadInterval.CanonicalLowerBound.AsSpan().SequenceEqual(
            value.ReadInterval.CanonicalUpperBound.AsSpan())
        && value.ExaminedPostings >= 0 && value.Candidates >= 0 && value.Comparisons >= 0
        && value.EvidenceBytes == encodedBytes;

    private static void Text(ArrayBufferWriter<byte> writer, string value) =>
        Bytes(writer, BaseStrictUtf8.Encode(value));

    private static void Bytes(ArrayBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        I32(writer, value.Length);
        writer.Write(value);
    }

    private static void I32(ArrayBufferWriter<byte> writer, int value)
    {
        Span<byte> bytes = writer.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        writer.Advance(sizeof(int));
    }

    private static void I64(ArrayBufferWriter<byte> writer, long value)
    {
        Span<byte> bytes = writer.GetSpan(sizeof(long));
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        writer.Advance(sizeof(long));
    }
}
