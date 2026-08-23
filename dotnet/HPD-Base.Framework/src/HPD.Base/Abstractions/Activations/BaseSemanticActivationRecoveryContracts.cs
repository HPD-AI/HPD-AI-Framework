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
    /// <summary>Gets the maximum publication duration.</summary>
    public required TimeSpan MaximumPublicationDuration { get; init; }
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
    /// <summary>Gets the required finite capability.</summary>
    public required BaseSemanticRecoveryAuthorityCapability RequiredCapability { get; init; }
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
            writer.Bool(value.CommitBoundRetentionSupported); writer.I32(value.MaximumEntries); writer.I32(value.MaximumPages);
            writer.I32(value.MaximumPageEntries); writer.I64(value.MaximumRequestBytes); writer.I64(value.MaximumResultBytes);
            writer.I64(value.MaximumTransientBytes); writer.I64(value.MaximumAcquisitionDuration.Ticks); writer.I64(value.MaximumPublicationDuration.Ticks);
        });

    /// <summary>Computes the canonical definition checksum.</summary>
    public static ImmutableArray<byte> DefinitionChecksum(BaseSemanticRecoveryAuthorityDefinition value) =>
        Hash("base.semanticRecovery.definition.v1\0", writer =>
        {
            writer.Text(value.Id); writer.I32(value.Version); writer.Text(value.LogicalStoreId); writer.Text(value.OwningModuleId);
            writer.Bytes(value.RequiredCapability.CapabilityChecksum.AsSpan()); writer.Bytes(value.KeyAuthority.Checksum.AsSpan());
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
                || !TextValid(definition.OwningModuleId) || keys.AuthorityId != definition.Id || keys.AuthorityVersion != definition.Version
                || keys.SigningAlgorithm != BaseSemanticRecoverySigningAlgorithm.Ed25519
                || keys.EncryptionAlgorithm != BaseSemanticRecoveryEncryptionAlgorithm.Aes256Gcm
                || !TextValid(keys.CurrentSigningKeyId) || keys.CurrentSigningKeyVersion <= 0
                || keys.CurrentSigningPublicKey.Length != Ed25519.PublicKeySize || !TextValid(keys.CurrentEncryptionKeyId)
                || keys.CurrentEncryptionKeyVersion <= 0 || keys.MinimumKeyRetention <= TimeSpan.Zero
                || !CapabilityValid(capability) || !Fixed(capability.CapabilityChecksum, CapabilityChecksum(capability))) return false;
            string? prior = null; int currentCoverage = 0;
            foreach (BaseSemanticRecoveryRetainedKeyAuthority key in keys.RetainedKeys)
            {
                string order = $"{key.SigningKeyId}\0{key.SigningKeyVersion:D10}\0{key.EncryptionKeyId}\0{key.EncryptionKeyVersion:D10}";
                if (prior is not null && string.CompareOrdinal(prior, order) >= 0 || !TextValid(key.SigningKeyId)
                    || key.SigningKeyVersion <= 0 || key.SigningPublicKey.Length != Ed25519.PublicKeySize
                    || !TextValid(key.EncryptionKeyId) || key.EncryptionKeyVersion <= 0 || key.RetainUntil <= key.NotBefore
                    || key.RetainUntil - key.NotBefore < keys.MinimumKeyRetention
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
        && value.MaximumEntries > 0 && value.MaximumPages > 0 && value.MaximumPageEntries is > 0 and <= 256
        && value.MaximumPageEntries <= value.MaximumEntries && value.MaximumRequestBytes > 0 && value.MaximumResultBytes > 0
        && value.MaximumTransientBytes > 0 && value.MaximumAcquisitionDuration > TimeSpan.Zero
        && value.MaximumPublicationDuration > TimeSpan.Zero;
    private static bool TextValid(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256;
    private static bool Fixed(ImmutableArray<byte> left, ImmutableArray<byte> right) =>
        left.Length == right.Length && left.Length > 0 && CryptographicOperations.FixedTimeEquals(left.AsSpan(), right.AsSpan());
    private static ImmutableArray<byte> Hash(string marker, Action<CanonicalWriter> write)
    {
        using var stream = new MemoryStream(); stream.Write(Encoding.ASCII.GetBytes(marker)); var writer = new CanonicalWriter(stream); write(writer);
        return SHA256.HashData(stream.ToArray()).ToImmutableArray();
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
    public required BaseSemanticActivationSubjectLifetimeBinding? SubjectLifetime { get; init; }
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
    /// <summary>Gets the expiry applicable only before local commit binding.</summary>
    public required DateTimeOffset PreCommitExpiresAt { get; init; }
    /// <summary>Gets the canonical ticket checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
    /// <summary>Gets the Ed25519 signature.</summary>
    public required ImmutableArray<byte> Signature { get; init; }
}

/// <summary>Contains one exact finalized semantic recovery entry.</summary>
public sealed record BaseSemanticActivationRecoveryEntry
{
    /// <summary>Gets its strict ordering boundary.</summary>
    public required BaseSemanticActivationRecoveryBoundary Boundary { get; init; }
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
    /// <summary>Gets the pending ticket.</summary>
    public required BaseSemanticRecoveryPendingPublication Pending { get; init; }
    /// <summary>Gets the exact post-commit entry.</summary>
    public required BaseSemanticActivationRecoveryEntry FinalEntry { get; init; }
    /// <summary>Gets the local receipt checksum.</summary>
    public required ImmutableArray<byte> LocalReceiptChecksum { get; init; }
    /// <summary>Gets the local commit-observation checksum.</summary>
    public required ImmutableArray<byte> CommitObservationChecksum { get; init; }
    /// <summary>Gets the identified request identity.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets effective limits.</summary>
    public required BaseSemanticRecoveryOperationLimits Limits { get; init; }
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
    /// <summary>Gets the cancellation disposition.</summary>
    public required BaseSemanticRecoveryCancellationDisposition Disposition { get; init; }
    /// <summary>Gets the affected sequence.</summary>
    public required long Sequence { get; init; }
    /// <summary>Gets the canonical result checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
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
    /// <summary>Gets the local receipt checksum.</summary>
    public required ImmutableArray<byte> LocalReceiptChecksum { get; init; }
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
    /// <summary>Finalizes one commit-bound terminal publication.</summary>
    ValueTask<BaseResult<BaseSemanticRecoveryPublishedHead>> FinalizeAsync(BaseSemanticRecoveryFinalizeRequest request, CancellationToken cancellationToken);
    /// <summary>Cancels one confirmed-uncommitted pending publication.</summary>
    ValueTask<BaseResult<BaseSemanticRecoveryCancellationResult>> CancelAsync(BaseSemanticRecoveryCancelRequest request, CancellationToken cancellationToken);
    /// <summary>Reads the authenticated current publication head.</summary>
    ValueTask<BaseResult<BaseSemanticRecoveryPublishedHead>> ReadHeadAsync(BaseSemanticRecoveryHeadRequest request, CancellationToken cancellationToken);
    /// <summary>Reads one authenticated finalized-publication page.</summary>
    ValueTask<BaseResult<BaseSemanticRecoveryPublicationPage>> ReadPageAsync(BaseSemanticRecoveryPageRequest request, CancellationToken cancellationToken);
}
