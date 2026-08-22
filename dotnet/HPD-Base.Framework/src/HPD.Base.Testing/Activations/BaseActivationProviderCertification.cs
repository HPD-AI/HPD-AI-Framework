using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base.Testing;

/// <summary>Runs one provider-neutral durable-activation certification case.</summary>
public interface IBaseActivationCertificationFixture : IAsyncDisposable
{
    /// <summary>Gets the immutable descriptor of the provider under test.</summary>
    BaseActivationProviderDescriptor Descriptor { get; }

    /// <summary>Executes one exact mandatory case in an isolated store and returns bounded evidence.</summary>
    ValueTask<BaseActivationCertificationCaseResult> ExecuteAsync(
        BaseActivationCertificationCaseRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Requests one exact mandatory durable-activation certification case.</summary>
public sealed record BaseActivationCertificationCaseRequest
{
    /// <summary>Gets the stable case identity.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the zero-based execution ordinal.</summary>
    public required int Ordinal { get; init; }
    /// <summary>Gets the finite case deadline.</summary>
    public required DateTimeOffset DeadlineUtc { get; init; }
}

/// <summary>Returns bounded evidence for one certification case.</summary>
public sealed record BaseActivationCertificationCaseResult
{
    /// <summary>Gets the stable case identity.</summary>
    public required string Id { get; init; }
    /// <summary>Gets whether every assertion passed.</summary>
    public required bool Passed { get; init; }
    /// <summary>Gets the stable status produced by the final assertion.</summary>
    public required OperationStatus Status { get; init; }
    /// <summary>Gets a safe stable error code when the case failed.</summary>
    public string? ErrorCode { get; init; }
    /// <summary>Gets the SHA-256 checksum of the canonical bounded observation log.</summary>
    public required ImmutableArray<byte> EvidenceChecksum { get; init; }
}

/// <summary>Contains the complete purpose-bound durable-activation certification report.</summary>
public sealed record BaseActivationCertificationReport
{
    /// <summary>Gets the certification protocol.</summary>
    public required string ProtocolVersion { get; init; }
    /// <summary>Gets the provider identity.</summary>
    public required string ProviderId { get; init; }
    /// <summary>Gets the provider version.</summary>
    public required string ProviderVersion { get; init; }
    /// <summary>Gets whether every mandatory case passed.</summary>
    public required bool Passed { get; init; }
    /// <summary>Gets results in canonical mandatory-case order.</summary>
    public required ImmutableArray<BaseActivationCertificationCaseResult> Cases { get; init; }
    /// <summary>Gets the capability checksum.</summary>
    public required ImmutableArray<byte> CapabilityChecksum { get; init; }
    /// <summary>Gets the certification-contract checksum.</summary>
    public required ImmutableArray<byte> ContractChecksum { get; init; }
    /// <summary>Gets the canonical successful-report checksum.</summary>
    public required ImmutableArray<byte> ReportChecksum { get; init; }
    /// <summary>Gets the deployment-bound certification receipt.</summary>
    public required ImmutableArray<byte> CertificationReceipt { get; init; }
}

/// <summary>Executes the complete durable-activation provider certification matrix.</summary>
public static class BaseActivationProviderCertification
{
    /// <summary>Gets all mandatory cases in canonical execution order.</summary>
    public static ImmutableArray<string> MandatoryCases { get; } =
    [
        "definition-target-handler-schedule-state-attempt-receipt-unions",
        "canonical-vectors-generated-manual-parity",
        "atomic-create-ordinary-selection-module",
        "transactional-target-commit-rollback-duplicate-conflict-indeterminate",
        "renew-child-complete-races",
        "lost-wakeup-observe-wait",
        "protected-scope-no-foreign-scan",
        "cron-calendar-timezone-gap-overlap",
        "clock-misfire-overlap-priority-starvation",
        "cancel-complete-effect-dispose-races",
        "restore-in-place-and-disaster-domain",
        "handler-version-migration-backup-dependency",
        "lifecycle-retirement-guarded-choreography",
        "noncooperative-provider-handler-quarantine",
        "capability-accounting-exact-and-plus-one",
        "backup-corruption-interrupted-restore",
        "pruning-removal-retained-receipts",
        "inmemory-sqlite-semantic-parity",
        "native-aot-transactional-worker",
        "node-worker-client",
        "browser-worker-omission",
        "graph-schedule-suspend-resume-restore-cancel",
    ];

    /// <summary>Runs every mandatory case and returns a receipt only for a complete successful report.</summary>
    public static async ValueTask<BaseActivationCertificationReport> RunAsync(
        IBaseActivationCertificationFixture fixture,
        TimeSpan caseTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        if (caseTimeout <= TimeSpan.Zero || caseTimeout > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(caseTimeout));
        BaseActivationProviderDescriptor descriptor = fixture.Descriptor;
        if (!BaseActivationCapabilityContract.IsValid(descriptor.Capability))
            throw new InvalidOperationException("base.activation.capabilityUnavailable");
        var cases = ImmutableArray.CreateBuilder<BaseActivationCertificationCaseResult>(MandatoryCases.Length);
        for (int ordinal = 0; ordinal < MandatoryCases.Length; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string id = MandatoryCases[ordinal];
            BaseActivationCertificationCaseResult result = await fixture.ExecuteAsync(new()
            {
                Id = id, Ordinal = ordinal, DeadlineUtc = DateTimeOffset.UtcNow.Add(caseTimeout),
            }, cancellationToken).ConfigureAwait(false);
            if (result.Id != id || result.EvidenceChecksum.Length != 32
                || result.Passed != result.Status.IsSuccess()
                || result.Passed && result.ErrorCode is not null
                || !result.Passed && string.IsNullOrWhiteSpace(result.ErrorCode))
                throw new InvalidOperationException("base.activation.providerContractInvalid");
            cases.Add(Clone(result));
        }
        bool passed = cases.All(static item => item.Passed);
        ImmutableArray<byte> capability = BaseActivationCertificationReceiptContract.CapabilityChecksum(descriptor.Capability);
        ImmutableArray<byte> report = ReportChecksum(descriptor, cases.ToImmutable(), passed, capability);
        ImmutableArray<byte> receipt = passed
            ? BaseActivationCertificationReceiptContract.Create(descriptor.ProviderId, descriptor.ProviderVersion,
                descriptor.ProtocolVersion, descriptor.Capability, descriptor.NativeDependencyReceipts, report)
            : [];
        return new BaseActivationCertificationReport
        {
            ProtocolVersion = BaseActivationCertificationReceiptContract.ProtocolVersion,
            ProviderId = new string(descriptor.ProviderId.AsSpan()), ProviderVersion = new string(descriptor.ProviderVersion.AsSpan()),
            Passed = passed, Cases = cases.MoveToImmutable(), CapabilityChecksum = capability,
            ContractChecksum = BaseActivationCertificationReceiptContract.ContractChecksum,
            ReportChecksum = report, CertificationReceipt = receipt,
        };
    }

    private static ImmutableArray<byte> ReportChecksum(
        BaseActivationProviderDescriptor descriptor,
        ImmutableArray<BaseActivationCertificationCaseResult> cases,
        bool passed,
        ImmutableArray<byte> capability)
    {
        using var stream = new MemoryStream();
        Write(stream, "HPDB-ACTIVATION-CERTIFICATION-REPORT-1\0");
        Write(stream, descriptor.ProviderId); Write(stream, descriptor.ProviderVersion);
        Write(stream, BaseActivationCertificationReceiptContract.ProtocolVersion);
        stream.WriteByte(passed ? (byte)1 : (byte)0); Write(stream, capability.AsSpan());
        Write(stream, BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(cases.Length)));
        foreach (BaseActivationCertificationCaseResult item in cases)
        {
            Write(stream, item.Id); stream.WriteByte(item.Passed ? (byte)1 : (byte)0);
            Write(stream, BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder((int)item.Status)));
            Write(stream, item.ErrorCode ?? string.Empty); Write(stream, item.EvidenceChecksum.AsSpan());
        }
        return SHA256.HashData(stream.ToArray()).ToImmutableArray();
    }

    private static BaseActivationCertificationCaseResult Clone(BaseActivationCertificationCaseResult value) => value with
    { Id = new string(value.Id.AsSpan()), ErrorCode = value.ErrorCode is null ? null : new string(value.ErrorCode.AsSpan()), EvidenceChecksum = value.EvidenceChecksum.ToArray().ToImmutableArray() };
    private static void Write(Stream stream, string value) => Write(stream, Encoding.UTF8.GetBytes(value));
    private static void Write(Stream stream, ReadOnlySpan<byte> value)
    { Span<byte> count = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(count, checked((uint)value.Length)); stream.Write(count); stream.Write(value); }
}
