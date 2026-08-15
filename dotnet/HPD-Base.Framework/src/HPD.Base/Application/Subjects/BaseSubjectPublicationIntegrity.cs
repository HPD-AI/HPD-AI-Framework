using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal static class BaseSubjectPublicationIntegrity
{
    internal static string Compute(
        string contractId,
        int contractVersion,
        string contractChecksum,
        long previousStateGeneration,
        long publishedStateGeneration,
        long restoreEpoch,
        BaseSubjectAuthorityPublicationKind kind,
        BaseMutationJournalPosition position,
        BaseSubjectAuthorityEpoch authorityEpoch)
    {
        var writer = new ArrayBufferWriter<byte>();
        Write(writer, "hpd.base.subject-publication.v1");
        Write(writer, contractId);
        Write(writer, contractVersion);
        Write(writer, contractChecksum);
        Write(writer, previousStateGeneration);
        Write(writer, publishedStateGeneration);
        Write(writer, restoreEpoch);
        Write(writer, (int)kind);
        Write(writer, position.Value);
        byte[] epoch = authorityEpoch.ToArray();
        Span<byte> epochLength = writer.GetSpan(4);
        BinaryPrimitives.WriteInt32BigEndian(epochLength, epoch.Length);
        writer.Advance(4);
        epoch.CopyTo(writer.GetSpan(epoch.Length));
        writer.Advance(epoch.Length);
        return Convert.ToHexStringLower(SHA256.HashData(writer.WrittenSpan));
    }

    private static void Write(ArrayBufferWriter<byte> writer, string value)
    {
        int count = Encoding.UTF8.GetByteCount(value);
        Span<byte> length = writer.GetSpan(4);
        BinaryPrimitives.WriteInt32BigEndian(length, count);
        writer.Advance(4);
        Encoding.UTF8.GetBytes(value, writer.GetSpan(count));
        writer.Advance(count);
    }

    private static void Write(ArrayBufferWriter<byte> writer, int value)
    {
        Span<byte> bytes = writer.GetSpan(4);
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        writer.Advance(4);
    }

    private static void Write(ArrayBufferWriter<byte> writer, long value)
    {
        Span<byte> bytes = writer.GetSpan(8);
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        writer.Advance(8);
    }
}
