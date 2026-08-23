using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace HPD.Base;

/// <summary>Selects the enabled semantic restore mode for one logical store.</summary>
public sealed record BaseSemanticActivationRestoreSelection
{
    /// <summary>Gets the logical store ID.</summary>
    public required string LogicalStoreId { get; init; }
    /// <summary>Gets the enabled mode, or null when semantic restore is disabled.</summary>
    public required BaseActivationRestoreMode? EnabledRestoreMode { get; init; }
    /// <summary>Gets the positive checked selection generation.</summary>
    public required long SelectionGeneration { get; init; }
    /// <summary>Gets the identified selection-publication identity.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets the canonical selection checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Identifies the only supported semantic-recovery signing algorithm.</summary>
public enum BaseSemanticRecoverySigningAlgorithm
{
    /// <summary>Ed25519 with a 32-byte public key and 64-byte signature.</summary>
    Ed25519 = 1,
}

/// <summary>Identifies the only supported semantic-recovery encryption algorithm.</summary>
public enum BaseSemanticRecoveryEncryptionAlgorithm
{
    /// <summary>AES-256-GCM with 12-byte nonces and 16-byte tags.</summary>
    Aes256Gcm = 1,
}

/// <summary>Describes one retained external recovery key version required by supported artifacts.</summary>
public sealed record BaseSemanticRecoveryRetainedKeyAuthority
{
    /// <summary>Gets the signing-key ID.</summary>
    public required string SigningKeyId { get; init; }
    /// <summary>Gets the positive signing-key version.</summary>
    public required int SigningKeyVersion { get; init; }
    /// <summary>Gets the exact 32-byte Ed25519 public key.</summary>
    public required ImmutableArray<byte> SigningPublicKey { get; init; }
    /// <summary>Gets the encryption-key ID.</summary>
    public required string EncryptionKeyId { get; init; }
    /// <summary>Gets the positive encryption-key version.</summary>
    public required int EncryptionKeyVersion { get; init; }
    /// <summary>Gets the inclusive authority start.</summary>
    public required DateTimeOffset NotBefore { get; init; }
    /// <summary>Gets the exclusive retained-until bound.</summary>
    public required DateTimeOffset RetainUntil { get; init; }
    /// <summary>Gets the canonical retained-key checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Defines the graph-owned key authority for external semantic recovery.</summary>
public sealed record BaseSemanticRecoveryKeyAuthorityReceipt
{
    /// <summary>Gets the recovery authority ID.</summary>
    public required string AuthorityId { get; init; }
    /// <summary>Gets the recovery authority version.</summary>
    public required int AuthorityVersion { get; init; }
    /// <summary>Gets the closed signing algorithm.</summary>
    public required BaseSemanticRecoverySigningAlgorithm SigningAlgorithm { get; init; }
    /// <summary>Gets the closed encryption algorithm.</summary>
    public required BaseSemanticRecoveryEncryptionAlgorithm EncryptionAlgorithm { get; init; }
    /// <summary>Gets the current signing-key ID.</summary>
    public required string CurrentSigningKeyId { get; init; }
    /// <summary>Gets the positive current signing-key version.</summary>
    public required int CurrentSigningKeyVersion { get; init; }
    /// <summary>Gets the exact 32-byte current Ed25519 public key.</summary>
    public required ImmutableArray<byte> CurrentSigningPublicKey { get; init; }
    /// <summary>Gets the current AES-256-GCM key ID.</summary>
    public required string CurrentEncryptionKeyId { get; init; }
    /// <summary>Gets the positive current AES-256-GCM key version.</summary>
    public required int CurrentEncryptionKeyVersion { get; init; }
    /// <summary>Gets the canonically ordered retained key-version coverage.</summary>
    public required ImmutableArray<BaseSemanticRecoveryRetainedKeyAuthority> RetainedKeys { get; init; }
    /// <summary>Gets the minimum retained-key lifetime.</summary>
    public required TimeSpan MinimumKeyRetention { get; init; }
    /// <summary>Gets the canonical receipt checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Describes finite external semantic-recovery capability.</summary>
public sealed record BaseSemanticRecoveryAuthorityCapability
{
    /// <summary>Gets whether durable pending publications are supported.</summary>
    public required bool DurablePendingSupported { get; init; }
    /// <summary>Gets whether identified cancellation is supported.</summary>
    public required bool IdentifiedCancellationSupported { get; init; }
    /// <summary>Gets whether locally committed pending publications are retained until resolution.</summary>
    public required bool CommitBoundRetentionSupported { get; init; }
    /// <summary>Gets whether signing keys referenced by unresolved tickets are retained permanently.</summary>
    public required bool PermanentPendingKeyRetentionSupported { get; init; }
    /// <summary>Gets the maximum entries across one bounded operation.</summary>
    public required int MaximumEntries { get; init; }
    /// <summary>Gets the maximum pages across one bounded operation.</summary>
    public required int MaximumPages { get; init; }
    /// <summary>Gets the maximum page entries.</summary>
    public required int MaximumPageEntries { get; init; }
    /// <summary>Gets the maximum canonical request bytes.</summary>
    public required long MaximumRequestBytes { get; init; }
    /// <summary>Gets the maximum canonical result bytes.</summary>
    public required long MaximumResultBytes { get; init; }
    /// <summary>Gets the maximum retained transient bytes.</summary>
    public required long MaximumTransientBytes { get; init; }
    /// <summary>Gets the maximum acquisition duration.</summary>
    public required TimeSpan MaximumAcquisitionDuration { get; init; }
    /// <summary>Gets the maximum identified resolution duration.</summary>
    public required TimeSpan MaximumResolutionDuration { get; init; }
    /// <summary>Gets the maximum publication duration.</summary>
    public required TimeSpan MaximumPublicationDuration { get; init; }
    /// <summary>Gets the maximum concurrent retained external operations.</summary>
    public required int MaximumConcurrentOperations { get; init; }
    /// <summary>Gets the canonical capability checksum.</summary>
    public required ImmutableArray<byte> CapabilityChecksum { get; init; }
}

/// <summary>Defines one graph-installed external semantic-recovery authority.</summary>
public sealed record BaseSemanticRecoveryAuthorityDefinition
{
    /// <summary>Gets the stable authority ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive authority version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the logical store ID.</summary>
    public required string LogicalStoreId { get; init; }
    /// <summary>Gets the owning module ID.</summary>
    public required string OwningModuleId { get; init; }
    /// <summary>Gets the exact ControlPlane grant for quarantine recovery.</summary>
    public required string RecoveryGrantId { get; init; }
    /// <summary>Gets the required finite capability.</summary>
    public required BaseSemanticRecoveryAuthorityCapability RequiredCapability { get; init; }
    /// <summary>Gets graph-owned host execution ceilings.</summary>
    public required BaseSemanticRecoveryOperationLimits Limits { get; init; }
    /// <summary>Gets the exact key authority.</summary>
    public required BaseSemanticRecoveryKeyAuthorityReceipt KeyAuthority { get; init; }
    /// <summary>Gets the canonical definition checksum.</summary>
    public required ImmutableArray<byte> ContractChecksum { get; init; }
}

/// <summary>Contains executed certification authority for one external recovery implementation.</summary>
public sealed record BaseSemanticRecoveryAuthorityCertificationReceipt
{
    /// <summary>Gets the authority ID.</summary>
    public required string AuthorityId { get; init; }
    /// <summary>Gets the authority version.</summary>
    public required int AuthorityVersion { get; init; }
    /// <summary>Gets the implementation contract ID.</summary>
    public required string ImplementationContractId { get; init; }
    /// <summary>Gets the implementation contract version.</summary>
    public required int ImplementationContractVersion { get; init; }
    /// <summary>Gets the native-dependency receipt checksum.</summary>
    public required ImmutableArray<byte> NativeDependencyReceiptChecksum { get; init; }
    /// <summary>Gets the certified capability checksum.</summary>
    public required ImmutableArray<byte> CapabilityChecksum { get; init; }
    /// <summary>Gets the definition checksum.</summary>
    public required ImmutableArray<byte> DefinitionContractChecksum { get; init; }
    /// <summary>Gets the executed certification report checksum.</summary>
    public required ImmutableArray<byte> ExecutedCertificationReportChecksum { get; init; }
    /// <summary>Gets the positive observation sequence.</summary>
    public required long ObservationSequence { get; init; }
    /// <summary>Gets the canonical receipt checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
    /// <summary>Gets the exact Ed25519 signature.</summary>
    public required ImmutableArray<byte> Signature { get; init; }
}

/// <summary>Creates one graph-owned external recovery instance.</summary>
public interface IBaseSemanticRecoveryAuthorityFactory
{
    /// <summary>Creates a new instance owned by one finalized graph generation.</summary>
    IBaseSemanticActivationRecoveryAuthority CreateOwned();
}

/// <summary>Describes the immutable certified implementation owned by one recovery instance.</summary>
public sealed record BaseSemanticRecoveryAuthorityInstanceDescriptor
{
    /// <summary>Gets the implementation contract ID.</summary>
    public required string ImplementationContractId { get; init; }
    /// <summary>Gets the positive implementation contract version.</summary>
    public required int ImplementationContractVersion { get; init; }
    /// <summary>Gets the exact certified capability checksum.</summary>
    public required ImmutableArray<byte> CapabilityChecksum { get; init; }
    /// <summary>Gets the exact key-authority checksum.</summary>
    public required ImmutableArray<byte> KeyAuthorityChecksum { get; init; }
    /// <summary>Gets the exact definition checksum.</summary>
    public required ImmutableArray<byte> DefinitionChecksum { get; init; }
    /// <summary>Gets the exact certification checksum.</summary>
    public required ImmutableArray<byte> CertificationChecksum { get; init; }
    /// <summary>Gets the canonical descriptor checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Registers one inert external recovery definition, factory, and certification receipt.</summary>
public sealed record BaseSemanticRecoveryAuthorityRegistration
{
    /// <summary>Gets the graph-owned definition.</summary>
    public required BaseSemanticRecoveryAuthorityDefinition Definition { get; init; }
    /// <summary>Gets the owned-instance factory.</summary>
    public required IBaseSemanticRecoveryAuthorityFactory Factory { get; init; }
    /// <summary>Gets executed certification authority.</summary>
    public required BaseSemanticRecoveryAuthorityCertificationReceipt Certification { get; init; }
}

/// <summary>Creates and validates canonical external semantic-recovery installation authority.</summary>
public static class BaseSemanticRecoveryAuthorityContract
{
    /// <summary>Creates the exact effective operation limits certified by one installed definition.</summary>
    public static BaseSemanticRecoveryOperationLimits OperationLimits(BaseSemanticRecoveryAuthorityDefinition definition) => definition.Limits;

    /// <summary>Computes the canonical pending terminal-intent checksum.</summary>
    public static ImmutableArray<byte> PendingIntentChecksum(BaseSemanticRecoveryPendingTerminalIntent value) =>
        Hash("base.semanticRecovery.pendingIntent.v1\0", writer =>
        {
            Boundary(writer, value.Boundary); writer.Bytes(value.RetirementOperationFingerprint.AsSpan());
            writer.Bool(value.SubjectLifetime is not null);
            if (value.SubjectLifetime is { } lifetime) writer.Bytes(BaseSemanticActivationEvidenceContract.SubjectLifetimeChecksum(lifetime).AsSpan());
        });

    /// <summary>Computes the canonical pending-ticket checksum.</summary>
    public static ImmutableArray<byte> PendingChecksum(BaseSemanticRecoveryPendingPublication value) =>
        Hash("base.semanticRecovery.pending.v1\0", writer =>
        {
            writer.I64(value.Sequence); writer.Text(value.TicketNonce); writer.Bytes(value.IntentChecksum.AsSpan());
            writer.Text(value.SigningKeyId); writer.I32(value.SigningKeyVersion);
            writer.I64(value.CancellationEligibleAt.ToUnixTimeMilliseconds());
        });

    /// <summary>Computes the canonical local transaction binding for one pending ticket.</summary>
    public static ImmutableArray<byte> PendingCommitChecksum(BaseSemanticRecoveryPendingCommitAuthority value) =>
        Hash("base.semanticRecovery.pendingCommit.v1\0", writer =>
        {
            writer.Text(value.ApplicationId); writer.Text(value.LogicalStoreId);
            writer.Text(value.LocalScope); writer.Text(value.LocalOperation); writer.Text(value.LocalIdempotencyKey);
            writer.Bytes(value.LocalFingerprint.AsSpan()); writer.Bytes(value.LocalStructuralDigest.AsSpan());
            writer.Text(value.AuthorityId); writer.I32(value.AuthorityVersion); writer.Bytes(value.AuthorityChecksum.AsSpan());
            writer.Bytes(value.Intent.Checksum.AsSpan()); writer.Bytes(value.Pending.Checksum.AsSpan());
        });

    /// <summary>Computes the canonical identified pending-resolution request checksum.</summary>
    public static ImmutableArray<byte> ResolvePendingRequestChecksum(BaseSemanticRecoveryResolvePendingRequest value) =>
        Hash("base.semanticRecovery.resolvePendingRequest.v1\0", writer =>
        {
            writer.Text(value.ApplicationId); writer.Text(value.LogicalStoreId); writer.Bytes(value.Intent.Checksum.AsSpan());
            Identity(writer, value.BeginIdentity); WriteLimits(writer, value.Limits);
        });

    /// <summary>Computes the canonical identified pending-resolution checksum.</summary>
    public static ImmutableArray<byte> PendingResolutionChecksum(BaseSemanticRecoveryPendingResolution value) =>
        Hash("base.semanticRecovery.pendingResolution.v1\0", writer =>
        {
            writer.Bytes(value.RequestChecksum.AsSpan()); writer.I32((int)value.Disposition); writer.Bool(value.Pending is not null);
            if (value.Pending is { } pending) writer.Bytes(PendingChecksum(pending).AsSpan());
        });

    /// <summary>Validates one identified pending-resolution result.</summary>
    public static bool PendingResolutionIsValid(BaseSemanticRecoveryAuthorityDefinition definition,
        BaseSemanticRecoveryResolvePendingRequest request, BaseSemanticRecoveryPendingResolution value,
        DateTimeOffset observedAt)
    {
        if (!Enum.IsDefined(value.Disposition) || value.RequestChecksum.Length != 32 || value.Checksum.Length != 32
            || !Fixed(value.RequestChecksum, ResolvePendingRequestChecksum(request))
            || !Fixed(value.Checksum, PendingResolutionChecksum(value))
            || !Verify(definition.KeyAuthority.CurrentSigningPublicKey,
                "base.semanticRecovery.pendingResolutionSignature.v1\0", value.Checksum, value.Signature)) return false;
        return value.Disposition switch
        {
            BaseSemanticRecoveryPendingResolutionDisposition.Missing => value.Pending is null,
            BaseSemanticRecoveryPendingResolutionDisposition.Pending
                or BaseSemanticRecoveryPendingResolutionDisposition.Cancelled
                or BaseSemanticRecoveryPendingResolutionDisposition.Finalized => value.Pending is { } pending
                    && PendingIsValid(definition, request.Intent, pending, observedAt),
            _ => false,
        };
    }

    /// <summary>Computes the canonical finalize-request binding checksum.</summary>
    public static ImmutableArray<byte> FinalizeRequestChecksum(BaseSemanticRecoveryFinalizeRequest value) =>
        Hash("base.semanticRecovery.finalizeRequest.v1\0", writer =>
        {
            writer.Text(value.ApplicationId); writer.Text(value.LogicalStoreId);
            writer.Bytes(PendingChecksum(value.Pending).AsSpan()); writer.Bytes(value.FinalEntry.Checksum.AsSpan());
            writer.Bytes(value.LocalReceipt.Checksum.AsSpan()); writer.Bytes(value.CommitObservationChecksum.AsSpan());
            Identity(writer, value.Identity); WriteLimits(writer, value.Limits);
        });

    /// <summary>Computes the canonical exact finalization-result checksum.</summary>
    public static ImmutableArray<byte> FinalizationResultChecksum(BaseSemanticRecoveryFinalizationResult value) =>
        Hash("base.semanticRecovery.finalizationResult.v1\0", writer =>
        {
            writer.Bytes(value.RequestChecksum.AsSpan()); writer.Bytes(PublishedHeadChecksum(value.Head).AsSpan());
        });

    /// <summary>Validates exact finalize-request/result correspondence and authenticated head authority.</summary>
    public static bool FinalizationIsValid(BaseSemanticRecoveryAuthorityDefinition definition,
        BaseSemanticRecoveryFinalizeRequest request, BaseSemanticRecoveryFinalizationResult value) =>
        value.RequestChecksum.Length == 32 && value.Checksum.Length == 32
        && Fixed(value.RequestChecksum, FinalizeRequestChecksum(request))
        && Fixed(value.Checksum, FinalizationResultChecksum(value))
        && Verify(definition.KeyAuthority.CurrentSigningPublicKey,
            "base.semanticRecovery.finalizationResultSignature.v1\0", value.Checksum, value.Signature)
        && PublishedHeadIsValid(definition, request.ApplicationId, request.LogicalStoreId,
            FinalizeRequestChecksum(request), value.Head)
        && value.Head.PublishedSequence >= request.Pending.Sequence;

    /// <summary>Computes the canonical cancellation-request binding checksum.</summary>
    public static ImmutableArray<byte> CancelRequestChecksum(BaseSemanticRecoveryCancelRequest value) =>
        Hash("base.semanticRecovery.cancelRequest.v1\0", writer =>
        {
            writer.Bytes(PendingChecksum(value.Pending).AsSpan()); writer.Bytes(value.ConfirmedRollbackProofChecksum.AsSpan());
            Identity(writer, value.Identity); WriteLimits(writer, value.Limits);
        });

    /// <summary>Validates exact cancel-request/result correspondence.</summary>
    public static bool CancellationIsValid(BaseSemanticRecoveryAuthorityDefinition definition,
        BaseSemanticRecoveryCancelRequest request,
        BaseSemanticRecoveryCancellationResult value) => Enum.IsDefined(value.Disposition)
        && value.Sequence == request.Pending.Sequence && value.RequestChecksum.Length == 32 && value.Checksum.Length == 32
        && Fixed(value.RequestChecksum, CancelRequestChecksum(request))
        && Fixed(value.Checksum, CancellationResultChecksum(value))
        && Verify(definition.KeyAuthority.CurrentSigningPublicKey,
            "base.semanticRecovery.cancellationResultSignature.v1\0", value.Checksum, value.Signature);

    /// <summary>Computes the canonical receipt-resolvable local recovery handoff checksum.</summary>
    public static ImmutableArray<byte> LocalReceiptAuthorityChecksum(BaseSemanticRecoveryLocalReceiptAuthority value) =>
        Hash("base.semanticRecovery.localReceipt.v1\0", writer =>
        {
            writer.Bytes(value.PendingAuthority.Checksum.AsSpan()); writer.Bytes(value.FinalEntry.Checksum.AsSpan());
        });

    /// <summary>Computes provider-confirmed no-commit authority after a rolled-back local transaction.</summary>
    public static ImmutableArray<byte> RollbackProofChecksum(BaseSemanticRecoveryPendingCommitAuthority pending,
        BaseAtomicMutationExecutionRequest request, RecordMutationExecutionOutcome outcome) =>
        Hash("base.semanticRecovery.rollbackProof.v1\0", writer =>
        {
            writer.Bytes(pending.Checksum.AsSpan()); writer.Text(request.Identity.Scope); writer.Text(request.Identity.Operation);
            writer.Text(request.Identity.IdempotencyKey); writer.Bytes(request.Identity.Fingerprint.ToArray());
            writer.Bytes(request.StructuralDigest); writer.I32((int)outcome);
        });

    /// <summary>Computes the canonical finalized recovery-entry checksum.</summary>
    public static ImmutableArray<byte> RecoveryEntryChecksum(BaseSemanticActivationRecoveryEntry value) =>
        Hash("base.semanticRecovery.entry.v1\0", writer =>
        {
            Boundary(writer, value.Boundary);
            writer.Bytes(BaseSemanticActivationEvidenceContract.ScopeBindingChecksum(value.ScopeBinding).AsSpan());
            writer.Bytes(value.TerminalActivation.Checksum.AsSpan());
            writer.Text(value.RetirementOperation.OperationId); writer.I32(value.RetirementOperation.OperationVersion);
            writer.Text(value.RetirementOperation.OperationChecksum);
            writer.Text(value.Definition.Id); writer.I32(value.Definition.Version);
            writer.Bytes(value.Definition.Checksum.AsSpan()); writer.I32((int)value.State); writer.I64(value.SlotGeneration);
            writer.Bytes(value.AuthorityBytes.AsSpan());
        });

    /// <summary>Computes the canonical fingerprint of the installed retirement operation.</summary>
    public static ImmutableArray<byte> RetirementOperationFingerprint(BaseSemanticActivationModuleOperationIdentity value) =>
        Hash("base.semanticRecovery.retirementOperation.v1\0", writer =>
        {
            writer.Text(value.OperationId); writer.I32(value.OperationVersion); writer.Text(value.OperationChecksum);
        });

    /// <summary>Computes the canonical terminal activation snapshot checksum.</summary>
    public static ImmutableArray<byte> TerminalActivationChecksum(BaseSemanticRecoveryTerminalActivationAuthority value) =>
        Hash("base.semanticRecovery.terminalActivation.v1\0", writer =>
        {
            BaseActivationPayload payload = value.Payload;
            writer.Text(payload.ActivationId); writer.Text(payload.Definition.Id); writer.I32(payload.Definition.Version);
            writer.Bytes(payload.Definition.Checksum.AsSpan()); writer.Bytes(payload.CanonicalInput.AsSpan());
            writer.Bytes(payload.InputChecksum.AsSpan()); writer.I32((int)payload.Scope.Kind);
            writer.Bool(payload.Scope.Value is not null); if (payload.Scope.Value is { } scope) writer.Text(scope);
            writer.Bool(payload.OccurrenceId is not null); if (payload.OccurrenceId is { } occurrence) writer.Text(occurrence);
            writer.I64(payload.RequestedDueAt); writer.I64(payload.EffectiveDueAt); writer.Bytes(payload.Checksum.AsSpan());
            writer.Bytes(value.CreationFingerprint.AsSpan()); writer.I32(value.Priority);
            writer.Bool(value.OverlapKey is not null); if (value.OverlapKey is { } overlap) writer.Bytes(overlap.AsSpan());
            writer.I32((int)value.OverlapPolicy); writer.Bool(value.Eligible); writer.I32((int)value.State);
            writer.I64(value.Generation); writer.Bytes(value.ControlChecksum.AsSpan()); writer.I32(value.AttemptNumber);
            writer.I64(value.ClaimEpoch); writer.Bool(value.CanonicalResult is not null);
            if (value.CanonicalResult is { } result) writer.Bytes(result.AsSpan());
            writer.Bool(value.CanonicalResultChecksum is not null);
            if (value.CanonicalResultChecksum is { } resultChecksum) writer.Bytes(resultChecksum.AsSpan());
            writer.Text(value.TerminalReceipt.ReceiptKey); writer.Text(value.TerminalReceipt.OperationKind);
            writer.Bytes(value.TerminalReceipt.Fingerprint.AsSpan()); writer.Bytes(value.TerminalReceipt.ResultBytes.AsSpan());
            writer.Bytes(value.TerminalReceipt.ResultChecksum.AsSpan()); writer.Bytes(value.TerminalReceipt.AuthorityChecksum.AsSpan());
        });

    /// <summary>Validates the closed terminal activation snapshot shape and its internal checksums.</summary>
    public static bool TerminalActivationIsValid(BaseSemanticRecoveryTerminalActivationAuthority value)
    {
        try
        {
            BaseActivationPayload payload = value.Payload;
            bool terminal = value.State is BaseActivationState.Succeeded or BaseActivationState.Exhausted
                or BaseActivationState.Cancelled or BaseActivationState.Migrated or BaseActivationState.Disposed;
            return terminal && value.Generation > 0 && value.AttemptNumber >= 0 && value.ClaimEpoch >= 0
                && !value.Eligible && payload.OccurrenceId is null && value.OverlapKey is null
                && value.OverlapPolicy == BaseScheduleOverlapPolicy.Allow && value.Priority is >= -32 and <= 32
                && payload.ActivationId.Length > 0 && payload.Definition.Version > 0
                && payload.Definition.Checksum.Length == 32 && payload.InputChecksum.Length == 32
                && payload.Checksum.Length == 32 && value.CreationFingerprint.Length == 32
                && value.ControlChecksum.Length == 32 && value.TerminalReceipt.AuthorityChecksum.Length == 32
                && CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload.CanonicalInput.AsSpan()), payload.InputChecksum.AsSpan())
                && CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload.CanonicalInput.AsSpan()), payload.Checksum.AsSpan())
                && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(
                    $"base.activation.control.v2\0{payload.ActivationId}\n{value.Generation}\n{(int)value.State}")), value.ControlChecksum.AsSpan())
                && value.TerminalReceipt.ResultChecksum.Length == 32
                && CryptographicOperations.FixedTimeEquals(SHA256.HashData(value.TerminalReceipt.ResultBytes.AsSpan()),
                    value.TerminalReceipt.ResultChecksum.AsSpan())
                && TerminalReceiptMatches(value)
                && (value.CanonicalResult is null) == (value.CanonicalResultChecksum is null)
                && (value.CanonicalResult is null || CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(value.CanonicalResult.Value.AsSpan()), value.CanonicalResultChecksum!.Value.AsSpan()))
                && value.Checksum.Length == 32 && Fixed(value.Checksum, TerminalActivationChecksum(value));
        }
        catch { return false; }
    }

    private static bool TerminalReceiptMatches(BaseSemanticRecoveryTerminalActivationAuthority value)
    {
        BaseSemanticRecoveryTerminalReceiptEvidence receipt = value.TerminalReceipt;
        if (string.IsNullOrWhiteSpace(receipt.ReceiptKey) || string.IsNullOrWhiteSpace(receipt.OperationKind)
            || receipt.Fingerprint.Length != 32 || receipt.AuthorityChecksum.Length != 32) return false;
        byte[] authority = SHA256.HashData(Encoding.UTF8.GetBytes(receipt.OperationKind)
            .Concat(receipt.Fingerprint).Concat(receipt.ResultBytes).ToArray());
        if (!Fixed(receipt.AuthorityChecksum, authority.ToImmutableArray())) return false;
        BaseActivationTransitionResult? result = System.Text.Json.JsonSerializer.Deserialize(
            receipt.ResultBytes.AsSpan(), HPDBaseJsonSerializerContext.Default.BaseActivationTransitionResult);
        if (result is null || result.State != value.State || result.Generation != value.Generation
            || !Fixed(result.ControlChecksum, value.ControlChecksum)) return false;
        return receipt.OperationKind switch
        {
            "activation-completed" or "effect-completed" => value.State == BaseActivationState.Succeeded,
            "activation-failed-terminal" => value.State == BaseActivationState.Exhausted,
            "activation-cancelled" => value.State == BaseActivationState.Cancelled,
            "activation-migrated" => value.State == BaseActivationState.Migrated,
            "activation-disposed" => value.State == BaseActivationState.Disposed,
            "effect-reconciled" => value.State is BaseActivationState.Succeeded or BaseActivationState.Exhausted,
            _ => false,
        };
    }

    /// <summary>Computes the canonical checksum for one finalized external publication.</summary>
    public static ImmutableArray<byte> PublicationEntryChecksum(BaseSemanticRecoveryPublicationEntry value) =>
        Hash("base.semanticRecovery.publicationEntry.v1\0", writer =>
        {
            writer.I64(value.Sequence); writer.Bytes(value.Entry.Checksum.AsSpan());
            writer.Bytes(value.LocalReceipt.Checksum.AsSpan()); writer.Bytes(value.CommitObservationChecksum.AsSpan());
        });

    /// <summary>Computes the canonical externally retained local L37 receipt envelope checksum.</summary>
    public static ImmutableArray<byte> LocalReceiptEnvelopeChecksum(BaseSemanticRecoveryLocalReceiptEnvelope value) =>
        Hash("base.semanticRecovery.localReceiptEnvelope.v1\0", writer =>
        {
            Identity(writer, value.Identity); writer.Bytes(value.StructuralDigest.AsSpan());
            writer.Bytes(value.ReceiptBytes.AsSpan()); writer.Bytes(value.ReceiptChecksum.AsSpan());
            writer.I32(value.ReceiptFormatVersion); writer.I64(value.SchemaGeneration); writer.Text(value.StoreInstanceId);
            writer.I64(value.CommittedAt.ToUnixTimeMilliseconds()); writer.I64(value.ExpiresAt.ToUnixTimeMilliseconds());
            writer.Bytes(value.CommitObservationChecksum.AsSpan());
        });

    /// <summary>Computes the canonical checksum for one strict external publication page.</summary>
    public static ImmutableArray<byte> PublicationPageChecksum(BaseSemanticRecoveryPublicationPage value) =>
        Hash("base.semanticRecovery.publicationPage.v1\0", writer =>
        {
            writer.I64(value.AfterSequence); writer.I32(value.Entries.Length);
            foreach (BaseSemanticRecoveryPublicationEntry entry in value.Entries) writer.Bytes(entry.Checksum.AsSpan());
            writer.Bool(value.NextAfterSequence is not null);
            if (value.NextAfterSequence is { } next) writer.I64(next);
            writer.I64(value.HeadSequence);
        });

    /// <summary>Computes the canonical ordered checksum for a complete contiguous publication set.</summary>
    public static ImmutableArray<byte> EmptyPublicationSetChecksum() =>
        SHA256.HashData("base.semanticRecovery.orderedPublicationSet.empty.v1\0"u8).ToImmutableArray();

    /// <summary>Advances the canonical ordered publication checksum by exactly one contiguous entry.</summary>
    public static ImmutableArray<byte> AdvancePublicationSetChecksum(ImmutableArray<byte> prior,
        long priorCount, BaseSemanticRecoveryPublicationEntry entry) =>
        Hash("base.semanticRecovery.orderedPublicationSet.advance.v1\0", writer =>
        {
            writer.I64(priorCount); writer.Bytes(prior.AsSpan()); writer.I64(entry.Sequence);
            writer.Bytes(entry.Checksum.AsSpan());
        });

    /// <summary>Computes the canonical ordered checksum for a complete contiguous publication set.</summary>
    public static ImmutableArray<byte> OrderedPublicationSetChecksum(
        IEnumerable<BaseSemanticRecoveryPublicationEntry> entries)
    {
        ImmutableArray<byte> checksum = EmptyPublicationSetChecksum();
        long count = 0;
        foreach (BaseSemanticRecoveryPublicationEntry entry in entries)
        {
            if (entry.Sequence != checked(count + 1))
                throw new ArgumentException("Recovery publication sequences must be contiguous.", nameof(entries));
            checksum = AdvancePublicationSetChecksum(checksum, count, entry);
            count++;
        }
        return checksum;
    }

    /// <summary>Validates one page against its exact request and authenticated immutable head.</summary>
    public static bool PublicationPageIsValid(BaseSemanticRecoveryPageRequest request,
        BaseSemanticRecoveryPublicationPage value)
    {
        try
        {
            if (request.Take is <= 0 or > 256 || value.AfterSequence != request.AfterSequence
                || value.HeadSequence != request.Head.PublishedSequence || value.Entries.Length > request.Take
                || value.Checksum.Length != 32 || !Fixed(value.Checksum, PublicationPageChecksum(value))) return false;
            long expected = checked(value.AfterSequence + 1);
            foreach (BaseSemanticRecoveryPublicationEntry publication in value.Entries)
            {
                if (publication.Sequence != expected || publication.Entry.Checksum.Length != 32
                    || !LocalReceiptEnvelopeIsValid(publication.LocalReceipt) || publication.CommitObservationChecksum.Length != 32
                    || publication.Checksum.Length != 32 || !Fixed(publication.Entry.Checksum, RecoveryEntryChecksum(publication.Entry))
                    || !Fixed(publication.Checksum, PublicationEntryChecksum(publication))) return false;
                expected = checked(expected + 1);
            }
            long last = value.Entries.IsEmpty ? value.AfterSequence : value.Entries[^1].Sequence;
            return value.NextAfterSequence switch
            {
                null => last == value.HeadSequence,
                long next => value.Entries.Length == request.Take && next == last && next < value.HeadSequence,
            };
        }
        catch { return false; }
    }

    /// <summary>Validates exact canonical local receipt bytes and their request authority.</summary>
    public static bool LocalReceiptEnvelopeIsValid(BaseSemanticRecoveryLocalReceiptEnvelope value)
    {
        try
        {
            if (!(value.Identity is not null && value.Identity.Fingerprint.ToArray().Length == 32
                && value.StructuralDigest.Length == 32 && !value.ReceiptBytes.IsDefaultOrEmpty
                && value.ReceiptChecksum.Length == 32 && value.Checksum.Length == 32
                && value.ReceiptFormatVersion == 2 && value.SchemaGeneration > 0 && !string.IsNullOrWhiteSpace(value.StoreInstanceId)
                && value.CommittedAt > DateTimeOffset.UnixEpoch && value.ExpiresAt > value.CommittedAt
                && value.CommitObservationChecksum.Length == 32
                && Fixed(value.ReceiptChecksum, SHA256.HashData(value.ReceiptBytes.AsSpan()).ToImmutableArray())
                && Fixed(value.Checksum, LocalReceiptEnvelopeChecksum(value)))) return false;
            BaseAtomicReceiptWire? wire = System.Text.Json.JsonSerializer.Deserialize(
                value.ReceiptBytes.AsSpan(), HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
            if (wire is null) return false;
            BaseAtomicReceiptResult materialized = wire.Materialize();
            if (materialized.Kind != BaseAtomicReceiptResultKind.ModuleMutation
                || materialized.ModuleMutation?.SemanticActivation?.RecoveryPublication is null) return false;
            byte[] canonical = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                BaseAtomicReceiptWire.From(materialized), HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
            return canonical.AsSpan().SequenceEqual(value.ReceiptBytes.AsSpan());
        }
        catch { return false; }
    }

    /// <summary>Computes the canonical checksum for Runtime-validated restore authority.</summary>
    public static ImmutableArray<byte> RestoreAuthorityChecksum(BaseSemanticRecoveryRestoreAuthority value) =>
        Hash("base.semanticRecovery.restoreAuthority.v1\0", writer =>
        {
            writer.Bytes(value.Definition.ContractChecksum.AsSpan());
            writer.I64(value.AcceptedNow); writer.I32(value.PageCount); writer.I64(value.CanonicalBytes);
            writer.I64(value.TransientBytes); WriteLimits(writer, value.Limits);
            writer.I64(value.ArtifactSequence); writer.Bytes(value.ArtifactOrderedChecksum.AsSpan());
            writer.Bytes(HeadRequestChecksum(value.HeadRequest).AsSpan());
            writer.Bytes(value.Head.Checksum.AsSpan()); writer.I32(value.Publications.Length);
            foreach (BaseSemanticRecoveryPublicationEntry entry in value.Publications) writer.Bytes(entry.Checksum.AsSpan());
        });

    /// <summary>Validates a complete contiguous external suffix against its authenticated head.</summary>
    public static bool RestoreAuthorityIsValid(BaseSemanticRecoveryAuthorityDefinition definition,
        BaseSemanticRecoveryRestoreAuthority value)
    {
        try
        {
            if (!Fixed(value.Definition.ContractChecksum, definition.ContractChecksum)
                || value.Definition.LogicalStoreId != definition.LogicalStoreId
                || value.AcceptedNow <= 0 || value.PageCount < 0 || value.PageCount > value.Limits.MaximumPages
                || value.CanonicalBytes < 0 || value.TransientBytes != value.CanonicalBytes
                || value.TransientBytes > value.Limits.MaximumTransientBytes
                || value.ArtifactSequence < 0 || value.ArtifactOrderedChecksum.Length != 32
                || value.Head.HasPendingSuccessor || value.Head.EntryCount != value.Head.PublishedSequence
                || value.Head.PublishedSequence < value.ArtifactSequence
                || value.Publications.Length != value.Head.PublishedSequence - value.ArtifactSequence
                || !HeadRequestIsValid(definition, value.HeadRequest)
                || !PublishedHeadIsValid(definition, value.HeadRequest.ApplicationId, value.HeadRequest.LogicalStoreId,
                    HeadRequestChecksum(value.HeadRequest), value.Head) || value.Checksum.Length != 32
                || !Fixed(value.Checksum, RestoreAuthorityChecksum(value))) return false;
            long exactBytes = 0;
            foreach (BaseSemanticRecoveryPublicationEntry entry in value.Publications)
                exactBytes = checked(exactBytes + System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                    entry, HPDBaseJsonSerializerContext.Default.BaseSemanticRecoveryPublicationEntry).LongLength);
            int exactPages = value.Publications.IsEmpty ? 0
                : checked((value.Publications.Length + value.Limits.MaximumPageEntries - 1) / value.Limits.MaximumPageEntries);
            if (value.CanonicalBytes != exactBytes || value.PageCount != exactPages) return false;
            long sequence = value.ArtifactSequence;
            ImmutableArray<byte> checksum = value.ArtifactOrderedChecksum;
            foreach (BaseSemanticRecoveryPublicationEntry publication in value.Publications)
            {
                if (publication.Sequence != checked(sequence + 1)
                    || publication.Checksum.Length != 32 || !Fixed(publication.Checksum, PublicationEntryChecksum(publication))
                    || !LocalReceiptEnvelopeIsValid(publication.LocalReceipt)
                    || !Fixed(publication.Entry.Checksum, RecoveryEntryChecksum(publication.Entry))
                    || !Fixed(publication.Entry.ScopeBinding.Checksum,
                        BaseSemanticActivationEvidenceContract.ScopeBindingChecksum(publication.Entry.ScopeBinding))
                    || !Fixed(publication.Entry.Boundary.ScopeBindingId, publication.Entry.ScopeBinding.BindingId)
                    || !TerminalActivationIsValid(publication.Entry.TerminalActivation)
                    || !PublicationCorrespondenceIsValid(definition, value.Head.ApplicationId,
                        value.Head.LogicalStoreId, publication)
                    || publication.Entry.TerminalActivation.Payload.Scope.Kind != publication.Entry.ScopeBinding.Kind) return false;
                checksum = AdvancePublicationSetChecksum(checksum, sequence, publication);
                sequence++;
            }
            return sequence == value.Head.PublishedSequence && Fixed(checksum, value.Head.OrderedEntrySetChecksum);
        }
        catch { return false; }
    }

    /// <summary>Validates exact correspondence among one signed publication, its pending authority, terminal entry, and outer receipt.</summary>
    public static bool PublicationCorrespondenceIsValid(BaseSemanticRecoveryAuthorityDefinition definition,
        string applicationId, string logicalStoreId, BaseSemanticRecoveryPublicationEntry publication)
    {
        BaseAtomicReceiptWire? wire = System.Text.Json.JsonSerializer.Deserialize(
            publication.LocalReceipt.ReceiptBytes.AsSpan(), HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
        BaseSemanticActivationReceiptEvidence? semantic = wire?.Materialize().ModuleMutation?.SemanticActivation;
        if (semantic?.RecoveryPublication is not { } recovery) return false;
        BaseSemanticRecoveryPendingCommitAuthority pending = recovery.PendingAuthority;
        BaseSemanticActivationRetirementAuthority? retired = System.Text.Json.JsonSerializer.Deserialize(
            publication.Entry.AuthorityBytes.AsSpan(), HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority);
        if (retired is null) return false;
        return CompleteReceiptCorrespondence();

        bool CompleteReceiptCorrespondence() =>
            pending.ApplicationId == applicationId && pending.LogicalStoreId == logicalStoreId
            && pending.LocalScope == publication.LocalReceipt.Identity.Scope
            && pending.LocalOperation == publication.LocalReceipt.Identity.Operation
            && pending.LocalIdempotencyKey == publication.LocalReceipt.Identity.IdempotencyKey
            && Fixed(pending.LocalFingerprint, publication.LocalReceipt.Identity.Fingerprint.ToArray().ToImmutableArray())
            && Fixed(pending.LocalStructuralDigest, publication.LocalReceipt.StructuralDigest)
            && pending.AuthorityId == definition.Id && pending.AuthorityVersion == definition.Version
            && Fixed(pending.AuthorityChecksum, definition.ContractChecksum)
            && Fixed(pending.Checksum, PendingCommitChecksum(pending))
            && Fixed(pending.Intent.Checksum, PendingIntentChecksum(pending.Intent))
            && PendingCommitIsValid(definition, pending.Intent, pending.Pending)
            && Fixed(pending.Intent.RetirementOperationFingerprint,
                RetirementOperationFingerprint(publication.Entry.RetirementOperation))
            && recovery.PendingAuthority.Pending.Sequence == publication.Sequence
            && pending.Intent.Boundary.DefinitionId == publication.Entry.Boundary.DefinitionId
            && Fixed(pending.Intent.Boundary.ScopeBindingId, publication.Entry.Boundary.ScopeBindingId)
            && FixedKey(pending.Intent.Boundary.Key, publication.Entry.Boundary.Key)
            && Fixed(recovery.FinalEntry.Checksum, publication.Entry.Checksum)
            && semantic.Operation == BaseSemanticActivationOperationKind.Retire
            && semantic.RetirementDisposition == BaseSemanticActivationRetirementDisposition.RetiredNow
            && semantic.EnsureDisposition is null && semantic.State == BaseSemanticActivationSlotState.Retired
            && semantic.DefinitionId == publication.Entry.Definition.Id
            && semantic.DefinitionVersion == publication.Entry.Definition.Version
            && Fixed(semantic.DefinitionChecksum, publication.Entry.Definition.Checksum)
            && FixedKey(semantic.Key, publication.Entry.Boundary.Key)
            && semantic.SlotGeneration == publication.Entry.SlotGeneration
            && Fixed(semantic.SlotChecksum, retired.Checksum)
            && semantic.JournalPosition == retired.RetirementPosition
            && retired.SlotGeneration == publication.Entry.SlotGeneration
            && retired.Definition.Id == publication.Entry.Definition.Id
            && retired.Definition.Version == publication.Entry.Definition.Version
            && Fixed(retired.Definition.Checksum, publication.Entry.Definition.Checksum)
            && FixedKey(retired.KeyDigest, publication.Entry.Boundary.Key)
            && retired.TerminalState == publication.Entry.TerminalActivation.State
            && retired.TerminalActivationGeneration == publication.Entry.TerminalActivation.Generation
            && Fixed(retired.TerminalActivationChecksum, publication.Entry.TerminalActivation.ControlChecksum)
            && Fixed(retired.CompletionReceiptChecksum, publication.Entry.TerminalActivation.TerminalReceipt.AuthorityChecksum)
            && Fixed(retired.CompletionOperationChecksum,
                Convert.FromHexString(publication.Entry.RetirementOperation.OperationChecksum).ToImmutableArray())
            && SubjectLifetimeEqual(pending.Intent.SubjectLifetime, retired.SubjectLifetime)
            && Fixed(retired.Checksum, BaseSemanticActivationEvidenceContract.RetirementChecksum(retired))
            && Fixed(semantic.Checksum, BaseSemanticActivationEvidenceContract.ReceiptChecksum(semantic))
            && Fixed(semantic.CommitEvidenceChecksum, publication.CommitObservationChecksum)
            && Fixed(publication.LocalReceipt.CommitObservationChecksum, publication.CommitObservationChecksum);
    }

    private static bool FixedKey(BaseSemanticActivationKeyDigest left, BaseSemanticActivationKeyDigest right)
    {
        Span<byte> leftBytes = stackalloc byte[BaseSemanticActivationKeyDigest.Length]; left.CopyTo(leftBytes);
        Span<byte> rightBytes = stackalloc byte[BaseSemanticActivationKeyDigest.Length]; right.CopyTo(rightBytes);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static bool IdentityEqual(BaseMutationRequestIdentity left, BaseMutationRequestIdentity right) =>
        left.Scope == right.Scope && left.Operation == right.Operation
        && left.IdempotencyKey == right.IdempotencyKey
        && CryptographicOperations.FixedTimeEquals(left.Fingerprint.ToArray(), right.Fingerprint.ToArray());

    private static bool SubjectLifetimeEqual(BaseSemanticActivationSubjectLifetimeBinding? left,
        BaseSemanticActivationSubjectLifetimeBinding? right) =>
        left is null ? right is null : right is not null
        && left.ContractId == right.ContractId && left.ContractVersion == right.ContractVersion
        && Fixed(left.ContractChecksum, right.ContractChecksum) && left.SubjectId.Equals(right.SubjectId)
        && left.AuthorityEpoch.Equals(right.AuthorityEpoch)
        && left.Incarnation.Equals(right.Incarnation)
        && Fixed(left.ScopeBindingId, right.ScopeBindingId)
        && Fixed(left.Checksum, right.Checksum);

    /// <summary>Computes the canonical published-head checksum.</summary>
    public static ImmutableArray<byte> PublishedHeadChecksum(BaseSemanticRecoveryPublishedHead value) =>
        Hash("base.semanticRecovery.head.v1\0", writer =>
        {
            writer.Bytes(value.RequestChecksum.AsSpan()); writer.Text(value.ApplicationId); writer.Text(value.LogicalStoreId); writer.I64(value.PublishedSequence);
            writer.Bool(value.HasPendingSuccessor); writer.I64(value.EntryCount); writer.Bytes(value.OrderedEntrySetChecksum.AsSpan());
            writer.Text(value.SigningKeyId); writer.I32(value.SigningKeyVersion);
        });

    /// <summary>Computes the canonical identified cancellation result checksum.</summary>
    public static ImmutableArray<byte> CancellationResultChecksum(BaseSemanticRecoveryCancellationResult value) =>
        Hash("base.semanticRecovery.cancellation.v1\0", writer =>
        {
            writer.Bytes(value.RequestChecksum.AsSpan()); writer.I32((int)value.Disposition); writer.I64(value.Sequence);
        });

    /// <summary>Validates an authority-signed pending ticket.</summary>
    public static bool PendingIsValid(BaseSemanticRecoveryAuthorityDefinition definition,
        BaseSemanticRecoveryPendingTerminalIntent intent, BaseSemanticRecoveryPendingPublication value, DateTimeOffset observedAt)
    {
        try
        {
            return value.CancellationEligibleAt > DateTimeOffset.UnixEpoch && PendingCommitIsValid(definition, intent, value);
        }
        catch { return false; }
    }

    /// <summary>Validates a signed pending ticket after local commit, when pre-commit expiry no longer applies.</summary>
    public static bool PendingCommitIsValid(BaseSemanticRecoveryAuthorityDefinition definition,
        BaseSemanticRecoveryPendingTerminalIntent intent, BaseSemanticRecoveryPendingPublication value)
    {
        try
        {
            BaseSemanticRecoveryRetainedKeyAuthority? signingKey = definition.KeyAuthority.RetainedKeys.SingleOrDefault(key =>
                key.SigningKeyId == value.SigningKeyId && key.SigningKeyVersion == value.SigningKeyVersion);
            return value.Sequence > 0 && !string.IsNullOrWhiteSpace(value.TicketNonce)
                && signingKey is not null
                && Fixed(value.IntentChecksum, intent.Checksum) && Fixed(value.Checksum, PendingChecksum(value))
                && Verify(signingKey.SigningPublicKey,
                    "base.semanticRecovery.pendingSignature.v1\0", value.Checksum, value.Signature);
        }
        catch { return false; }
    }

    /// <summary>Validates an authority-signed current publication head.</summary>
    public static bool PublishedHeadIsValid(BaseSemanticRecoveryAuthorityDefinition definition,
        string applicationId, string logicalStoreId, ImmutableArray<byte> requestChecksum,
        BaseSemanticRecoveryPublishedHead value)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(applicationId) && logicalStoreId == definition.LogicalStoreId
                && requestChecksum.Length == 32 && Fixed(value.RequestChecksum, requestChecksum)
                && value.ApplicationId == applicationId && value.LogicalStoreId == logicalStoreId
                && value.PublishedSequence >= 0 && value.EntryCount >= 0 && value.OrderedEntrySetChecksum.Length == 32
                && value.SigningKeyId == definition.KeyAuthority.CurrentSigningKeyId
                && value.SigningKeyVersion == definition.KeyAuthority.CurrentSigningKeyVersion
                && Fixed(value.Checksum, PublishedHeadChecksum(value)) && Verify(definition.KeyAuthority.CurrentSigningPublicKey,
                    "base.semanticRecovery.headSignature.v1\0", value.Checksum, value.Signature);
        }
        catch { return false; }
    }

    /// <summary>Computes the canonical checksum for an artifact-bound head request.</summary>
    public static ImmutableArray<byte> HeadRequestChecksum(BaseSemanticRecoveryHeadRequest value) =>
        Hash("base.semanticRecovery.headRequest.v1\0", writer =>
        {
            writer.Text(value.ApplicationId); writer.Text(value.LogicalStoreId); writer.Text(value.ArtifactId);
            writer.Bytes(value.ArtifactChecksum.AsSpan()); WriteLimits(writer, value.Limits);
        });

    /// <summary>Validates an artifact-bound head request against installed recovery authority.</summary>
    public static bool HeadRequestIsValid(BaseSemanticRecoveryAuthorityDefinition definition,
        BaseSemanticRecoveryHeadRequest value)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(value.ApplicationId)
                && value.LogicalStoreId == definition.LogicalStoreId
                && !string.IsNullOrWhiteSpace(value.ArtifactId)
                && value.ArtifactChecksum.Length == 32
                && value.Limits == definition.Limits;
        }
        catch { return false; }
    }

    /// <summary>Computes the canonical retained-key checksum.</summary>
    public static ImmutableArray<byte> RetainedKeyChecksum(BaseSemanticRecoveryRetainedKeyAuthority value) =>
        Hash("base.semanticRecovery.retainedKey.v1\0", writer =>
        {
            writer.Text(value.SigningKeyId); writer.I32(value.SigningKeyVersion); writer.Bytes(value.SigningPublicKey.AsSpan());
            writer.Text(value.EncryptionKeyId); writer.I32(value.EncryptionKeyVersion);
            writer.I64(value.NotBefore.ToUnixTimeMilliseconds()); writer.I64(value.RetainUntil.ToUnixTimeMilliseconds());
        });

    /// <summary>Computes the canonical key-authority checksum.</summary>
    public static ImmutableArray<byte> KeyAuthorityChecksum(BaseSemanticRecoveryKeyAuthorityReceipt value) =>
        Hash("base.semanticRecovery.keyAuthority.v1\0", writer =>
        {
            writer.Text(value.AuthorityId); writer.I32(value.AuthorityVersion); writer.I32((int)value.SigningAlgorithm);
            writer.I32((int)value.EncryptionAlgorithm); writer.Text(value.CurrentSigningKeyId); writer.I32(value.CurrentSigningKeyVersion);
            writer.Bytes(value.CurrentSigningPublicKey.AsSpan()); writer.Text(value.CurrentEncryptionKeyId); writer.I32(value.CurrentEncryptionKeyVersion);
            writer.I32(value.RetainedKeys.Length); foreach (BaseSemanticRecoveryRetainedKeyAuthority key in value.RetainedKeys) writer.Bytes(key.Checksum.AsSpan());
            writer.I64(value.MinimumKeyRetention.Ticks);
        });

    /// <summary>Computes the canonical capability checksum.</summary>
    public static ImmutableArray<byte> CapabilityChecksum(BaseSemanticRecoveryAuthorityCapability value) =>
        Hash("base.semanticRecovery.capability.v1\0", writer =>
        {
            writer.Bool(value.DurablePendingSupported); writer.Bool(value.IdentifiedCancellationSupported);
            writer.Bool(value.CommitBoundRetentionSupported); writer.Bool(value.PermanentPendingKeyRetentionSupported);
            writer.I32(value.MaximumEntries); writer.I32(value.MaximumPages);
            writer.I32(value.MaximumPageEntries); writer.I64(value.MaximumRequestBytes); writer.I64(value.MaximumResultBytes);
            writer.I64(value.MaximumTransientBytes); writer.I64(value.MaximumAcquisitionDuration.Ticks);
            writer.I64(value.MaximumResolutionDuration.Ticks); writer.I64(value.MaximumPublicationDuration.Ticks);
            writer.I32(value.MaximumConcurrentOperations);
        });

    /// <summary>Computes the canonical definition checksum.</summary>
    public static ImmutableArray<byte> DefinitionChecksum(BaseSemanticRecoveryAuthorityDefinition value) =>
        Hash("base.semanticRecovery.definition.v1\0", writer =>
        {
            writer.Text(value.Id); writer.I32(value.Version); writer.Text(value.LogicalStoreId); writer.Text(value.OwningModuleId);
            writer.Text(value.RecoveryGrantId);
            writer.Bytes(value.RequiredCapability.CapabilityChecksum.AsSpan()); WriteLimits(writer, value.Limits);
            writer.Bytes(value.KeyAuthority.Checksum.AsSpan());
        });

    /// <summary>Computes the canonical unsigned certification checksum.</summary>
    public static ImmutableArray<byte> CertificationChecksum(BaseSemanticRecoveryAuthorityCertificationReceipt value) =>
        Hash("base.semanticRecovery.certification.v1\0", writer =>
        {
            writer.Text(value.AuthorityId); writer.I32(value.AuthorityVersion); writer.Text(value.ImplementationContractId);
            writer.I32(value.ImplementationContractVersion); writer.Bytes(value.NativeDependencyReceiptChecksum.AsSpan());
            writer.Bytes(value.CapabilityChecksum.AsSpan()); writer.Bytes(value.DefinitionContractChecksum.AsSpan());
            writer.Bytes(value.ExecutedCertificationReportChecksum.AsSpan()); writer.I64(value.ObservationSequence);
        });

    /// <summary>Computes the canonical owned-instance descriptor checksum.</summary>
    public static ImmutableArray<byte> InstanceDescriptorChecksum(BaseSemanticRecoveryAuthorityInstanceDescriptor value) =>
        Hash("base.semanticRecovery.instanceDescriptor.v1\0", writer =>
        {
            writer.Text(value.ImplementationContractId); writer.I32(value.ImplementationContractVersion);
            writer.Bytes(value.CapabilityChecksum.AsSpan()); writer.Bytes(value.KeyAuthorityChecksum.AsSpan());
            writer.Bytes(value.DefinitionChecksum.AsSpan()); writer.Bytes(value.CertificationChecksum.AsSpan());
        });

    /// <summary>Validates complete registration shape, canonical authority, retained-key coverage, and Ed25519 certification.</summary>
    public static bool IsValid(BaseSemanticRecoveryAuthorityRegistration value)
        => IsValidAt(value, null);

    /// <summary>Validates registration authority and current-key coverage at one trusted instant.</summary>
    public static bool IsValidAt(BaseSemanticRecoveryAuthorityRegistration value, DateTimeOffset? observedAt)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(value); ArgumentNullException.ThrowIfNull(value.Definition);
            ArgumentNullException.ThrowIfNull(value.Factory); ArgumentNullException.ThrowIfNull(value.Certification);
            BaseSemanticRecoveryAuthorityDefinition definition = value.Definition;
            BaseSemanticRecoveryKeyAuthorityReceipt keys = definition.KeyAuthority;
            BaseSemanticRecoveryAuthorityCapability capability = definition.RequiredCapability;
            if (!TextValid(definition.Id) || definition.Version <= 0 || !TextValid(definition.LogicalStoreId)
                || !TextValid(definition.OwningModuleId) || !TextValid(definition.RecoveryGrantId)
                || keys.AuthorityId != definition.Id || keys.AuthorityVersion != definition.Version
                || keys.SigningAlgorithm != BaseSemanticRecoverySigningAlgorithm.Ed25519
                || keys.EncryptionAlgorithm != BaseSemanticRecoveryEncryptionAlgorithm.Aes256Gcm
                || !TextValid(keys.CurrentSigningKeyId) || keys.CurrentSigningKeyVersion <= 0
                || keys.CurrentSigningPublicKey.Length != Ed25519.PublicKeySize || !TextValid(keys.CurrentEncryptionKeyId)
                || keys.CurrentEncryptionKeyVersion <= 0 || keys.MinimumKeyRetention <= TimeSpan.Zero
                || !CapabilityValid(capability) || !OperationLimitsValid(definition.Limits, capability)
                || !Fixed(capability.CapabilityChecksum, CapabilityChecksum(capability))) return false;
            string? prior = null; int currentCoverage = 0;
            foreach (BaseSemanticRecoveryRetainedKeyAuthority key in keys.RetainedKeys)
            {
                string order = $"{key.SigningKeyId}\0{key.SigningKeyVersion:D10}\0{key.EncryptionKeyId}\0{key.EncryptionKeyVersion:D10}";
                if (prior is not null && string.CompareOrdinal(prior, order) >= 0 || !TextValid(key.SigningKeyId)
                    || key.SigningKeyVersion <= 0 || key.SigningPublicKey.Length != Ed25519.PublicKeySize
                    || !TextValid(key.EncryptionKeyId) || key.EncryptionKeyVersion <= 0 || key.RetainUntil <= key.NotBefore
                    || key.RetainUntil - key.NotBefore < keys.MinimumKeyRetention
                    || capability.PermanentPendingKeyRetentionSupported && key.RetainUntil != DateTimeOffset.MaxValue
                    || !Fixed(key.Checksum, RetainedKeyChecksum(key))) return false;
                bool current = key.SigningKeyId == keys.CurrentSigningKeyId && key.SigningKeyVersion == keys.CurrentSigningKeyVersion
                    && key.EncryptionKeyId == keys.CurrentEncryptionKeyId && key.EncryptionKeyVersion == keys.CurrentEncryptionKeyVersion
                    && Fixed(key.SigningPublicKey, keys.CurrentSigningPublicKey);
                if (current)
                {
                    currentCoverage++;
                    if (observedAt is { } now && (now < key.NotBefore || now >= key.RetainUntil
                        || key.RetainUntil - now < keys.MinimumKeyRetention)) return false;
                }
                prior = order;
            }
            if (currentCoverage != 1 || !Fixed(keys.Checksum, KeyAuthorityChecksum(keys))
                || !Fixed(definition.ContractChecksum, DefinitionChecksum(definition))) return false;
            BaseSemanticRecoveryAuthorityCertificationReceipt certification = value.Certification;
            if (certification.AuthorityId != definition.Id || certification.AuthorityVersion != definition.Version
                || !TextValid(certification.ImplementationContractId) || certification.ImplementationContractVersion <= 0
                || certification.NativeDependencyReceiptChecksum.Length != 32 || certification.ExecutedCertificationReportChecksum.Length != 32
                || certification.ObservationSequence <= 0 || !Fixed(certification.CapabilityChecksum, capability.CapabilityChecksum)
                || !Fixed(certification.DefinitionContractChecksum, definition.ContractChecksum)
                || !Fixed(certification.Checksum, CertificationChecksum(certification)) || certification.Signature.Length != Ed25519.SignatureSize)
                return false;
            byte[] digest = SHA512.HashData([.. Encoding.UTF8.GetBytes("base.semanticRecovery.certificationSignature.v1\0"), .. certification.Checksum]);
            return Ed25519.Verify(certification.Signature.ToArray(), 0, keys.CurrentSigningPublicKey.ToArray(), 0, digest, 0, digest.Length);
        }
        catch { return false; }
    }

    private static bool CapabilityValid(BaseSemanticRecoveryAuthorityCapability value) =>
        value.DurablePendingSupported && value.IdentifiedCancellationSupported && value.CommitBoundRetentionSupported
        && value.PermanentPendingKeyRetentionSupported
        && value.MaximumEntries > 0 && value.MaximumPages > 0 && value.MaximumPageEntries is > 0 and <= 256
        && value.MaximumPageEntries <= value.MaximumEntries && value.MaximumRequestBytes > 0 && value.MaximumResultBytes > 0
        && value.MaximumTransientBytes > 0 && value.MaximumAcquisitionDuration > TimeSpan.Zero
        && value.MaximumResolutionDuration > TimeSpan.Zero
        && value.MaximumPublicationDuration > TimeSpan.Zero && value.MaximumConcurrentOperations > 0;

    private static bool OperationLimitsValid(BaseSemanticRecoveryOperationLimits value, BaseSemanticRecoveryAuthorityCapability capability) =>
        value.AcquisitionDeadline > TimeSpan.Zero && value.AcquisitionDeadline <= capability.MaximumAcquisitionDuration
        && value.ResolutionDeadline > TimeSpan.Zero && value.ResolutionDeadline <= capability.MaximumResolutionDuration
        && value.PublicationDeadline > TimeSpan.Zero && value.PublicationDeadline <= capability.MaximumPublicationDuration
        && value.MaximumEntries > 0 && value.MaximumEntries <= capability.MaximumEntries
        && value.MaximumPages > 0 && value.MaximumPages <= capability.MaximumPages
        && value.MaximumPageEntries > 0 && value.MaximumPageEntries <= capability.MaximumPageEntries
        && value.MaximumRequestBytes > 0 && value.MaximumRequestBytes <= capability.MaximumRequestBytes
        && value.MaximumResultBytes > 0 && value.MaximumResultBytes <= capability.MaximumResultBytes
        && value.MaximumTransientBytes > 0 && value.MaximumTransientBytes <= capability.MaximumTransientBytes
        && value.MaximumConcurrentOperations > 0 && value.MaximumConcurrentOperations <= capability.MaximumConcurrentOperations;

    private static void WriteLimits(CanonicalWriter writer, BaseSemanticRecoveryOperationLimits value)
    {
        writer.I64(value.AcquisitionDeadline.Ticks); writer.I64(value.ResolutionDeadline.Ticks);
        writer.I64(value.PublicationDeadline.Ticks); writer.I32(value.MaximumEntries); writer.I32(value.MaximumPages);
        writer.I32(value.MaximumPageEntries); writer.I64(value.MaximumRequestBytes); writer.I64(value.MaximumResultBytes);
        writer.I64(value.MaximumTransientBytes); writer.I32(value.MaximumConcurrentOperations);
    }
    private static bool TextValid(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256;
    private static bool Fixed(ImmutableArray<byte> left, ImmutableArray<byte> right) =>
        left.Length == right.Length && left.Length > 0 && CryptographicOperations.FixedTimeEquals(left.AsSpan(), right.AsSpan());
    private static ImmutableArray<byte> Hash(string marker, Action<CanonicalWriter> write)
    {
        using var stream = new MemoryStream(); stream.Write(Encoding.ASCII.GetBytes(marker)); var writer = new CanonicalWriter(stream); write(writer);
        return SHA256.HashData(stream.ToArray()).ToImmutableArray();
    }
    private static bool Verify(ImmutableArray<byte> publicKey, string marker, ImmutableArray<byte> checksum, ImmutableArray<byte> signature)
    {
        if (publicKey.Length != Ed25519.PublicKeySize || checksum.Length != 32 || signature.Length != Ed25519.SignatureSize) return false;
        byte[] digest = SHA512.HashData([.. Encoding.UTF8.GetBytes(marker), .. checksum]);
        return Ed25519.Verify(signature.ToArray(), 0, publicKey.ToArray(), 0, digest, 0, digest.Length);
    }
    private static void Boundary(CanonicalWriter writer, BaseSemanticActivationRecoveryBoundary value)
    {
        writer.Text(value.DefinitionId); writer.Bytes(value.ScopeBindingId.AsSpan());
        Span<byte> key = stackalloc byte[BaseSemanticActivationKeyDigest.Length]; value.Key.CopyTo(key); writer.Bytes(key);
    }

    private static void Identity(CanonicalWriter writer, BaseMutationRequestIdentity value)
    {
        writer.Text(value.Scope); writer.Text(value.Operation); writer.Text(value.IdempotencyKey);
        writer.Bytes(value.Fingerprint.ToArray());
    }
    private sealed class CanonicalWriter(Stream stream)
    {
        internal void Bool(bool value) => stream.WriteByte(value ? (byte)1 : (byte)0);
        internal void I32(int value) { Span<byte> bytes = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, value); stream.Write(bytes); }
        internal void I64(long value) { Span<byte> bytes = stackalloc byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, value); stream.Write(bytes); }
        internal void Text(string value) => Bytes(Encoding.UTF8.GetBytes(value));
        internal void Bytes(ReadOnlySpan<byte> value) { I32(value.Length); stream.Write(value); }
    }
}

/// <summary>Bounds one external semantic-recovery operation.</summary>
public sealed record BaseSemanticRecoveryOperationLimits
{
    /// <summary>Gets the acquisition deadline.</summary>
    public required TimeSpan AcquisitionDeadline { get; init; }
    /// <summary>Gets the identified response-resolution deadline.</summary>
    public required TimeSpan ResolutionDeadline { get; init; }
    /// <summary>Gets the publication deadline.</summary>
    public required TimeSpan PublicationDeadline { get; init; }
    /// <summary>Gets the maximum entries.</summary>
    public required int MaximumEntries { get; init; }
    /// <summary>Gets the maximum pages.</summary>
    public required int MaximumPages { get; init; }
    /// <summary>Gets the maximum entries per page.</summary>
    public required int MaximumPageEntries { get; init; }
    /// <summary>Gets the maximum request bytes.</summary>
    public required long MaximumRequestBytes { get; init; }
    /// <summary>Gets the maximum result bytes.</summary>
    public required long MaximumResultBytes { get; init; }
    /// <summary>Gets the maximum retained transient bytes.</summary>
    public required long MaximumTransientBytes { get; init; }
    /// <summary>Gets the maximum concurrent retained external operations.</summary>
    public required int MaximumConcurrentOperations { get; init; }
}

/// <summary>Requests a bounded read-only preflight of an existing semantic slot before external publication.</summary>
public sealed record BaseSemanticRecoveryPreflightRequest
{
    /// <summary>Gets the exact installed definition.</summary>
    public required BaseSemanticActivationDefinitionIdentity Definition { get; init; }
    /// <summary>Gets the unbound canonical semantic key.</summary>
    public required ImmutableArray<byte> CanonicalKey { get; init; }
    /// <summary>Gets the checksum of the unbound canonical key.</summary>
    public required ImmutableArray<byte> KeyPreimageChecksum { get; init; }
    /// <summary>Gets exact protected scope evidence.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets optional unbound subject-lifetime input finalized against the returned binding.</summary>
    public BaseSemanticRecoverySubjectLifetimePreimage? SubjectLifetime { get; init; }
    /// <summary>Gets the effective provider-and-definition canonical key-byte ceiling.</summary>
    public required int MaximumCanonicalKeyBytes { get; init; }
    /// <summary>Gets the current store authority requirement.</summary>
    public required BaseSemanticActivationStoreAuthorityRequirement StoreAuthority { get; init; }
    /// <summary>Gets effective semantic limits.</summary>
    public required BaseSemanticActivationExecutionLimits Limits { get; init; }
    /// <summary>Gets the bounded preflight deadline.</summary>
    public required TimeSpan Deadline { get; init; }
}

/// <summary>Contains subject-lifetime identity before provider-owned scope binding is resolved.</summary>
public sealed record BaseSemanticRecoverySubjectLifetimePreimage
{
    /// <summary>Gets the exported-subject contract ID.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the positive exported-subject contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the exact exported-subject contract checksum.</summary>
    public required ImmutableArray<byte> ContractChecksum { get; init; }
    /// <summary>Gets the canonical subject ID.</summary>
    public required BaseSubjectId SubjectId { get; init; }
    /// <summary>Gets the authority epoch.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
    /// <summary>Gets the incarnation within the epoch.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
}

/// <summary>Contains non-authoritative read-only preflight evidence that must be recaptured transactionally.</summary>
public sealed record BaseSemanticRecoveryPreflightEvidence
{
    /// <summary>Gets the existing stable scope binding.</summary>
    public required BaseSemanticActivationScopeBinding ScopeBinding { get; init; }
    /// <summary>Gets the bound semantic key.</summary>
    public required BaseSemanticActivationKeyDigest Key { get; init; }
    /// <summary>Gets the exact current live-slot authority.</summary>
    public required BaseSemanticActivationLiveAuthority Live { get; init; }
    /// <summary>Gets the mapped terminal activation generation.</summary>
    public required long ActivationGeneration { get; init; }
    /// <summary>Gets the mapped terminal activation state.</summary>
    public required BaseActivationState ActivationState { get; init; }
    /// <summary>Gets the mapped terminal activation control checksum.</summary>
    public required ImmutableArray<byte> ActivationChecksum { get; init; }
    /// <summary>Gets the exact mapped terminal receipt checksum.</summary>
    public required ImmutableArray<byte> ActivationTerminalReceiptChecksum { get; init; }
    /// <summary>Gets the exact bounded terminal-receipt evidence used to recompute authority and accounting.</summary>
    public required BaseSemanticRecoveryTerminalReceiptEvidence TerminalReceipt { get; init; }
    /// <summary>Gets the complete terminal activation snapshot from the same finite provider snapshot.</summary>
    public required BaseSemanticRecoveryTerminalActivationAuthority TerminalActivation { get; init; }
    /// <summary>Gets nonempty read intervals.</summary>
    public required ImmutableArray<BaseAtomicReadIntervalEvidence> ReadIntervals { get; init; }
    /// <summary>Gets exact accounting.</summary>
    public required BaseSemanticActivationAccounting Accounting { get; init; }
    /// <summary>Gets the canonical preflight checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains the exact private terminal receipt tuple hydrated during preflight.</summary>
public sealed record BaseSemanticRecoveryTerminalReceiptEvidence
{
    /// <summary>Gets the durable receipt key.</summary>
    public required string ReceiptKey { get; init; }
    /// <summary>Gets the closed L51 terminal operation kind.</summary>
    public required string OperationKind { get; init; }
    /// <summary>Gets the exact request fingerprint.</summary>
    public required ImmutableArray<byte> Fingerprint { get; init; }
    /// <summary>Gets the canonical stored transition-result bytes.</summary>
    public required ImmutableArray<byte> ResultBytes { get; init; }
    /// <summary>Gets the result checksum.</summary>
    public required ImmutableArray<byte> ResultChecksum { get; init; }
    /// <summary>Gets the purpose-bound receipt authority checksum.</summary>
    public required ImmutableArray<byte> AuthorityChecksum { get; init; }
}

/// <summary>Captures the exact terminal L51 activation row required by disaster recovery.</summary>
public sealed record BaseSemanticRecoveryTerminalActivationAuthority
{
    /// <summary>Gets the immutable activation payload.</summary>
    public required BaseActivationPayload Payload { get; init; }
    /// <summary>Gets the creation fingerprint.</summary>
    public required ImmutableArray<byte> CreationFingerprint { get; init; }
    /// <summary>Gets the retained priority.</summary>
    public required int Priority { get; init; }
    /// <summary>Gets the optional overlap key.</summary>
    public ImmutableArray<byte>? OverlapKey { get; init; }
    /// <summary>Gets the overlap policy.</summary>
    public required BaseScheduleOverlapPolicy OverlapPolicy { get; init; }
    /// <summary>Gets whether this terminal row is claim-eligible; it must be false.</summary>
    public required bool Eligible { get; init; }
    /// <summary>Gets the exact eligible terminal state.</summary>
    public required BaseActivationState State { get; init; }
    /// <summary>Gets the positive terminal generation.</summary>
    public required long Generation { get; init; }
    /// <summary>Gets the terminal control checksum.</summary>
    public required ImmutableArray<byte> ControlChecksum { get; init; }
    /// <summary>Gets the retained attempt number.</summary>
    public required int AttemptNumber { get; init; }
    /// <summary>Gets the retained claim epoch after claim authority has been cleared.</summary>
    public required long ClaimEpoch { get; init; }
    /// <summary>Gets optional canonical terminal result bytes.</summary>
    public ImmutableArray<byte>? CanonicalResult { get; init; }
    /// <summary>Gets the optional canonical result checksum.</summary>
    public ImmutableArray<byte>? CanonicalResultChecksum { get; init; }
    /// <summary>Gets the exact durable terminal transition receipt.</summary>
    public required BaseSemanticRecoveryTerminalReceiptEvidence TerminalReceipt { get; init; }
    /// <summary>Gets the purpose-bound snapshot checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Provides bounded semantic preflight without opening or retaining a provider transaction.</summary>
public interface IBaseSemanticActivationPreflightStore
{
    /// <summary>Reads one existing semantic slot for a later external-publication handoff.</summary>
    ValueTask<OperationResult<BaseSemanticRecoveryPreflightEvidence>> PreflightSemanticRecoveryAsync(
        BaseSemanticRecoveryPreflightRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Contains the pre-commit terminal publication intent.</summary>
public sealed record BaseSemanticRecoveryPendingTerminalIntent
{
    /// <summary>Gets the stable private semantic boundary.</summary>
    public required BaseSemanticActivationRecoveryBoundary Boundary { get; init; }
    /// <summary>Gets the installed retirement-operation fingerprint.</summary>
    public required ImmutableArray<byte> RetirementOperationFingerprint { get; init; }
    /// <summary>Gets optional complete subject-lifetime authority.</summary>
    public BaseSemanticActivationSubjectLifetimeBinding? SubjectLifetime { get; init; }
    /// <summary>Gets the canonical intent checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Requests one durable pending terminal publication.</summary>
public sealed record BaseSemanticRecoveryBeginRequest
{
    /// <summary>Gets the application ID.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the logical store ID.</summary>
    public required string LogicalStoreId { get; init; }
    /// <summary>Gets the terminal intent.</summary>
    public required BaseSemanticRecoveryPendingTerminalIntent Intent { get; init; }
    /// <summary>Gets the identified request identity.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets effective limits.</summary>
    public required BaseSemanticRecoveryOperationLimits Limits { get; init; }
}

/// <summary>Contains one durable pending publication ticket.</summary>
public sealed record BaseSemanticRecoveryPendingPublication
{
    /// <summary>Gets the reserved positive sequence.</summary>
    public required long Sequence { get; init; }
    /// <summary>Gets the opaque ticket nonce.</summary>
    public required string TicketNonce { get; init; }
    /// <summary>Gets the exact intent checksum.</summary>
    public required ImmutableArray<byte> IntentChecksum { get; init; }
    /// <summary>Gets the signing-key ID used for this immutable ticket.</summary>
    public required string SigningKeyId { get; init; }
    /// <summary>Gets the positive signing-key version used for this immutable ticket.</summary>
    public required int SigningKeyVersion { get; init; }
    /// <summary>Gets the earliest instant at which confirmed rollback may cancel this ticket. Tickets never expire autonomously.</summary>
    public required DateTimeOffset CancellationEligibleAt { get; init; }
    /// <summary>Gets the canonical ticket checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
    /// <summary>Gets the Ed25519 signature.</summary>
    public required ImmutableArray<byte> Signature { get; init; }
}

/// <summary>Binds one certified external pending reservation into the local atomic transaction.</summary>
public sealed record BaseSemanticRecoveryPendingCommitAuthority
{
    /// <summary>Gets the application authority owning the pending publication.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the logical store authority owning the pending publication.</summary>
    public required string LogicalStoreId { get; init; }
    /// <summary>Gets the exact local L37 request scope.</summary>
    public required string LocalScope { get; init; }
    /// <summary>Gets the exact local L37 operation.</summary>
    public required string LocalOperation { get; init; }
    /// <summary>Gets the exact local L37 idempotency key.</summary>
    public required string LocalIdempotencyKey { get; init; }
    /// <summary>Gets the exact local L37 request fingerprint.</summary>
    public required ImmutableArray<byte> LocalFingerprint { get; init; }
    /// <summary>Gets the Runtime-owned structural digest of the exact local operation.</summary>
    public required ImmutableArray<byte> LocalStructuralDigest { get; init; }
    /// <summary>Gets the installed external authority ID.</summary>
    public required string AuthorityId { get; init; }
    /// <summary>Gets the installed external authority version.</summary>
    public required int AuthorityVersion { get; init; }
    /// <summary>Gets the installed external authority checksum.</summary>
    public required ImmutableArray<byte> AuthorityChecksum { get; init; }
    /// <summary>Gets the exact pending intent.</summary>
    public required BaseSemanticRecoveryPendingTerminalIntent Intent { get; init; }
    /// <summary>Gets the exact signed pending ticket.</summary>
    public required BaseSemanticRecoveryPendingPublication Pending { get; init; }
    /// <summary>Gets the canonical binding checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Requests identified resolution of one Begin operation after response loss.</summary>
public sealed record BaseSemanticRecoveryResolvePendingRequest
{
    /// <summary>Gets the application ID.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the logical store ID.</summary>
    public required string LogicalStoreId { get; init; }
    /// <summary>Gets the exact pending terminal intent.</summary>
    public required BaseSemanticRecoveryPendingTerminalIntent Intent { get; init; }
    /// <summary>Gets the original Begin identity.</summary>
    public required BaseMutationRequestIdentity BeginIdentity { get; init; }
    /// <summary>Gets effective limits.</summary>
    public required BaseSemanticRecoveryOperationLimits Limits { get; init; }
}

/// <summary>Classifies the durable state of one identified Begin operation.</summary>
public enum BaseSemanticRecoveryPendingResolutionDisposition
{
    /// <summary>No identified Begin authority exists.</summary>
    Missing = 1,
    /// <summary>The ticket remains pending and may be commit-bound.</summary>
    Pending = 2,
    /// <summary>The ticket was cancelled by confirmed rollback.</summary>
    Cancelled = 3,
    /// <summary>The ticket was finalized.</summary>
    Finalized = 4,
}

/// <summary>Returns the authenticated durable state of one identified Begin operation.</summary>
public sealed record BaseSemanticRecoveryPendingResolution
{
    /// <summary>Gets the exact canonical resolution-request checksum.</summary>
    public required ImmutableArray<byte> RequestChecksum { get; init; }
    /// <summary>Gets the closed durable disposition.</summary>
    public required BaseSemanticRecoveryPendingResolutionDisposition Disposition { get; init; }
    /// <summary>Gets the exact ticket for every disposition except Missing.</summary>
    public required BaseSemanticRecoveryPendingPublication? Pending { get; init; }
    /// <summary>Gets the canonical result checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
    /// <summary>Gets the authority signature over the canonical result checksum.</summary>
    public required ImmutableArray<byte> Signature { get; init; }
}

/// <summary>Requests explicit release of process-local external recovery quarantine after retained work completes.</summary>
public sealed record BaseSemanticRecoveryQuarantineRecoveryRequest
{
    /// <summary>Gets the exact logical store.</summary>
    public required string LogicalStoreId { get; init; }
    /// <summary>Gets the exact ControlPlane principal.</summary>
    public required PrincipalContext Principal { get; init; }
    /// <summary>Gets the identified ControlPlane recovery identity.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
}

/// <summary>Reports explicit process-local quarantine recovery.</summary>
public sealed record BaseSemanticRecoveryQuarantineRecoveryResult
{
    /// <summary>Gets whether quarantine was released.</summary>
    public required bool Released { get; init; }
    /// <summary>Gets the remaining retained late-work count.</summary>
    public required long RetainedLateWork { get; init; }
}

/// <summary>Persists the complete recovery handoff in the one outer module receipt.</summary>
public sealed record BaseSemanticRecoveryLocalReceiptAuthority
{
    /// <summary>Gets the transaction-bound pending authority.</summary>
    public required BaseSemanticRecoveryPendingCommitAuthority PendingAuthority { get; init; }
    /// <summary>Gets the exact terminal entry resulting from local commit.</summary>
    public required BaseSemanticActivationRecoveryEntry FinalEntry { get; init; }
    /// <summary>Gets the canonical local handoff checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains one exact finalized semantic recovery entry.</summary>
public sealed record BaseSemanticActivationRecoveryEntry
{
    /// <summary>Gets its strict ordering boundary.</summary>
    public required BaseSemanticActivationRecoveryBoundary Boundary { get; init; }
    /// <summary>Gets the protected scope-directory authority required to resolve this boundary.</summary>
    public required BaseSemanticActivationScopeBinding ScopeBinding { get; init; }
    /// <summary>Gets the exact terminal activation snapshot that dominates an older artifact row.</summary>
    public required BaseSemanticRecoveryTerminalActivationAuthority TerminalActivation { get; init; }
    /// <summary>Gets the exact installed completion operation that authorized retirement.</summary>
    public required BaseSemanticActivationModuleOperationIdentity RetirementOperation { get; init; }
    /// <summary>Gets the exact definition authority.</summary>
    public required BaseSemanticActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets the terminal slot state.</summary>
    public required BaseSemanticActivationSlotState State { get; init; }
    /// <summary>Gets the positive slot generation.</summary>
    public required long SlotGeneration { get; init; }
    /// <summary>Gets the exact protected authority payload bytes.</summary>
    public required ImmutableArray<byte> AuthorityBytes { get; init; }
    /// <summary>Gets the canonical entry checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Finalizes one commit-bound pending publication.</summary>
public sealed record BaseSemanticRecoveryFinalizeRequest
{
    /// <summary>Gets the application authority owning the finalized publication.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the logical store authority owning the finalized publication.</summary>
    public required string LogicalStoreId { get; init; }
    /// <summary>Gets the pending ticket.</summary>
    public required BaseSemanticRecoveryPendingPublication Pending { get; init; }
    /// <summary>Gets the exact post-commit entry.</summary>
    public required BaseSemanticActivationRecoveryEntry FinalEntry { get; init; }
    /// <summary>Gets the complete bounded local L37 receipt envelope.</summary>
    public required BaseSemanticRecoveryLocalReceiptEnvelope LocalReceipt { get; init; }
    /// <summary>Gets the local commit-observation checksum.</summary>
    public required ImmutableArray<byte> CommitObservationChecksum { get; init; }
    /// <summary>Gets the identified request identity.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets effective limits.</summary>
    public required BaseSemanticRecoveryOperationLimits Limits { get; init; }
}

/// <summary>Retains the complete canonical L37 receipt and request key needed for disaster replay.</summary>
public sealed record BaseSemanticRecoveryLocalReceiptEnvelope
{
    /// <summary>Gets the exact identified local request.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets the local request structural digest.</summary>
    public required ImmutableArray<byte> StructuralDigest { get; init; }
    /// <summary>Gets canonical source-generated <see cref="BaseAtomicReceiptWire"/> bytes.</summary>
    public required ImmutableArray<byte> ReceiptBytes { get; init; }
    /// <summary>Gets SHA-256 of the canonical receipt bytes.</summary>
    public required ImmutableArray<byte> ReceiptChecksum { get; init; }
    /// <summary>Gets the fixed durable receipt format version.</summary>
    public required int ReceiptFormatVersion { get; init; }
    /// <summary>Gets the historical schema generation under which the receipt committed.</summary>
    public required long SchemaGeneration { get; init; }
    /// <summary>Gets the historical provider store-instance authority.</summary>
    public required string StoreInstanceId { get; init; }
    /// <summary>Gets the exact committed instant.</summary>
    public required DateTimeOffset CommittedAt { get; init; }
    /// <summary>Gets the original durable receipt expiration.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
    /// <summary>Gets the exact outer semantic commit-observation checksum.</summary>
    public required ImmutableArray<byte> CommitObservationChecksum { get; init; }
    /// <summary>Gets the purpose-bound envelope checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Proves finalization of the exact requested pending publication.</summary>
public sealed record BaseSemanticRecoveryFinalizationResult
{
    /// <summary>Gets the exact canonical finalize-request checksum.</summary>
    public required ImmutableArray<byte> RequestChecksum { get; init; }
    /// <summary>Gets the authenticated head resulting from the exact finalization.</summary>
    public required BaseSemanticRecoveryPublishedHead Head { get; init; }
    /// <summary>Gets the canonical result checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
    /// <summary>Gets the authority signature over the canonical result checksum.</summary>
    public required ImmutableArray<byte> Signature { get; init; }
}

/// <summary>Cancels one pre-commit pending publication after confirmed rollback.</summary>
public sealed record BaseSemanticRecoveryCancelRequest
{
    /// <summary>Gets the pending ticket.</summary>
    public required BaseSemanticRecoveryPendingPublication Pending { get; init; }
    /// <summary>Gets the provider-validated confirmed rollback proof checksum.</summary>
    public required ImmutableArray<byte> ConfirmedRollbackProofChecksum { get; init; }
    /// <summary>Gets the identified request identity.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets effective limits.</summary>
    public required BaseSemanticRecoveryOperationLimits Limits { get; init; }
}

/// <summary>Classifies cancellation of a pending publication.</summary>
public enum BaseSemanticRecoveryCancellationDisposition
{
    /// <summary>The pre-commit ticket was cancelled.</summary>
    Cancelled = 1,
    /// <summary>The same cancellation was already committed.</summary>
    AlreadyCancelled = 2,
    /// <summary>The publication was already finalized.</summary>
    AlreadyFinalized = 3,
    /// <summary>Local commit authority prevents cancellation.</summary>
    CommitBoundPending = 4,
}

/// <summary>Reports identified pending-publication cancellation.</summary>
public sealed record BaseSemanticRecoveryCancellationResult
{
    /// <summary>Gets the exact canonical cancel-request checksum.</summary>
    public required ImmutableArray<byte> RequestChecksum { get; init; }
    /// <summary>Gets the cancellation disposition.</summary>
    public required BaseSemanticRecoveryCancellationDisposition Disposition { get; init; }
    /// <summary>Gets the affected sequence.</summary>
    public required long Sequence { get; init; }
    /// <summary>Gets the canonical result checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
    /// <summary>Gets the authority signature over the canonical result checksum.</summary>
    public required ImmutableArray<byte> Signature { get; init; }
}

/// <summary>Requests the authenticated current external publication head.</summary>
public sealed record BaseSemanticRecoveryHeadRequest
{
    /// <summary>Gets the application ID.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the logical store ID.</summary>
    public required string LogicalStoreId { get; init; }
    /// <summary>Gets the authenticated artifact ID.</summary>
    public required string ArtifactId { get; init; }
    /// <summary>Gets the authenticated artifact checksum.</summary>
    public required ImmutableArray<byte> ArtifactChecksum { get; init; }
    /// <summary>Gets effective limits.</summary>
    public required BaseSemanticRecoveryOperationLimits Limits { get; init; }
}

/// <summary>Contains the authenticated current external publication head.</summary>
public sealed record BaseSemanticRecoveryPublishedHead
{
    /// <summary>Gets the checksum of the exact artifact-bound head request.</summary>
    public required ImmutableArray<byte> RequestChecksum { get; init; }
    /// <summary>Gets the application ID.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the logical store ID.</summary>
    public required string LogicalStoreId { get; init; }
    /// <summary>Gets the latest contiguous finalized sequence.</summary>
    public required long PublishedSequence { get; init; }
    /// <summary>Gets whether the next sequence is pending.</summary>
    public required bool HasPendingSuccessor { get; init; }
    /// <summary>Gets the finalized entry count.</summary>
    public required long EntryCount { get; init; }
    /// <summary>Gets the ordered finalized-entry set checksum.</summary>
    public required ImmutableArray<byte> OrderedEntrySetChecksum { get; init; }
    /// <summary>Gets the signing-key ID.</summary>
    public required string SigningKeyId { get; init; }
    /// <summary>Gets the signing-key version.</summary>
    public required int SigningKeyVersion { get; init; }
    /// <summary>Gets the canonical head checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
    /// <summary>Gets the Ed25519 signature.</summary>
    public required ImmutableArray<byte> Signature { get; init; }
}

/// <summary>Requests one strict page of finalized external recovery publications.</summary>
public sealed record BaseSemanticRecoveryPageRequest
{
    /// <summary>Gets the authenticated head.</summary>
    public required BaseSemanticRecoveryPublishedHead Head { get; init; }
    /// <summary>Gets the exclusive prior sequence.</summary>
    public required long AfterSequence { get; init; }
    /// <summary>Gets the requested page size.</summary>
    public required int Take { get; init; }
    /// <summary>Gets effective limits.</summary>
    public required BaseSemanticRecoveryOperationLimits Limits { get; init; }
}

/// <summary>Contains one finalized external recovery publication.</summary>
public sealed record BaseSemanticRecoveryPublicationEntry
{
    /// <summary>Gets its positive sequence.</summary>
    public required long Sequence { get; init; }
    /// <summary>Gets the finalized semantic entry.</summary>
    public required BaseSemanticActivationRecoveryEntry Entry { get; init; }
    /// <summary>Gets the complete bounded local L37 receipt envelope.</summary>
    public required BaseSemanticRecoveryLocalReceiptEnvelope LocalReceipt { get; init; }
    /// <summary>Gets the local commit-observation checksum.</summary>
    public required ImmutableArray<byte> CommitObservationChecksum { get; init; }
    /// <summary>Gets the canonical publication checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains one strict finalized-publication page.</summary>
public sealed record BaseSemanticRecoveryPublicationPage
{
    /// <summary>Gets the exclusive requested sequence.</summary>
    public required long AfterSequence { get; init; }
    /// <summary>Gets ordered publications.</summary>
    public required ImmutableArray<BaseSemanticRecoveryPublicationEntry> Entries { get; init; }
    /// <summary>Gets the next exclusive sequence, or null at the head.</summary>
    public required long? NextAfterSequence { get; init; }
    /// <summary>Gets the bound head sequence.</summary>
    public required long HeadSequence { get; init; }
    /// <summary>Gets the canonical page checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Provides one closed external semantic-recovery administration SPI.</summary>
public interface IBaseSemanticActivationRecoveryAuthority
{
    /// <summary>Gets immutable certified implementation correspondence.</summary>
    BaseSemanticRecoveryAuthorityInstanceDescriptor Descriptor { get; }
    /// <summary>Durably reserves one pending terminal sequence.</summary>
    ValueTask<BaseResult<BaseSemanticRecoveryPendingPublication>> BeginAsync(BaseSemanticRecoveryBeginRequest request, CancellationToken cancellationToken);
    /// <summary>Resolves one identified Begin operation without allocating another sequence.</summary>
    ValueTask<BaseResult<BaseSemanticRecoveryPendingResolution>> ResolvePendingAsync(BaseSemanticRecoveryResolvePendingRequest request, CancellationToken cancellationToken);
    /// <summary>Finalizes one commit-bound terminal publication.</summary>
    ValueTask<BaseResult<BaseSemanticRecoveryFinalizationResult>> FinalizeAsync(BaseSemanticRecoveryFinalizeRequest request, CancellationToken cancellationToken);
    /// <summary>Cancels one confirmed-uncommitted pending publication.</summary>
    ValueTask<BaseResult<BaseSemanticRecoveryCancellationResult>> CancelAsync(BaseSemanticRecoveryCancelRequest request, CancellationToken cancellationToken);
    /// <summary>Reads the authenticated current publication head.</summary>
    ValueTask<BaseResult<BaseSemanticRecoveryPublishedHead>> ReadHeadAsync(BaseSemanticRecoveryHeadRequest request, CancellationToken cancellationToken);
    /// <summary>Reads one authenticated finalized-publication page.</summary>
    ValueTask<BaseResult<BaseSemanticRecoveryPublicationPage>> ReadPageAsync(BaseSemanticRecoveryPageRequest request, CancellationToken cancellationToken);
}
