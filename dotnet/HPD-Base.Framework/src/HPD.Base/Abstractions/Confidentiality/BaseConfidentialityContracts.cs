using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Classifies the maximum permitted disclosure of a declared field.</summary>
public enum BaseFieldConfidentiality
{
    /// <summary>Ordinary public data.</summary>
    Public,
    /// <summary>Authenticated application/control-plane data.</summary>
    Internal,
    /// <summary>Explicitly projected confidential data.</summary>
    Confidential,
    /// <summary>Projection-only secret data.</summary>
    Secret
}
/// <summary>Controls ordinary record disclosure.</summary>
public enum BaseRecordDisclosure
{
    /// <summary>Includes the typed value.</summary>
    Include,
    /// <summary>Omits the property.</summary>
    Omit,
    /// <summary>Emits the structural marker.</summary>
    FixedMarker
}
/// <summary>Protects authoritative history required for replay and recovery.</summary>
public enum BaseHistoryProtection
{
    /// <summary>Retains authoritative state.</summary>
    AuthoritativeRequired
}
/// <summary>Controls one outward projection channel.</summary>
public enum BaseProjectionDisclosure
{
    /// <summary>Includes the typed value.</summary>
    Include,
    /// <summary>Omits the property.</summary>
    Omit,
    /// <summary>Emits the structural marker.</summary>
    FixedMarker
}
/// <summary>Protects authoritative backup state.</summary>
public enum BaseAuthoritativeBackupProtection
{
    /// <summary>Preserves authoritative state.</summary>
    PreserveAuthoritativeValue
}
/// <summary>Controls whether and how a field may influence an index.</summary>
public enum BaseIndexDisclosure
{
    /// <summary>Forbids index influence.</summary>
    Forbidden,
    /// <summary>Permits equality influence only.</summary>
    EqualityOnly,
    /// <summary>Permits declared operators.</summary>
    DeclaredOperators
}

/// <summary>Defines the complete immutable disclosure contract for one field.</summary>
public sealed record BaseFieldDisclosurePolicy
{
    /// <summary>Gets ordinary record disclosure.</summary>
    public required BaseRecordDisclosure RecordRead { get; init; }
    /// <summary>Gets authoritative history protection.</summary>
    public required BaseHistoryProtection AuthoritativeHistory { get; init; }
    /// <summary>Gets event disclosure.</summary>
    public required BaseProjectionDisclosure Event { get; init; }
    /// <summary>Gets realtime disclosure.</summary>
    public required BaseProjectionDisclosure Realtime { get; init; }
    /// <summary>Gets diagnostic disclosure.</summary>
    public required BaseProjectionDisclosure Diagnostic { get; init; }
    /// <summary>Gets authoritative backup protection.</summary>
    public required BaseAuthoritativeBackupProtection AuthoritativeBackup { get; init; }
    /// <summary>Gets administrative export disclosure.</summary>
    public required BaseProjectionDisclosure AdministrativeDataExport { get; init; }
    /// <summary>Gets ordinary export disclosure.</summary>
    public required BaseProjectionDisclosure OrdinaryDataExport { get; init; }
    /// <summary>Gets index disclosure.</summary>
    public required BaseIndexDisclosure Indexing { get; init; }
}

/// <summary>Provides immutable normative disclosure defaults.</summary>
public static class BaseFieldDisclosurePolicies
{
    /// <summary>Gets the normative policy for <paramref name="confidentiality"/>.</summary>
    public static BaseFieldDisclosurePolicy For(BaseFieldConfidentiality confidentiality) => BaseConfidentialityPolicy.Default(confidentiality) with { };
}

/// <summary>Represents the one structural redaction value emitted by BASE.</summary>
[JsonConverter(typeof(BaseRedactedJsonConverter))]
public sealed class BaseRedacted
{
    private BaseRedacted() { }
    /// <summary>Gets the singleton redaction marker.</summary>
    public static BaseRedacted Value { get; } = new();
    /// <inheritdoc />
    public override string ToString() => nameof(BaseRedacted);
}

internal sealed class BaseRedactedJsonConverter : JsonConverter<BaseRedacted>
{
    public override BaseRedacted Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.GetRawText() != "{\"$base\":\"redacted\"}")
            throw new JsonException("base.confidentiality.redactedMarkerForbidden");
        return BaseRedacted.Value;
    }
    public override void Write(Utf8JsonWriter writer, BaseRedacted value, JsonSerializerOptions options)
    {
        writer.WriteStartObject(); writer.WriteString("$base", "redacted"); writer.WriteEndObject();
    }
}

/// <summary>Describes an encryption guarantee without exposing provider secrets.</summary>
public enum BaseStorageEncryptionGuarantee
{
    /// <summary>No encryption guarantee.</summary>
    None,
    /// <summary>Host-declared guarantee.</summary>
    HostDeclared,
    /// <summary>Provider-declared guarantee.</summary>
    ProviderDeclared,
    /// <summary>Operationally verified provider guarantee.</summary>
    ProviderVerified
}
/// <summary>Describes protection of one storage surface.</summary>
public enum BaseStorageProtectionState
{
    /// <summary>The surface is protected.</summary>
    Protected,
    /// <summary>The surface is unprotected.</summary>
    Unprotected,
    /// <summary>The surface is not retained.</summary>
    NotRetained,
    /// <summary>The module does not own the surface.</summary>
    NotApplicable
}
/// <summary>Identifies who owns storage keys.</summary>
public enum BaseStorageKeyOwner
{
    /// <summary>No key owner.</summary>
    None,
    /// <summary>The host owns keys.</summary>
    Host,
    /// <summary>The provider owns keys.</summary>
    Provider,
    /// <summary>An external authority owns keys.</summary>
    ExternalAuthority
}
/// <summary>Describes supported key rotation.</summary>
public enum BaseStorageRotationSupport
{
    /// <summary>No rotation support.</summary>
    None,
    /// <summary>Offline rotation support.</summary>
    Offline,
    /// <summary>Online rotation support.</summary>
    Online,
    /// <summary>Externally managed rotation.</summary>
    ExternallyManaged
}
/// <summary>Describes how strongly a protection claim was verified.</summary>
public enum BaseStorageVerificationStatus
{
    /// <summary>Unverified claim.</summary>
    Unverified,
    /// <summary>Configuration-validated claim.</summary>
    ConfigurationValidated,
    /// <summary>Operationally verified claim.</summary>
    OperationallyVerified
}

/// <summary>Describes protection of every closed BASE storage surface.</summary>
public sealed record BaseStorageProtectionCoverage
{
    /// <summary>Gets authoritative-record protection.</summary>
    public required BaseStorageProtectionState AuthoritativeRecords { get; init; }
    /// <summary>Gets journal protection.</summary>
    public required BaseStorageProtectionState Journal { get; init; }
    /// <summary>Gets receipt protection.</summary>
    public required BaseStorageProtectionState Receipts { get; init; }
    /// <summary>Gets provider-state protection.</summary>
    public required BaseStorageProtectionState ProviderState { get; init; }
    /// <summary>Gets index protection.</summary>
    public required BaseStorageProtectionState Indexes { get; init; }
    /// <summary>Gets temporary-file protection.</summary>
    public required BaseStorageProtectionState TemporaryFiles { get; init; }
    /// <summary>Gets authoritative-backup protection.</summary>
    public required BaseStorageProtectionState AuthoritativeBackups { get; init; }
    /// <summary>Gets administrative-export protection.</summary>
    public required BaseStorageProtectionState AdministrativeExports { get; init; }
    /// <summary>Gets ordinary-export protection.</summary>
    public required BaseStorageProtectionState OrdinaryExports { get; init; }
    /// <summary>Gets external file/blob protection.</summary>
    public required BaseStorageProtectionState ExternalFilesAndBlobs { get; init; }
}

/// <summary>Provides a frozen protection capability for one owning module.</summary>
public sealed record BaseStorageProtectionCapability
{
    /// <summary>Gets the stable owning module identifier.</summary>
    public required string OwningModuleId { get; init; }
    /// <summary>Gets the encryption guarantee.</summary>
    public required BaseStorageEncryptionGuarantee Guarantee { get; init; }
    /// <summary>Gets surface coverage.</summary>
    public required BaseStorageProtectionCoverage Coverage { get; init; }
    /// <summary>Gets key ownership.</summary>
    public required BaseStorageKeyOwner KeyOwner { get; init; }
    /// <summary>Gets rotation support.</summary>
    public required BaseStorageRotationSupport Rotation { get; init; }
    /// <summary>Gets verification status.</summary>
    public required BaseStorageVerificationStatus Verification { get; init; }
}

/// <summary>Defines permitted protection states for every closed storage surface.</summary>
public sealed record BaseStorageProtectionCoverageRequirement
{
    /// <summary>Gets permitted authoritative-record states.</summary>
    public required ImmutableArray<BaseStorageProtectionState> AuthoritativeRecords { get; init; }
    /// <summary>Gets permitted journal states.</summary>
    public required ImmutableArray<BaseStorageProtectionState> Journal { get; init; }
    /// <summary>Gets permitted receipt states.</summary>
    public required ImmutableArray<BaseStorageProtectionState> Receipts { get; init; }
    /// <summary>Gets permitted provider-state states.</summary>
    public required ImmutableArray<BaseStorageProtectionState> ProviderState { get; init; }
    /// <summary>Gets permitted index states.</summary>
    public required ImmutableArray<BaseStorageProtectionState> Indexes { get; init; }
    /// <summary>Gets permitted temporary-file states.</summary>
    public required ImmutableArray<BaseStorageProtectionState> TemporaryFiles { get; init; }
    /// <summary>Gets permitted backup states.</summary>
    public required ImmutableArray<BaseStorageProtectionState> AuthoritativeBackups { get; init; }
    /// <summary>Gets permitted administrative-export states.</summary>
    public required ImmutableArray<BaseStorageProtectionState> AdministrativeExports { get; init; }
    /// <summary>Gets permitted ordinary-export states.</summary>
    public required ImmutableArray<BaseStorageProtectionState> OrdinaryExports { get; init; }
    /// <summary>Gets permitted external file/blob states.</summary>
    public required ImmutableArray<BaseStorageProtectionState> ExternalFilesAndBlobs { get; init; }
}

/// <summary>Requires a closed storage-protection contract from one owning module.</summary>
public sealed record BaseStorageProtectionRequirement
{
    /// <summary>Gets the stable owning module identifier.</summary>
    public required string OwningModuleId { get; init; }
    /// <summary>Gets permitted encryption guarantees.</summary>
    public required ImmutableArray<BaseStorageEncryptionGuarantee> PermittedGuarantees { get; init; }
    /// <summary>Gets permitted surface states.</summary>
    public required BaseStorageProtectionCoverageRequirement Coverage { get; init; }
    /// <summary>Gets permitted key owners.</summary>
    public required ImmutableArray<BaseStorageKeyOwner> PermittedKeyOwners { get; init; }
    /// <summary>Gets required rotation support.</summary>
    public required BaseStorageRotationSupport RequiredRotation { get; init; }
    /// <summary>Gets minimum verification.</summary>
    public required BaseStorageVerificationStatus MinimumVerification { get; init; }
}

/// <summary>Provides stable L42 failure codes.</summary>
public static class BaseConfidentialityErrorCodes
{
    /// <summary>Invalid contract.</summary>
    public const string ContractInvalid = "base.confidentiality.contractInvalid";
    /// <summary>Forbidden disclosure.</summary>
    public const string DisclosureForbidden = "base.confidentiality.disclosureForbidden";
    /// <summary>Missing provider capability.</summary>
    public const string ProviderCapabilityMissing = "base.confidentiality.providerCapabilityMissing";
    /// <summary>Insufficient storage protection.</summary>
    public const string StorageProtectionInsufficient = "base.confidentiality.storageProtectionInsufficient";
    /// <summary>Invalid descriptor.</summary>
    public const string StorageDescriptorInvalid = "base.confidentiality.storageDescriptorInvalid";
    /// <summary>Conflicting requirement.</summary>
    public const string StorageRequirementConflict = "base.confidentiality.storageRequirementConflict";
    /// <summary>Invalid requirement.</summary>
    public const string StorageRequirementInvalid = "base.confidentiality.storageRequirementInvalid";
    /// <summary>Duplicate requirement.</summary>
    public const string StorageRequirementDuplicate = "base.confidentiality.storageRequirementDuplicate";
    /// <summary>Missing owner.</summary>
    public const string StorageRequirementOwnerMissing = "base.confidentiality.storageRequirementOwnerMissing";
    /// <summary>Late contribution.</summary>
    public const string StorageRequirementLate = "base.confidentiality.storageRequirementLate";
    /// <summary>Forbidden marker input.</summary>
    public const string RedactedMarkerForbidden = "base.confidentiality.redactedMarkerForbidden";
}

/// <summary>Provides stable binary-field failure codes.</summary>
public static class BaseBinaryErrorCodes
{
    /// <summary>Value exceeds its bound.</summary>
    public const string ValueTooLarge = "base.binary.valueTooLarge";
    /// <summary>Encoding is noncanonical.</summary>
    public const string EncodingInvalid = "base.binary.encodingInvalid";
    /// <summary>Operator is unsupported.</summary>
    public const string OperatorUnsupported = "base.binary.operatorUnsupported";
}
