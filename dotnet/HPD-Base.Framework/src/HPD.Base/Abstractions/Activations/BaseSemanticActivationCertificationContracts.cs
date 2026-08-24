using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Declares the immutable certification authority selected with one store bundle.</summary>
public sealed record BaseSemanticActivationCertificationProfile
{
    /// <summary>Gets whether this is a successful supported-provider profile.</summary>
    public required bool Supported { get; init; }
    /// <summary>Gets the stable semantic-provider identity.</summary>
    public required string ProviderId { get; init; }
    /// <summary>Gets the semantic-provider implementation version.</summary>
    public required string ProviderVersion { get; init; }
    /// <summary>Gets the exact owning store-provider kind.</summary>
    public required string StoreProviderKind { get; init; }
    /// <summary>Gets the exact owning store-provider protocol.</summary>
    public required int StoreProviderProtocolVersion { get; init; }
    /// <summary>Gets sorted native dependency receipts exercised by certification.</summary>
    public required ImmutableArray<string> NativeDependencyReceipts { get; init; }
    /// <summary>Gets the semantic capability checksum exercised by certification.</summary>
    public required ImmutableArray<byte> SemanticCapabilityChecksum { get; init; }
    /// <summary>Gets the composed L50 capability checksum exercised by certification.</summary>
    public required ImmutableArray<byte> ModuleMutationCapabilityChecksum { get; init; }
    /// <summary>Gets the declared L51 capability checksum exercised by certification.</summary>
    public required ImmutableArray<byte> ActivationCapabilityChecksum { get; init; }
    /// <summary>Gets the exact certification-contract checksum.</summary>
    public required ImmutableArray<byte> ContractChecksum { get; init; }
    /// <summary>Gets the frozen executed-report checksum.</summary>
    public required ImmutableArray<byte> ExecutedReportChecksum { get; init; }
    /// <summary>Gets the purpose-bound profile checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Declares the immutable provider subject exercised before a profile exists.</summary>
public sealed record BaseSemanticActivationCertificationSubject
{
    /// <summary>Gets the stable semantic-provider identity.</summary>
    public required string ProviderId { get; init; }
    /// <summary>Gets the semantic-provider implementation version.</summary>
    public required string ProviderVersion { get; init; }
    /// <summary>Gets the owning store-provider kind.</summary>
    public required string StoreProviderKind { get; init; }
    /// <summary>Gets the owning store-provider protocol.</summary>
    public required int StoreProviderProtocolVersion { get; init; }
    /// <summary>Gets sorted native dependency receipts.</summary>
    public required ImmutableArray<string> NativeDependencyReceipts { get; init; }
    /// <summary>Gets the exact semantic capability checksum.</summary>
    public required ImmutableArray<byte> SemanticCapabilityChecksum { get; init; }
    /// <summary>Gets the exact L50 capability checksum.</summary>
    public required ImmutableArray<byte> ModuleMutationCapabilityChecksum { get; init; }
    /// <summary>Gets the exact L51 capability checksum.</summary>
    public required ImmutableArray<byte> ActivationCapabilityChecksum { get; init; }
}

/// <summary>Classifies whether one certification case executed or was not advertised.</summary>
public enum BaseSemanticActivationCertificationApplicability
{
    /// <summary>The provider advertised and executed the case.</summary>
    Executed = 1,
    /// <summary>The provider did not advertise the optional capability.</summary>
    NotAdvertised = 2,
}

/// <summary>Contains host-owned bounded evidence for one certification case.</summary>
public sealed record BaseSemanticActivationCertificationCaseResult
{
    /// <summary>Gets the stable case ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets its zero-based registry ordinal.</summary>
    public required int Ordinal { get; init; }
    /// <summary>Gets whether the case executed.</summary>
    public required BaseSemanticActivationCertificationApplicability Applicability { get; init; }
    /// <summary>Gets the host verdict.</summary>
    public required OperationStatus Status { get; init; }
    /// <summary>Gets the host verdict error when failed.</summary>
    public string? ErrorCode { get; init; }
    /// <summary>Gets the actual provider operation status.</summary>
    public required OperationStatus ObservedStatus { get; init; }
    /// <summary>Gets the actual provider error code.</summary>
    public string? ObservedErrorCode { get; init; }
    /// <summary>Gets the provider atomic outcome when the case used the atomic boundary.</summary>
    public RecordMutationExecutionOutcome? AtomicOutcome { get; init; }
    /// <summary>Gets dedicated receipt-resolution disposition.</summary>
    public required BaseAtomicReceiptResolutionDisposition ReceiptResolution { get; init; }
    /// <summary>Gets committed-versus-duplicate request disposition when applicable.</summary>
    public BaseMutationRequestDisposition? RequestDisposition { get; init; }
    /// <summary>Gets the exact durable receipt checksum when applicable.</summary>
    public required ImmutableArray<byte> ReceiptChecksum { get; init; }
    /// <summary>Gets the positive fixture observation sequence.</summary>
    public required long ObservationSequence { get; init; }
    /// <summary>Gets the purpose-bound case evidence checksum.</summary>
    public required ImmutableArray<byte> EvidenceChecksum { get; init; }
}

/// <summary>Contains structured provider state observed after one certification case.</summary>
public sealed record BaseSemanticActivationCertificationObservation
{
    /// <summary>Gets the positive checked observation sequence.</summary>
    public required long Sequence { get; init; }
    /// <summary>Gets canonical bounded provider evidence.</summary>
    public required ImmutableArray<byte> Evidence { get; init; }
    /// <summary>Gets the retained live-slot count.</summary>
    public required long LiveSlots { get; init; }
    /// <summary>Gets the retained retirement-tombstone count.</summary>
    public required long RetiredSlots { get; init; }
    /// <summary>Gets the retained permanent-absence count.</summary>
    public required long AbsenceMarkers { get; init; }
    /// <summary>Gets the durable activation count.</summary>
    public required long Activations { get; init; }
    /// <summary>Gets the durable outer-receipt count.</summary>
    public required long Receipts { get; init; }
    /// <summary>Gets active provider work after observation.</summary>
    public required int ActiveWork { get; init; }
    /// <summary>Gets quarantined retained work after observation.</summary>
    public required int QuarantinedWork { get; init; }
    /// <summary>Gets explicitly released retained-work completions.</summary>
    public required int ReleasedWork { get; init; }
    /// <summary>Gets late completions prevented from publishing.</summary>
    public required int RejectedLateCompletions { get; init; }
    /// <summary>Gets whether exact-limit execution succeeded.</summary>
    public required bool ExactLimitAccepted { get; init; }
    /// <summary>Gets whether max-plus-one execution failed before commit.</summary>
    public required bool MaxPlusOneRejected { get; init; }
    /// <summary>Gets whether recovery-floor dominance was verified.</summary>
    public required bool RecoveryFloorVerified { get; init; }
    /// <summary>Gets whether response loss resolved the exact historical receipt.</summary>
    public required bool ReceiptResolved { get; init; }
    /// <summary>Gets the exact semantic authority checksum before replay/resolution.</summary>
    public required ImmutableArray<byte> AuthorityBeforeChecksum { get; init; }
    /// <summary>Gets the exact semantic authority checksum after replay/resolution.</summary>
    public required ImmutableArray<byte> AuthorityAfterChecksum { get; init; }
}

/// <summary>Contains the complete executed L53 provider report before profile sealing.</summary>
public sealed record BaseSemanticActivationCertificationReport
{
    /// <summary>Gets the exact uncertified subject that was exercised.</summary>
    public required BaseSemanticActivationCertificationSubject Subject { get; init; }
    /// <summary>Gets whether every applicable case passed.</summary>
    public required bool Passed { get; init; }
    /// <summary>Gets every case in exact contract order.</summary>
    public required ImmutableArray<BaseSemanticActivationCertificationCaseResult> Cases { get; init; }
    /// <summary>Gets the certification-contract checksum.</summary>
    public required ImmutableArray<byte> ContractChecksum { get; init; }
    /// <summary>Gets the canonical report checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Binds an installed semantic provider to the exact production L51 provider it composes.</summary>
public sealed record BaseInstalledSemanticActivationProviderDescriptor
{
    /// <summary>Gets the exact selected certification profile.</summary>
    public required BaseSemanticActivationCertificationProfile Profile { get; init; }
    /// <summary>Gets the exact installed L51 provider certification receipt.</summary>
    public required ImmutableArray<byte> InstalledActivationCertificationReceipt { get; init; }
    /// <summary>Gets the exact selected logical store identity.</summary>
    public required string LogicalStoreId { get; init; }
    /// <summary>Gets the exact selected physical store-instance identity.</summary>
    public required string StoreInstanceId { get; init; }
    /// <summary>Gets the checksum of the exact selected store registration set.</summary>
    public required ImmutableArray<byte> StoreRegistrationSetChecksum { get; init; }
    /// <summary>Gets the deployment-bound semantic certification receipt.</summary>
    public required ImmutableArray<byte> Receipt { get; init; }
}

/// <summary>Owns the canonical L53 provider-certification profile and installed receipt.</summary>
public static class BaseSemanticActivationCertificationContract
{
    /// <summary>Gets the certification protocol identity.</summary>
    public const string ProtocolVersion = "hpd.base.semanticActivation.certification.v2";

    /// <summary>Gets the exact ordered mandatory certification-case registry.</summary>
    public static ImmutableArray<string> MandatoryCaseIds { get; } =
    [
        "atomic-missing-ensure", "different-parent-race", "existing-replay", "terminal-retirement",
        "receipt-resolution", "hostile-capture", "hostile-prepare", "hostile-apply", "accounting-limits",
        "inspection", "maintenance-authority", "maintenance", "backup-restore", "recovery-floor", "noncooperative-release",
        "fault-ResponseLossAfterCommit", "fault-IndeterminateCommit", "fault-NonCooperativeCapture",
        "fault-NonCooperativePrepare", "fault-NonCooperativeApply", "fault-NonCooperativeReceipt",
        "fault-NonCooperativeMaintenance", "fault-NonCooperativeRestore", "fault-SubstituteKey",
        "fault-SubstituteScopeBinding", "fault-SubstituteSeekDigest", "fault-SubstituteSlotGeneration",
        "fault-SubstituteActivation", "fault-SubstituteDueAuthority", "fault-CorruptInterval",
        "fault-CorruptAccounting", "fault-CorruptRetirement", "fault-CorruptAbsence",
        "fault-CorruptRecoveryEntry", "fault-InterruptMaintenancePublication",
        "fault-InterruptRestorePublication", "fault-RetentionOvertake",
    ];

    /// <summary>Gets the contract checksum binding the ordered mandatory-case registry and report grammar.</summary>
    public static ImmutableArray<byte> ContractChecksum { get; } = ComputeContractChecksum();

    /// <summary>Creates the uncertified subject exercised by the certification host.</summary>
    public static BaseSemanticActivationCertificationSubject CreateSubject(
        string providerId, string providerVersion, string storeProviderKind, int storeProviderProtocolVersion,
        BaseSemanticActivationCapability semanticCapability, BaseModuleMutationCapability moduleCapability,
        BaseActivationProviderCapability activationCapability, params string[] nativeDependencyReceipts)
    {
        if (!semanticCapability.Supported)
            throw new ArgumentException("base.semanticActivation.certificationInvalid");
        ImmutableArray<string> dependencies = nativeDependencyReceipts.Order(StringComparer.Ordinal).ToImmutableArray();
        var value = new BaseSemanticActivationCertificationSubject
        {
            ProviderId = providerId, ProviderVersion = providerVersion, StoreProviderKind = storeProviderKind,
            StoreProviderProtocolVersion = storeProviderProtocolVersion, NativeDependencyReceipts = dependencies,
            SemanticCapabilityChecksum = BaseSemanticActivationCapabilityContract.Checksum(semanticCapability),
            ModuleMutationCapabilityChecksum = ModuleMutationCapabilityChecksum(moduleCapability),
            ActivationCapabilityChecksum = BaseActivationCertificationReceiptContract.CapabilityChecksum(activationCapability),
        };
        if (!ValidateSubject(value)) throw new ArgumentException("base.semanticActivation.certificationInvalid");
        return value;
    }

    /// <summary>Seals a supported profile only from a complete successful executed report.</summary>
    public static BaseSemanticActivationCertificationProfile SealSuccessfulReport(
        BaseSemanticActivationCertificationReport report, BaseSemanticActivationCapability semanticCapability,
        BaseModuleMutationCapability moduleCapability, BaseActivationProviderCapability activationCapability)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!ValidateReport(report)
            || !Fixed(report.Subject.SemanticCapabilityChecksum, BaseSemanticActivationCapabilityContract.Checksum(semanticCapability))
            || !Fixed(report.Subject.ModuleMutationCapabilityChecksum, ModuleMutationCapabilityChecksum(moduleCapability))
            || !Fixed(report.Subject.ActivationCapabilityChecksum, BaseActivationCertificationReceiptContract.CapabilityChecksum(activationCapability)))
            throw new ArgumentException("base.semanticActivation.certificationInvalid");
        for (int i = 0; i < report.Cases.Length; i++)
            if ((report.Cases[i].Applicability == BaseSemanticActivationCertificationApplicability.Executed)
                != CaseIsAdvertised(report.Cases[i].Id, semanticCapability))
                throw new ArgumentException("base.semanticActivation.certificationInvalid");
        var value = new BaseSemanticActivationCertificationProfile
        {
            Supported = true, ProviderId = report.Subject.ProviderId, ProviderVersion = report.Subject.ProviderVersion,
            StoreProviderKind = report.Subject.StoreProviderKind, StoreProviderProtocolVersion = report.Subject.StoreProviderProtocolVersion,
            NativeDependencyReceipts = report.Subject.NativeDependencyReceipts, SemanticCapabilityChecksum = report.Subject.SemanticCapabilityChecksum,
            ModuleMutationCapabilityChecksum = report.Subject.ModuleMutationCapabilityChecksum,
            ActivationCapabilityChecksum = report.Subject.ActivationCapabilityChecksum, ContractChecksum = report.ContractChecksum,
            ExecutedReportChecksum = report.Checksum, Checksum = [],
        };
        return value with { Checksum = ProfileChecksum(value) };
    }

    /// <summary>Creates the purpose-distinct zero-applicable profile for an unsupported semantic provider.</summary>
    public static BaseSemanticActivationCertificationProfile Unsupported(string storeProviderKind,
        int storeProviderProtocolVersion, BaseModuleMutationCapability moduleCapability,
        BaseActivationProviderCapability activationCapability)
    {
        BaseSemanticActivationCapability semantic = BaseSemanticActivationCapabilityContract.Unsupported();
        var value = new BaseSemanticActivationCertificationProfile
        {
            Supported = false, ProviderId = "base.semanticActivation.unsupported", ProviderVersion = "1",
            StoreProviderKind = storeProviderKind, StoreProviderProtocolVersion = storeProviderProtocolVersion,
            NativeDependencyReceipts = [], SemanticCapabilityChecksum = semantic.Checksum,
            ModuleMutationCapabilityChecksum = ModuleMutationCapabilityChecksum(moduleCapability),
            ActivationCapabilityChecksum = BaseActivationCertificationReceiptContract.CapabilityChecksum(activationCapability),
            ContractChecksum = ContractChecksum, ExecutedReportChecksum = ImmutableArray.Create(new byte[32]), Checksum = [],
        };
        return value with { Checksum = ProfileChecksum(value) };
    }

    /// <summary>Creates the installed receipt after the production L51 provider instance exists.</summary>
    public static BaseInstalledSemanticActivationProviderDescriptor BindInstalled(
        BaseSemanticActivationCertificationProfile profile, BaseActivationProviderDescriptor activation,
        string logicalStoreId, string storeInstanceId, ImmutableArray<byte> storeRegistrationSetChecksum)
    {
        if (!ValidateProfile(profile) || !BaseActivationCertificationReceiptContract.Validate(activation)
            || !Fixed(profile.ActivationCapabilityChecksum,
                BaseActivationCertificationReceiptContract.CapabilityChecksum(activation.Capability)))
            throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
        if (string.IsNullOrWhiteSpace(logicalStoreId) || string.IsNullOrWhiteSpace(storeInstanceId)
            || storeRegistrationSetChecksum.Length != 32) throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
        var value = new BaseInstalledSemanticActivationProviderDescriptor
        {
            Profile = Clone(profile), InstalledActivationCertificationReceipt = activation.CertificationReceipt.ToArray().ToImmutableArray(), Receipt = [],
            LogicalStoreId = new string(logicalStoreId.AsSpan()), StoreInstanceId = new string(storeInstanceId.AsSpan()),
            StoreRegistrationSetChecksum = storeRegistrationSetChecksum.ToArray().ToImmutableArray(),
        };
        return value with { Receipt = profile.Supported ? InstalledChecksum(value) : [] };
    }

    /// <summary>Validates an installed descriptor against the selected profile and exact L51 provider.</summary>
    public static bool ValidateInstalled(BaseInstalledSemanticActivationProviderDescriptor value,
        BaseSemanticActivationCertificationProfile selected, BaseActivationProviderDescriptor activation,
        string logicalStoreId, string storeInstanceId, ImmutableArray<byte> storeRegistrationSetChecksum) =>
        ValidateProfile(value.Profile) && Fixed(value.Profile.Checksum, selected.Checksum)
        && BaseActivationCertificationReceiptContract.Validate(activation)
        && Fixed(value.Profile.ActivationCapabilityChecksum,
            BaseActivationCertificationReceiptContract.CapabilityChecksum(activation.Capability))
        && Fixed(value.InstalledActivationCertificationReceipt, activation.CertificationReceipt)
        && string.Equals(value.LogicalStoreId, logicalStoreId, StringComparison.Ordinal)
        && string.Equals(value.StoreInstanceId, storeInstanceId, StringComparison.Ordinal)
        && Fixed(value.StoreRegistrationSetChecksum, storeRegistrationSetChecksum)
        && (value.Profile.Supported ? Fixed(value.Receipt, InstalledChecksum(value)) : value.Receipt.IsEmpty);

    /// <summary>Rebinds an installed descriptor to a newly installed physical store identity.</summary>
    public static BaseInstalledSemanticActivationProviderDescriptor RebindInstalled(
        BaseInstalledSemanticActivationProviderDescriptor value, string storeInstanceId)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!ValidateProfile(value.Profile) || !value.Profile.Supported || value.Receipt.Length != 32
            || string.IsNullOrWhiteSpace(storeInstanceId))
            throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
        BaseInstalledSemanticActivationProviderDescriptor rebound = value with
        {
            Profile = Clone(value.Profile),
            InstalledActivationCertificationReceipt = value.InstalledActivationCertificationReceipt.ToArray().ToImmutableArray(),
            LogicalStoreId = new string(value.LogicalStoreId.AsSpan()),
            StoreInstanceId = new string(storeInstanceId.AsSpan()),
            StoreRegistrationSetChecksum = value.StoreRegistrationSetChecksum.ToArray().ToImmutableArray(),
            Receipt = [],
        };
        return rebound with { Receipt = InstalledChecksum(rebound) };
    }

    /// <summary>Validates a selected immutable profile.</summary>
    public static bool ValidateProfile(BaseSemanticActivationCertificationProfile? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.ProviderId) || string.IsNullOrWhiteSpace(value.ProviderVersion)
            || string.IsNullOrWhiteSpace(value.StoreProviderKind) || value.StoreProviderProtocolVersion < 1
            || value.NativeDependencyReceipts.IsDefault
            || !value.NativeDependencyReceipts.SequenceEqual(value.NativeDependencyReceipts.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || value.NativeDependencyReceipts.Distinct(StringComparer.Ordinal).Count() != value.NativeDependencyReceipts.Length
            || value.NativeDependencyReceipts.Any(string.IsNullOrWhiteSpace)
            || value.SemanticCapabilityChecksum.Length != 32 || value.ModuleMutationCapabilityChecksum.Length != 32
            || value.ActivationCapabilityChecksum.Length != 32 || value.ExecutedReportChecksum.Length != 32
            || value.ContractChecksum.Length != 32 || value.Checksum.Length != 32
            || !Fixed(value.ContractChecksum, ContractChecksum)
            || value.Supported && value.ExecutedReportChecksum.All(static item => item == 0)
            || !value.Supported && (value.ProviderId != "base.semanticActivation.unsupported"
                || !value.NativeDependencyReceipts.IsEmpty || value.ExecutedReportChecksum.Any(static item => item != 0))) return false;
        return Fixed(value.Checksum, ProfileChecksum(value));
    }

    /// <summary>Creates and checksums one executed report from host-owned case results.</summary>
    public static BaseSemanticActivationCertificationReport CreateReport(
        BaseSemanticActivationCertificationSubject subject,
        ImmutableArray<BaseSemanticActivationCertificationCaseResult> cases)
    {
        if (!ValidateSubject(subject) || cases.IsDefault)
            throw new ArgumentException("base.semanticActivation.certificationInvalid");
        bool passed = cases.All(static item => item.Applicability == BaseSemanticActivationCertificationApplicability.NotAdvertised
            || item.Status == OperationStatus.Ok);
        var value = new BaseSemanticActivationCertificationReport
        {
            Subject = CloneSubject(subject), Passed = passed,
            Cases = cases.Select(CloneCase).ToImmutableArray(), ContractChecksum = ContractChecksum, Checksum = [],
        };
        return value with { Checksum = ReportChecksum(value) };
    }

    /// <summary>Computes one frozen case checksum after host validation of the complete structured observation.</summary>
    public static ImmutableArray<byte> CaseEvidenceChecksum(
        string id, int ordinal, BaseSemanticActivationCertificationApplicability applicability,
        OperationStatus verdict, string? verdictError, OperationStatus observedStatus, string? observedError,
        RecordMutationExecutionOutcome? atomicOutcome, BaseAtomicReceiptResolutionDisposition receiptResolution,
        BaseMutationRequestDisposition? requestDisposition, ImmutableArray<byte> receiptChecksum,
        BaseSemanticActivationCertificationObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        using var stream = new MemoryStream(); Text(stream, "base.semanticActivation.certificationCase.v2\0");
        Text(stream, id); I32(stream, ordinal); I32(stream, (int)applicability); I32(stream, (int)verdict);
        Text(stream, verdictError ?? string.Empty); I32(stream, (int)observedStatus); Text(stream, observedError ?? string.Empty);
        I32(stream, atomicOutcome is null ? -1 : (int)atomicOutcome.Value); I32(stream, (int)receiptResolution);
        I32(stream, requestDisposition is null ? -1 : (int)requestDisposition.Value); Bytes(stream, receiptChecksum.AsSpan());
        I64(stream, observation.Sequence); I64(stream, observation.LiveSlots); I64(stream, observation.RetiredSlots);
        I64(stream, observation.AbsenceMarkers); I64(stream, observation.Activations); I64(stream, observation.Receipts);
        I32(stream, observation.ActiveWork); I32(stream, observation.QuarantinedWork); I32(stream, observation.ReleasedWork);
        I32(stream, observation.RejectedLateCompletions); stream.WriteByte(observation.ExactLimitAccepted ? (byte)1 : (byte)0);
        stream.WriteByte(observation.MaxPlusOneRejected ? (byte)1 : (byte)0); stream.WriteByte(observation.RecoveryFloorVerified ? (byte)1 : (byte)0);
        stream.WriteByte(observation.ReceiptResolved ? (byte)1 : (byte)0); Bytes(stream, observation.AuthorityBeforeChecksum.AsSpan());
        Bytes(stream, observation.AuthorityAfterChecksum.AsSpan()); Bytes(stream, observation.Evidence.AsSpan());
        return SHA256.HashData(stream.ToArray()).ToImmutableArray();
    }

    /// <summary>Canonicalizes one validated provider-opaque executed evidence value for a frozen report.</summary>
    public static ImmutableArray<byte> CanonicalExecutedEvidence(string caseId, string evidenceKind)
    {
        if (string.IsNullOrWhiteSpace(caseId) || string.IsNullOrWhiteSpace(evidenceKind))
            throw new ArgumentException("base.semanticActivation.certificationInvalid");
        using var stream = new MemoryStream();
        Text(stream, "base.semanticActivation.canonicalExecutedEvidence.v1\0");
        Text(stream, caseId); Text(stream, evidenceKind);
        return SHA256.HashData(stream.ToArray()).ToImmutableArray();
    }

    /// <summary>Validates a complete executed report and its exact ordered case registry.</summary>
    public static bool ValidateReport(BaseSemanticActivationCertificationReport? value)
    {
        if (value is null || !value.Passed || !ValidateSubject(value.Subject)
            || !Fixed(value.ContractChecksum, ContractChecksum) || value.Checksum.Length != 32
            || value.Cases.IsDefault || value.Cases.Length != MandatoryCaseIds.Length
            || !value.Cases.Select(static item => item.Id).SequenceEqual(MandatoryCaseIds, StringComparer.Ordinal)) return false;
        for (int i = 0; i < value.Cases.Length; i++)
        {
            BaseSemanticActivationCertificationCaseResult item = value.Cases[i];
            if (item.Ordinal != i || item.ObservationSequence <= 0 || item.EvidenceChecksum.Length != 32
                || !Enum.IsDefined(item.Applicability) || !Enum.IsDefined(item.Status) || !Enum.IsDefined(item.ObservedStatus)
                || item.Applicability == BaseSemanticActivationCertificationApplicability.Executed && item.Status != OperationStatus.Ok
                || item.Applicability == BaseSemanticActivationCertificationApplicability.NotAdvertised
                    && (item.Status != OperationStatus.Ok || item.ObservedStatus != OperationStatus.Unsupported)
                || item.Status == OperationStatus.Ok && item.ErrorCode is not null || item.ReceiptChecksum.IsDefault
                || !ObservedOutcomeMatches(item.Id, item.Applicability, item.ObservedStatus, item.ObservedErrorCode)) return false;
            if (!ExpectedReceiptOutcome(item)) return false;
        }
        return Fixed(value.Checksum, ReportChecksum(value));
    }

    /// <summary>Validates one uncertified provider subject.</summary>
    public static bool ValidateSubject(BaseSemanticActivationCertificationSubject? value) => value is not null
        && !string.IsNullOrWhiteSpace(value.ProviderId) && !string.IsNullOrWhiteSpace(value.ProviderVersion)
        && !string.IsNullOrWhiteSpace(value.StoreProviderKind) && value.StoreProviderProtocolVersion > 0
        && !value.NativeDependencyReceipts.IsDefault
        && value.NativeDependencyReceipts.SequenceEqual(value.NativeDependencyReceipts.Order(StringComparer.Ordinal), StringComparer.Ordinal)
        && value.NativeDependencyReceipts.Distinct(StringComparer.Ordinal).Count() == value.NativeDependencyReceipts.Length
        && value.NativeDependencyReceipts.All(static item => !string.IsNullOrWhiteSpace(item))
        && value.SemanticCapabilityChecksum.Length == 32 && value.ModuleMutationCapabilityChecksum.Length == 32
        && value.ActivationCapabilityChecksum.Length == 32;

    /// <summary>Computes the purpose-bound checksum of the complete composed L50 capability.</summary>
    public static ImmutableArray<byte> ModuleMutationCapabilityChecksum(BaseModuleMutationCapability value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var stream = new MemoryStream(); Text(stream, "base.moduleMutation.capability.v1\0");
        stream.WriteByte(value.Supported ? (byte)1 : (byte)0); stream.WriteByte(value.SerializableExecution ? (byte)1 : (byte)0);
        stream.WriteByte(value.DurableReceipts ? (byte)1 : (byte)0); stream.WriteByte(value.GenerationCells ? (byte)1 : (byte)0);
        stream.WriteByte(value.AtomicRecordAndGenerationCommit ? (byte)1 : (byte)0);
        foreach (long item in BaseSemanticActivationCertificationEncoding.ModuleLimits(value.MaximumLimits)) I64(stream, item);
        return SHA256.HashData(stream.ToArray()).ToImmutableArray();
    }

    /// <summary>Computes the exact selected store-registration-set checksum.</summary>
    public static ImmutableArray<byte> StoreRegistrationSetChecksum(string kind, int protocolVersion,
        string recordStoreRegistrationId, IEnumerable<string> contributorIds)
    {
        ArgumentNullException.ThrowIfNull(contributorIds);
        string[] values = contributorIds.Order(StringComparer.Ordinal).ToArray();
        if (string.IsNullOrWhiteSpace(kind) || protocolVersion < 1 || string.IsNullOrWhiteSpace(recordStoreRegistrationId)
            || values.Length == 0 || values.Any(string.IsNullOrWhiteSpace)
            || values.Distinct(StringComparer.Ordinal).Count() != values.Length) throw new ArgumentException("base.semanticActivation.certificationInvalid");
        using var stream = new MemoryStream(); Text(stream, "base.semanticActivation.storeRegistrationSet.v1\0");
        Text(stream, kind); I32(stream, protocolVersion); Text(stream, recordStoreRegistrationId); I32(stream, values.Length);
        foreach (string value in values) Text(stream, value); return SHA256.HashData(stream.ToArray()).ToImmutableArray();
    }

    /// <summary>Validates the complete zero-collection dynamic authority captured for readiness.</summary>
    public static bool ValidateReadinessAuthority(BaseAtomicMutationAuthorityRequirement? value,
        string applicationId, string logicalStoreId, ImmutableArray<byte> installedDefinitionSetChecksum)
    {
        if (value?.SemanticActivation is not { } semantic || string.IsNullOrWhiteSpace(applicationId)
            || string.IsNullOrWhiteSpace(logicalStoreId) || installedDefinitionSetChecksum.Length != 32
            || !string.Equals(value.ApplicationId, applicationId, StringComparison.Ordinal)
            || !value.Collections.IsEmpty || string.IsNullOrWhiteSpace(value.StoreInstanceId)
            || value.RestoreEpoch < 0 || value.SchemaGeneration <= 0
            || !string.Equals(semantic.ApplicationId, value.ApplicationId, StringComparison.Ordinal)
            || !string.Equals(semantic.LogicalStoreId, logicalStoreId, StringComparison.Ordinal)
            || !string.Equals(semantic.StoreInstanceId, value.StoreInstanceId, StringComparison.Ordinal)
            || semantic.RestoreEpoch != value.RestoreEpoch || semantic.SchemaGeneration != value.SchemaGeneration
            || semantic.SemanticAuthorityGeneration <= 0 || semantic.DefinitionSetChecksum.Length != 32)
            return false;
        return CryptographicOperations.FixedTimeEquals(semantic.DefinitionSetChecksum.AsSpan(), installedDefinitionSetChecksum.AsSpan());
    }

    /// <summary>Returns a deeply owned profile.</summary>
    public static BaseSemanticActivationCertificationProfile Clone(BaseSemanticActivationCertificationProfile value) => value with
    {
        NativeDependencyReceipts = value.NativeDependencyReceipts.Select(static item => new string(item.AsSpan())).ToImmutableArray(),
        SemanticCapabilityChecksum = value.SemanticCapabilityChecksum.ToArray().ToImmutableArray(),
        ModuleMutationCapabilityChecksum = value.ModuleMutationCapabilityChecksum.ToArray().ToImmutableArray(),
        ActivationCapabilityChecksum = value.ActivationCapabilityChecksum.ToArray().ToImmutableArray(),
        ContractChecksum = value.ContractChecksum.ToArray().ToImmutableArray(), ExecutedReportChecksum = value.ExecutedReportChecksum.ToArray().ToImmutableArray(),
        Checksum = value.Checksum.ToArray().ToImmutableArray(),
    };

    /// <summary>Returns a deeply owned certification subject.</summary>
    public static BaseSemanticActivationCertificationSubject CloneSubject(BaseSemanticActivationCertificationSubject value) => value with
    {
        ProviderId = new string(value.ProviderId.AsSpan()), ProviderVersion = new string(value.ProviderVersion.AsSpan()),
        StoreProviderKind = new string(value.StoreProviderKind.AsSpan()),
        NativeDependencyReceipts = value.NativeDependencyReceipts.Select(static item => new string(item.AsSpan())).ToImmutableArray(),
        SemanticCapabilityChecksum = value.SemanticCapabilityChecksum.ToArray().ToImmutableArray(),
        ModuleMutationCapabilityChecksum = value.ModuleMutationCapabilityChecksum.ToArray().ToImmutableArray(),
        ActivationCapabilityChecksum = value.ActivationCapabilityChecksum.ToArray().ToImmutableArray(),
    };

    private static ImmutableArray<byte> ComputeContractChecksum()
    {
        using var stream = new MemoryStream(); Text(stream, "base.semanticActivation.certificationContract.v2\0"); Text(stream, ProtocolVersion);
        I32(stream, MandatoryCaseIds.Length); for (int i = 0; i < MandatoryCaseIds.Length; i++) { I32(stream, i); Text(stream, MandatoryCaseIds[i]); }
        Text(stream, "case:id,ordinal,applicability,verdictStatus,verdictError,observedStatus,observedError,optionalAtomicOutcome,receiptResolution,optionalRequestDisposition,receiptChecksum,positiveObservationSequence,liveSlots,retiredSlots,absenceMarkers,activations,receipts,activeWork,quarantinedWork,releasedWork,rejectedLateCompletions,exactLimitAccepted,maxPlusOneRejected,recoveryFloorVerified,receiptResolved,authorityBeforeChecksum,authorityAfterChecksum,evidenceBytes,evidenceChecksum;report:subject(providerId,providerVersion,storeProviderKind,storeProviderProtocolVersion,orderedNativeDependencyReceipts,semanticCapabilityChecksum,moduleMutationCapabilityChecksum,activationCapabilityChecksum),passed,contractChecksum,orderedCases,reportChecksum");
        return SHA256.HashData(stream.ToArray()).ToImmutableArray();
    }

    private static ImmutableArray<byte> ProfileChecksum(BaseSemanticActivationCertificationProfile value)
    {
        using var stream = new MemoryStream(); Text(stream, value.Supported ? "base.semanticActivation.certificationProfile.v2\0" : "base.semanticActivation.unsupportedProfile.v2\0"); Text(stream, ProtocolVersion);
        stream.WriteByte(value.Supported ? (byte)1 : (byte)0);
        Text(stream, value.ProviderId); Text(stream, value.ProviderVersion); Text(stream, value.StoreProviderKind); I32(stream, value.StoreProviderProtocolVersion);
        I32(stream, value.NativeDependencyReceipts.Length); foreach (string item in value.NativeDependencyReceipts) Text(stream, item);
        Bytes(stream, value.SemanticCapabilityChecksum.AsSpan()); Bytes(stream, value.ModuleMutationCapabilityChecksum.AsSpan()); Bytes(stream, value.ActivationCapabilityChecksum.AsSpan());
        Bytes(stream, value.ContractChecksum.AsSpan()); Bytes(stream, value.ExecutedReportChecksum.AsSpan()); return SHA256.HashData(stream.ToArray()).ToImmutableArray();
    }

    private static ImmutableArray<byte> InstalledChecksum(BaseInstalledSemanticActivationProviderDescriptor value)
    {
        using var stream = new MemoryStream(); Text(stream, "base.semanticActivation.installedCertification.v2\0"); Bytes(stream, value.Profile.Checksum.AsSpan());
        Bytes(stream, value.InstalledActivationCertificationReceipt.AsSpan()); Text(stream, value.LogicalStoreId); Text(stream, value.StoreInstanceId);
        Bytes(stream, value.StoreRegistrationSetChecksum.AsSpan()); return SHA256.HashData(stream.ToArray()).ToImmutableArray();
    }

    private static ImmutableArray<byte> ReportChecksum(BaseSemanticActivationCertificationReport value)
    {
        using var stream = new MemoryStream(); Text(stream, "base.semanticActivation.certificationReport.v2\0");
        WriteSubject(stream, value.Subject); stream.WriteByte(value.Passed ? (byte)1 : (byte)0);
        Bytes(stream, value.ContractChecksum.AsSpan()); I32(stream, value.Cases.Length);
        foreach (BaseSemanticActivationCertificationCaseResult item in value.Cases)
        {
            Text(stream, item.Id); I32(stream, item.Ordinal); I32(stream, (int)item.Applicability); I32(stream, (int)item.Status);
            Text(stream, item.ErrorCode ?? string.Empty); I32(stream, (int)item.ObservedStatus);
            Text(stream, item.ObservedErrorCode ?? string.Empty); I32(stream, item.AtomicOutcome is null ? -1 : (int)item.AtomicOutcome.Value);
            I32(stream, (int)item.ReceiptResolution); I32(stream, item.RequestDisposition is null ? -1 : (int)item.RequestDisposition.Value);
            Bytes(stream, item.ReceiptChecksum.AsSpan()); I64(stream, item.ObservationSequence); Bytes(stream, item.EvidenceChecksum.AsSpan());
        }
        return SHA256.HashData(stream.ToArray()).ToImmutableArray();
    }

    private static void WriteSubject(Stream stream, BaseSemanticActivationCertificationSubject value)
    {
        Text(stream, value.ProviderId); Text(stream, value.ProviderVersion); Text(stream, value.StoreProviderKind);
        I32(stream, value.StoreProviderProtocolVersion); I32(stream, value.NativeDependencyReceipts.Length);
        foreach (string item in value.NativeDependencyReceipts) Text(stream, item);
        Bytes(stream, value.SemanticCapabilityChecksum.AsSpan()); Bytes(stream, value.ModuleMutationCapabilityChecksum.AsSpan());
        Bytes(stream, value.ActivationCapabilityChecksum.AsSpan());
    }

    private static BaseSemanticActivationCertificationCaseResult CloneCase(BaseSemanticActivationCertificationCaseResult value) => value with
    {
        Id = new string(value.Id.AsSpan()), ErrorCode = value.ErrorCode is null ? null : new string(value.ErrorCode.AsSpan()),
        ObservedErrorCode = value.ObservedErrorCode is null ? null : new string(value.ObservedErrorCode.AsSpan()),
        ReceiptChecksum = value.ReceiptChecksum.ToArray().ToImmutableArray(),
        EvidenceChecksum = value.EvidenceChecksum.ToArray().ToImmutableArray(),
    };

    private static bool ExpectedReceiptOutcome(BaseSemanticActivationCertificationCaseResult value)
    {
        if (value.Applicability == BaseSemanticActivationCertificationApplicability.NotAdvertised)
            return value.AtomicOutcome is null && value.ReceiptResolution == BaseAtomicReceiptResolutionDisposition.NotApplicable
                && value.RequestDisposition is null && value.ReceiptChecksum.IsEmpty;
        if (value.Id == "existing-replay")
            return value.AtomicOutcome == RecordMutationExecutionOutcome.Committed
                && value.ReceiptResolution == BaseAtomicReceiptResolutionDisposition.NotApplicable
                && value.RequestDisposition == BaseMutationRequestDisposition.Duplicate && value.ReceiptChecksum.Length == 32;
        if (value.Id == "receipt-resolution")
            return value.AtomicOutcome == RecordMutationExecutionOutcome.Committed
                && value.ReceiptResolution == BaseAtomicReceiptResolutionDisposition.Found
                && value.RequestDisposition == BaseMutationRequestDisposition.Duplicate && value.ReceiptChecksum.Length == 32;
        if (value.Id is "atomic-missing-ensure" or "different-parent-race" or "terminal-retirement")
            return value.AtomicOutcome == RecordMutationExecutionOutcome.Committed
                && value.ReceiptResolution == BaseAtomicReceiptResolutionDisposition.NotApplicable
                && value.RequestDisposition == BaseMutationRequestDisposition.Committed && value.ReceiptChecksum.Length == 32;
        if (value.Id is "inspection" or "maintenance-authority" or "maintenance" or "backup-restore" or "recovery-floor")
            return value.AtomicOutcome is null && value.ReceiptResolution == BaseAtomicReceiptResolutionDisposition.NotApplicable
                && value.RequestDisposition is null && value.ReceiptChecksum.IsEmpty;
        if (value.Id is "hostile-capture" or "hostile-prepare" or "hostile-apply" or "accounting-limits")
            return value.AtomicOutcome == RecordMutationExecutionOutcome.RollbackConfirmed && value.ReceiptChecksum.IsEmpty;
        if (value.Id.StartsWith("fault-", StringComparison.Ordinal) || value.Id == "noncooperative-release")
            return value.ReceiptChecksum.IsEmpty || value.Id == "fault-ResponseLossAfterCommit" && value.ReceiptChecksum.Length == 32;
        return false;
    }

    private static bool CaseIsAdvertised(string id, BaseSemanticActivationCapability capability) => id is
        "inspection" or "maintenance-authority" or "maintenance" or "fault-NonCooperativeMaintenance" or "fault-InterruptMaintenancePublication"
            ? capability.MaintenanceSupported
            : id is "backup-restore" or "recovery-floor" or "fault-NonCooperativeRestore" or "fault-CorruptRecoveryEntry"
                or "fault-InterruptRestorePublication" or "fault-RetentionOvertake"
                ? capability.RestoreRecoveryFloorsSupported && !capability.BackupModes.IsEmpty && !capability.RestoreModes.IsEmpty
                : true;

    internal static bool ObservedOutcomeMatches(string id, BaseSemanticActivationCertificationApplicability applicability,
        OperationStatus status, string? error)
    {
        (OperationStatus Status, string? Error) expected = ExpectedObservedOutcome(id, applicability);
        return status == expected.Status && string.Equals(error, expected.Error, StringComparison.Ordinal);
    }

    internal static (OperationStatus Status, string? Error) ExpectedObservedOutcome(
        string id, BaseSemanticActivationCertificationApplicability applicability)
    {
        if (applicability == BaseSemanticActivationCertificationApplicability.NotAdvertised)
            return (OperationStatus.Unsupported, "base.semanticActivation.certification.notAdvertised");
        return id switch
        {
            "hostile-capture" or "hostile-prepare" or "hostile-apply" =>
                (OperationStatus.CapabilityUnavailable, BaseSemanticActivationErrorCodes.ProviderContractInvalid),
            "accounting-limits" => (OperationStatus.ValidationFailed, BaseSemanticActivationErrorCodes.BudgetExceeded),
            "noncooperative-release" or "fault-NonCooperativeCapture" or "fault-NonCooperativePrepare"
                or "fault-NonCooperativeApply" => (OperationStatus.StoreError, BaseSemanticActivationErrorCodes.TransactionTimeout),
            "fault-NonCooperativeReceipt" => (OperationStatus.StoreError, BaseSemanticActivationErrorCodes.ReceiptResolutionTimeout),
            "fault-NonCooperativeMaintenance" or "fault-NonCooperativeRestore" =>
                (OperationStatus.StoreError, BaseSemanticActivationErrorCodes.MaintenanceTimeout),
            "fault-ResponseLossAfterCommit" or "fault-IndeterminateCommit" =>
                (OperationStatus.StoreError, BaseSemanticActivationErrorCodes.CommitIndeterminate),
            "fault-InterruptMaintenancePublication" or "fault-InterruptRestorePublication" =>
                (OperationStatus.StoreError, BaseSemanticActivationErrorCodes.MaintenanceIndeterminate),
            "fault-CorruptRetirement" or "fault-CorruptAbsence" =>
                (OperationStatus.StoreError, BaseSemanticActivationErrorCodes.Corrupt),
            "fault-CorruptRecoveryEntry" or "fault-RetentionOvertake" =>
                (OperationStatus.StoreError, BaseSemanticActivationErrorCodes.RecoveryProofInvalid),
            "maintenance" => (OperationStatus.Updated, null),
            _ when id.StartsWith("fault-", StringComparison.Ordinal) =>
                (OperationStatus.CapabilityUnavailable, BaseSemanticActivationErrorCodes.ProviderContractInvalid),
            _ => (OperationStatus.Ok, null),
        };
    }

    private static bool Fixed(ImmutableArray<byte> left, ImmutableArray<byte> right) => left.Length == 32 && right.Length == 32 && CryptographicOperations.FixedTimeEquals(left.AsSpan(), right.AsSpan());
    private static void Text(Stream stream, string value) => Bytes(stream, Encoding.UTF8.GetBytes(value));
    private static void Bytes(Stream stream, ReadOnlySpan<byte> value) { I32(stream, value.Length); stream.Write(value); }
    private static void I32(Stream stream, int value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(bytes, value); stream.Write(bytes); }
    private static void I64(Stream stream, long value) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); stream.Write(bytes); }
}

internal static class BaseSemanticActivationCertificationEncoding
{
    internal static IEnumerable<long> ModuleLimits(BaseModuleMutationLimits value)
    {
        yield return value.MaximumCaptures; yield return value.MaximumRecordCaptures; yield return value.MaximumRelationTargetCaptures;
        yield return value.MaximumGenerationCaptures; yield return value.MaximumRecordMutations; yield return value.MaximumGenerationReads;
        yield return value.MaximumGenerationComparisons; yield return value.MaximumGenerationIncrements; yield return value.MaximumGuardNodes;
        yield return value.MaximumGuardDepth; yield return value.MaximumStatements; yield return value.MaximumBranches; yield return value.MaximumExpressionNodes;
        yield return value.MaximumReadIntervals; yield return value.MaximumSubjectValidations; yield return value.MaximumAuthorityReads;
        yield return value.MaximumRelationChecks; yield return value.MaximumUniqueConstraintChecks; yield return value.MaximumRequestBytes;
        yield return value.MaximumSelectedBytes; yield return value.MaximumGenerationBytes; yield return value.MaximumEvidenceBytes;
        yield return value.MaximumWrittenBytes; yield return value.MaximumFactBytes; yield return value.MaximumJournalBytes;
        yield return value.MaximumReceiptBytes; yield return value.MaximumResultBytes; yield return value.MaximumTransientBytes;
        yield return value.Deadlines.AcquisitionTimeout.Ticks; yield return value.Deadlines.TransactionTimeout.Ticks;
        yield return value.Deadlines.CommitObservationTimeout.Ticks; yield return value.Deadlines.ReceiptResolutionTimeout.Ticks;
    }
}
