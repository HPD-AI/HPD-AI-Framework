using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Defines the deployment-bound certification receipt for durable-activation providers.</summary>
public static class BaseActivationCertificationReceiptContract
{
    /// <summary>Gets the exact provider certification protocol.</summary>
    public const string ProtocolVersion = "hpd.base.activation.certification.v1";

    /// <summary>Gets the checksum of this certification contract.</summary>
    public static ImmutableArray<byte> ContractChecksum { get; } = ImmutableArray.Create(
        SHA256.HashData("HPDB-ACTIVATION-CERTIFICATION-CONTRACT-1\0hpd.base.activation.certification.v1"u8));

    /// <summary>Computes canonical capability bytes as a SHA-256 checksum.</summary>
    public static ImmutableArray<byte> CapabilityChecksum(BaseActivationProviderCapability value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var stream = new MemoryStream();
        stream.Write("HPDB-ACTIVATION-CAPABILITY-1\0"u8);
        Bool(stream, value.AtomicCreationSupported); Bool(stream, value.SelectionTargetSupported);
        Bool(stream, value.ModuleTargetSupported); Bool(stream, value.GuardedChildrenSupported);
        Bool(stream, value.DurableYieldSupported);
        Bool(stream, value.RestoreFencingSupported); I64(stream, (long)value.DueInvalidation);
        Sequence(stream, value.ScheduleKinds.Select(static item => (long)item));
        Sequence(stream, value.ExecutionClasses.Select(static item => (long)item));
        Sequence(stream, value.BackupModes.Select(static item => (long)item));
        Sequence(stream, value.RestoreModes.Select(static item => (long)item));
        long[] numbers =
        [
            value.MaximumActivationsPerTransaction, value.MaximumDueCandidates,
            value.MaximumReadIntervals, value.MaximumIndexOperations, value.MaximumInputBytes,
            value.MaximumResultBytes, value.MaximumEvidenceBytes, value.MaximumTransientBytes,
            value.MaximumReceiptBytes, value.MaximumPendingRows, value.MaximumClaimedRows,
            value.MaximumTerminalRows, value.MaximumAttempts, value.MaximumYieldsPerActivation,
            value.MaximumReservedYieldReceiptSlots, value.MaximumRenewalsPerSlice,
            value.MaximumChildrenPerSlice, value.MaximumLineageDepth, value.MaximumOccurrencePage,
            value.MaximumPriorityAgingBoost, value.PriorityAgingInterval.Ticks,
            value.ObservationTokenLifetime.Ticks, value.MaximumTimeZoneBytes, value.MaximumHandlerDependencies,
            value.AcquisitionDeadline.Ticks, value.TransactionDeadline.Ticks,
            value.ObservationWaitDeadline.Ticks, value.RenewalDeadline.Ticks,
            value.CommitObservationDeadline.Ticks, value.ReceiptResolutionDeadline.Ticks,
            value.MaintenanceDeadline.Ticks, value.ShutdownDrainDeadline.Ticks,
            value.ProviderQuarantineSlots, value.HandlerQuarantineSlots,
        ];
        foreach (long number in numbers) I64(stream, number);
        Bytes(stream, value.CanonicalChecksum);
        return ImmutableArray.Create(SHA256.HashData(stream.ToArray()));
    }

    /// <summary>Creates a purpose-bound provider certification receipt.</summary>
    public static ImmutableArray<byte> Create(
        string providerId,
        string providerVersion,
        int protocolVersion,
        BaseActivationProviderCapability capability,
        ImmutableArray<string> nativeDependencies,
        ImmutableArray<byte> reportChecksum)
    {
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(providerVersion)
            || protocolVersion != 2 || reportChecksum.Length != 32 || nativeDependencies.IsDefault
            || !nativeDependencies.SequenceEqual(nativeDependencies.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || nativeDependencies.Distinct(StringComparer.Ordinal).Count() != nativeDependencies.Length
            || nativeDependencies.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("base.activation.providerContractInvalid");
        using var stream = new MemoryStream();
        stream.Write("HPDB-ACTIVATION-CERTIFICATION-RECEIPT-1\0"u8);
        String(stream, ProtocolVersion); String(stream, providerId); String(stream, providerVersion);
        I64(stream, protocolVersion); Bytes(stream, CapabilityChecksum(capability));
        U32(stream, nativeDependencies.Length);
        foreach (string dependency in nativeDependencies) String(stream, dependency);
        Bytes(stream, ContractChecksum); Bytes(stream, reportChecksum);
        return ImmutableArray.Create(SHA256.HashData(stream.ToArray()));
    }

    /// <summary>Creates a descriptor from one frozen successful conformance report.</summary>
    public static BaseActivationProviderDescriptor FromSuccessfulReport(
        string providerId,
        string providerVersion,
        BaseActivationProviderCapability capability,
        ImmutableArray<byte> successfulReportChecksum,
        params string[] nativeDependencies)
    {
        if (successfulReportChecksum.Length != 32)
            throw new ArgumentException("base.activation.providerContractInvalid", nameof(successfulReportChecksum));
        ImmutableArray<string> dependencies = nativeDependencies.Order(StringComparer.Ordinal).ToImmutableArray();
        return new BaseActivationProviderDescriptor
        {
            ProviderId = providerId, ProviderVersion = providerVersion, ProtocolVersion = 2,
            Capability = capability, NativeDependencyReceipts = dependencies,
            CertificationContractChecksum = ContractChecksum, CertificationReportChecksum = successfulReportChecksum,
            CertificationReceipt = Create(providerId, providerVersion, 2, capability, dependencies, successfulReportChecksum),
        };
    }

    /// <summary>Validates every authority component of an installed provider descriptor.</summary>
    public static bool Validate(BaseActivationProviderDescriptor descriptor)
    {
        try
        {
            return BaseActivationCapabilityContract.IsValid(descriptor.Capability)
                && descriptor.CertificationContractChecksum.AsSpan().SequenceEqual(ContractChecksum.AsSpan())
                && descriptor.CertificationReceipt.AsSpan().SequenceEqual(Create(
                    descriptor.ProviderId, descriptor.ProviderVersion, descriptor.ProtocolVersion,
                    descriptor.Capability, descriptor.NativeDependencyReceipts,
                    descriptor.CertificationReportChecksum).AsSpan());
        }
        catch { return false; }
    }

    private static void Bool(Stream stream, bool value) => stream.WriteByte(value ? (byte)1 : (byte)0);
    private static void Sequence(Stream stream, IEnumerable<long> values)
    {
        long[] materialized = values.ToArray(); U32(stream, materialized.Length);
        foreach (long value in materialized) I64(stream, value);
    }
    private static void I64(Stream stream, long value) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); stream.Write(bytes); }
    private static void U32(Stream stream, int value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)value)); stream.Write(bytes); }
    private static void Bytes(Stream stream, ImmutableArray<byte> value) { U32(stream, value.Length); stream.Write(value.AsSpan()); }
    private static void String(Stream stream, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); U32(stream, bytes.Length); stream.Write(bytes); }
}
