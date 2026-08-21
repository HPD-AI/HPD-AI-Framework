using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Defines the exact deployment-bound certification receipt for lexical providers.</summary>
public static class BaseTextCertificationReceiptContract
{
    /// <summary>Gets the exact certification protocol version.</summary>
    public const string ProtocolVersion = "hpd.base.text.certification.v1";
    /// <summary>Gets the checksum of the certification contract.</summary>
    public static ImmutableArray<byte> ContractChecksum { get; } = ImmutableArray.Create(SHA256.HashData(Encoding.ASCII.GetBytes("HPDB-TEXT-CERTIFICATION-CONTRACT-1\0" + ProtocolVersion)));

    /// <summary>Computes the canonical checksum of a provider capability.</summary>
    public static ImmutableArray<byte> CapabilityChecksum(BaseTextProviderCapability value)
    {
        ArgumentNullException.ThrowIfNull(value); using var stream = new MemoryStream(); stream.Write("HPDB-TEXT-CAPABILITY-1\0"u8);
        I64(stream, (long)value.ProviderClass); Bool(stream, value.TransactionalMaintenanceSupported); Bool(stream, value.ExactRevisionHydrationSupported); Bool(stream, value.PolicyBeforeRankingSupported); Bool(stream, value.ExactFixedPointScoreSupported);
        long[] numbers = [value.MaximumIndexesPerCollection,value.MaximumFieldsPerIndex,value.MaximumFilterFields,value.MaximumQueryNodes,value.MaximumQueryDepth,value.MaximumPhraseTerms,value.MaximumQueryBytes,value.MaximumFilterNodes,value.MaximumFilterDepth,value.MaximumFilterLiterals,value.MaximumInValues,value.MaximumPrefixExpansions,value.MaximumPrefixExpansionBytes,value.MaximumSecondaryOrderFields,value.MaximumOrderingBytes,value.MaximumCandidates,value.MaximumScoreProofBytes,value.MaximumTokensPerRecord,value.MaximumNormalizedBytesPerField,value.MaximumNormalizedBytesPerRecord,value.MaximumIndexedRecords,value.MaximumPostings,value.MaximumStatisticsBytes,value.MaximumResults,value.MaximumResultBytes,value.MaximumCursorBytes,value.MaximumStatementParameters,value.MaximumRebuildStagingRows,value.MaximumRebuildBytes,value.MaximumTransientBytes,value.MaximumWriteTime.Ticks,value.MaximumQueryTime.Ticks,value.MaximumConsistencyWait.Ticks,value.MaximumInspectionTime.Ticks,value.MaximumRebuildTime.Ticks,value.MaximumQuarantinedOperations];
        foreach (long number in numbers) I64(stream, number); return ImmutableArray.Create(SHA256.HashData(stream.ToArray()));
    }

    /// <summary>Creates a deployment-bound certification receipt.</summary>
    public static ImmutableArray<byte> Create(string providerId, int providerVersion, BaseTextProviderClass providerClass, BaseTextProviderCapability capability, ImmutableArray<string> nativeDependencies, ImmutableArray<byte> reportChecksum)
    {
        if (string.IsNullOrWhiteSpace(providerId) || providerVersion < 1 || reportChecksum.Length != 32 || nativeDependencies.IsDefault || !nativeDependencies.SequenceEqual(nativeDependencies.Order(StringComparer.Ordinal), StringComparer.Ordinal) || nativeDependencies.Distinct(StringComparer.Ordinal).Count() != nativeDependencies.Length) throw new ArgumentException(BaseTextErrorCodes.ContractInvalid);
        using var stream = new MemoryStream(); stream.Write("HPDB-TEXT-CERTIFICATION-RECEIPT-1\0"u8); String(stream, ProtocolVersion); String(stream, providerId); I64(stream, providerVersion); I64(stream, (long)providerClass); Bytes(stream, CapabilityChecksum(capability)); U32(stream, nativeDependencies.Length); foreach (string dependency in nativeDependencies) String(stream, dependency); Bytes(stream, ContractChecksum); Bytes(stream, reportChecksum); return ImmutableArray.Create(SHA256.HashData(stream.ToArray()));
    }

    /// <summary>Validates every authority component of an installed provider descriptor.</summary>
    public static bool Validate(BaseTextProviderDescriptor descriptor)
    {
        try { return descriptor.CertificationContractChecksum.AsSpan().SequenceEqual(ContractChecksum.AsSpan()) && descriptor.CertificationReceipt.AsSpan().SequenceEqual(Create(descriptor.Id, descriptor.Version, descriptor.ProviderClass, descriptor.Capability, descriptor.NativeDependencyReceipts, descriptor.CertificationReportChecksum).AsSpan()); }
        catch { return false; }
    }

    private static void Bool(Stream stream, bool value) => stream.WriteByte(value ? (byte)1 : (byte)0);
    private static void I64(Stream stream, long value) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); stream.Write(bytes); }
    private static void U32(Stream stream, int value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)value)); stream.Write(bytes); }
    private static void Bytes(Stream stream, ImmutableArray<byte> value) { U32(stream, value.Length); stream.Write(value.AsSpan()); }
    private static void String(Stream stream, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); U32(stream, bytes.Length); stream.Write(bytes); }
}
