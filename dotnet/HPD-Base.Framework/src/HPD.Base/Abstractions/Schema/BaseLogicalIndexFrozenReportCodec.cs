using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace HPD.Base;

internal static class BaseLogicalIndexFrozenReportCodec
{
    private static readonly byte[] Purpose =
        "base.logicalIndex.frozenCertificationReport.v2\0"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static ImmutableArray<byte> Encode(BaseLogicalIndexCertificationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!BaseLogicalIndexProviderContract.ValidateReport(report))
            throw new ArgumentException(
                "base.logicalIndex.certificationReportInvalid", nameof(report));
        using var stream = new MemoryStream(16_384);
        stream.Write(Purpose);
        WriteText(stream, report.ProviderId);
        WriteInt32(stream, report.ProviderVersion);
        WriteText(stream, report.StoreProviderKind);
        WriteInt32(stream, report.StoreProviderProtocolVersion);
        WriteFixed32(stream, report.ProductionCapabilityChecksum);
        WriteFixed32(stream, report.BoundedCertificationCapabilityChecksum);
        WriteInt32(stream, report.Cases.Length);
        foreach (BaseLogicalIndexCertificationCaseResult item in report.Cases)
        {
            WriteText(stream, item.Id);
            WriteInt32(stream, item.Ordinal);
            WriteInt32(stream, (int)item.ObservedStatus);
            stream.WriteByte(item.ObservedErrorCode is null ? (byte)0 : (byte)1);
            if (item.ObservedErrorCode is not null)
                WriteText(stream, item.ObservedErrorCode);
            WriteAccounting(stream, item.Accounting);
            WriteFixed32(stream, item.BeforeMemberSetChecksum);
            WriteFixed32(stream, item.AfterMemberSetChecksum);
            WriteFixed32(stream, item.BeforePublicationChecksum);
            WriteFixed32(stream, item.AfterPublicationChecksum);
            WriteFixed32(stream, item.EvidenceChecksum);
        }
        WriteFixed32(stream, report.ContractChecksum);
        WriteFixed32(stream, report.Checksum);
        return stream.ToArray().ToImmutableArray();
    }

    internal static BaseLogicalIndexCertificationReport Decode(ReadOnlySpan<byte> bytes)
    {
        var reader = new Reader(bytes);
        reader.Require(Purpose);
        string providerId = reader.ReadText();
        int providerVersion = reader.ReadInt32();
        string kind = reader.ReadText();
        int protocol = reader.ReadInt32();
        ImmutableArray<byte> production = reader.ReadFixed32();
        ImmutableArray<byte> bounded = reader.ReadFixed32();
        int count = reader.ReadInt32();
        if (count != BaseLogicalIndexProviderContract.CaseIds.Length)
            throw Invalid();
        var cases = ImmutableArray.CreateBuilder<BaseLogicalIndexCertificationCaseResult>(count);
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            string id = reader.ReadText();
            int encodedOrdinal = reader.ReadInt32();
            OperationStatus status = (OperationStatus)reader.ReadInt32();
            byte presence = reader.ReadByte();
            if (presence > 1)
                throw Invalid();
            string? error = presence == 1 ? reader.ReadText() : null;
            cases.Add(new BaseLogicalIndexCertificationCaseResult
            {
                Id = id,
                Ordinal = encodedOrdinal,
                ObservedStatus = status,
                ObservedErrorCode = error,
                Accounting = reader.ReadAccounting(),
                BeforeMemberSetChecksum = reader.ReadFixed32(),
                AfterMemberSetChecksum = reader.ReadFixed32(),
                BeforePublicationChecksum = reader.ReadFixed32(),
                AfterPublicationChecksum = reader.ReadFixed32(),
                EvidenceChecksum = reader.ReadFixed32(),
            });
        }
        var report = new BaseLogicalIndexCertificationReport
        {
            ProviderId = providerId,
            ProviderVersion = providerVersion,
            StoreProviderKind = kind,
            StoreProviderProtocolVersion = protocol,
            ProductionCapabilityChecksum = production,
            BoundedCertificationCapabilityChecksum = bounded,
            Cases = cases.MoveToImmutable(),
            ContractChecksum = reader.ReadFixed32(),
            Checksum = reader.ReadFixed32(),
        };
        if (!reader.AtEnd || !BaseLogicalIndexProviderContract.ValidateReport(report))
            throw Invalid();
        return report;
    }

    private static void WriteAccounting(
        Stream stream, BaseLogicalIndexCertificationAccounting value)
    {
        WriteInt64(stream, value.Records);
        WriteInt64(stream, value.PredicateEvaluations);
        WriteInt64(stream, value.Keys);
        WriteInt64(stream, value.KeyBytes);
        WriteInt64(stream, value.PostingKeys);
        WriteInt64(stream, value.Postings);
        WriteInt64(stream, value.ComparatorEntries);
        WriteInt64(stream, value.Comparisons);
        WriteInt64(stream, value.EvidenceBytes);
        WriteInt64(stream, value.RetainedDirectoryBytes);
        WriteInt64(stream, value.TransientBytes);
    }

    private static void WriteText(Stream stream, string value)
    {
        byte[] bytes = StrictUtf8.GetBytes(value);
        WriteInt32(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteFixed32(Stream stream, ImmutableArray<byte> value)
    {
        if (value.Length != 32)
            throw Invalid();
        stream.Write(value.AsSpan());
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static InvalidOperationException Invalid() => new(
        "base.logicalIndex.certificationReportInvalid");

    private ref struct Reader(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        private int _offset;

        internal bool AtEnd => _offset == _bytes.Length;

        internal void Require(ReadOnlySpan<byte> expected)
        {
            if (!Take(expected.Length).SequenceEqual(expected))
                throw Invalid();
        }

        internal byte ReadByte() => Take(1)[0];

        internal int ReadInt32() => BinaryPrimitives.ReadInt32BigEndian(Take(4));

        internal long ReadInt64() => BinaryPrimitives.ReadInt64BigEndian(Take(8));

        internal string ReadText()
        {
            int length = ReadInt32();
            if (length < 0 || length > 4_096)
                throw Invalid();
            try
            {
                return StrictUtf8.GetString(Take(length));
            }
            catch (DecoderFallbackException)
            {
                throw Invalid();
            }
        }

        internal ImmutableArray<byte> ReadFixed32() => Take(32).ToArray().ToImmutableArray();

        internal BaseLogicalIndexCertificationAccounting ReadAccounting() => new()
        {
            Records = ReadInt64(),
            PredicateEvaluations = ReadInt64(),
            Keys = ReadInt64(),
            KeyBytes = ReadInt64(),
            PostingKeys = ReadInt64(),
            Postings = ReadInt64(),
            ComparatorEntries = ReadInt64(),
            Comparisons = ReadInt64(),
            EvidenceBytes = ReadInt64(),
            RetainedDirectoryBytes = ReadInt64(),
            TransientBytes = ReadInt64(),
        };

        private ReadOnlySpan<byte> Take(int length)
        {
            if (length < 0 || _offset > _bytes.Length - length)
                throw Invalid();
            ReadOnlySpan<byte> value = _bytes.Slice(_offset, length);
            _offset += length;
            return value;
        }
    }
}
