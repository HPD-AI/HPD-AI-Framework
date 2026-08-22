using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Classifies one semantic activation operation carried by a module mutation.</summary>
public enum BaseSemanticActivationOperationKind
{
    /// <summary>Ensures that the logical semantic activation exists.</summary>
    Ensure = 1,
    /// <summary>Retires a terminal logical semantic activation.</summary>
    Retire = 2,
}

/// <summary>Classifies durable semantic-slot state.</summary>
public enum BaseSemanticActivationSlotState
{
    /// <summary>The slot maps to one live activation lifetime.</summary>
    Live = 1,
    /// <summary>The mapped activation was terminally retired.</summary>
    Retired = 2,
    /// <summary>Detailed retirement evidence was compacted into permanent absence authority.</summary>
    CompactedAbsent = 3,
}

/// <summary>Classifies the outcome of ensuring one semantic activation.</summary>
public enum BaseSemanticActivationEnsureDisposition
{
    /// <summary>The operation created the slot and activation.</summary>
    Created = 1,
    /// <summary>The operation resolved the existing live activation.</summary>
    Existing = 2,
    /// <summary>The identity is terminal and cannot be materialized again.</summary>
    Retired = 3,
}

/// <summary>Classifies the outcome of retiring one semantic activation.</summary>
public enum BaseSemanticActivationRetirementDisposition
{
    /// <summary>The operation retired the live slot.</summary>
    RetiredNow = 1,
    /// <summary>The slot was already retired.</summary>
    AlreadyRetired = 2,
    /// <summary>The slot already contains compacted absence authority.</summary>
    AlreadyCompacted = 3,
}

/// <summary>Classifies how the canonical activation due instant was obtained.</summary>
public enum BaseSemanticActivationDueMode
{
    /// <summary>BASE accepted the current provider time.</summary>
    AcceptedCurrentTime = 1,
    /// <summary>The installed operation supplied an explicit UTC instant.</summary>
    ExplicitUtcInstant = 2,
}

/// <summary>Identifies an installed semantic activation definition.</summary>
public sealed record BaseSemanticActivationDefinitionKey
{
    /// <summary>Gets the stable definition identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive definition version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the canonical 256-bit definition checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains installed semantic definition authority for one finalized graph.</summary>
public sealed record BaseSemanticActivationDefinitionIdentity
{
    /// <summary>Gets the stable definition identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive definition version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the canonical definition checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
    /// <summary>Gets the positive graph-owner generation.</summary>
    public required long OwnerGeneration { get; init; }
}

/// <summary>Owns a canonical semantic activation key digest.</summary>
public sealed class BaseSemanticActivationKeyDigest : IEquatable<BaseSemanticActivationKeyDigest>
{
    /// <summary>Gets the exact digest length.</summary>
    public const int Length = 32;
    private readonly byte[] _bytes;
    private BaseSemanticActivationKeyDigest(byte[] bytes) => _bytes = bytes;
    /// <summary>Creates a deeply owned digest.</summary>
    public static BaseSemanticActivationKeyDigest Create(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Length) throw new ArgumentException("A semantic activation key digest must contain exactly 32 bytes.", nameof(bytes));
        return new(bytes.ToArray());
    }
    /// <summary>Copies the digest into an exact-size or larger destination.</summary>
    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < Length) throw new ArgumentException("The destination is too small.", nameof(destination));
        _bytes.CopyTo(destination);
    }
    internal byte[] ToArray() => _bytes.ToArray();
    /// <inheritdoc />
    public bool Equals(BaseSemanticActivationKeyDigest? other) => other is not null && CryptographicOperations.FixedTimeEquals(_bytes, other._bytes);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BaseSemanticActivationKeyDigest other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => BitConverter.ToInt32(_bytes, 0);
}

/// <summary>Contains the canonical due authority stored in one semantic slot.</summary>
public sealed record BaseSemanticActivationDueAuthority
{
    /// <summary>Gets how the instant was selected.</summary>
    public required BaseSemanticActivationDueMode Mode { get; init; }
    /// <summary>Gets the canonical Unix-millisecond instant.</summary>
    public required long CanonicalUnixMilliseconds { get; init; }
}

/// <summary>Contains the complete exported-subject lifetime bound to a semantic identity.</summary>
public sealed record BaseSemanticActivationSubjectLifetimeBinding
{
    /// <summary>Gets the exported-subject contract identifier.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the positive exported-subject contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the exported-subject contract checksum.</summary>
    public required ImmutableArray<byte> ContractChecksum { get; init; }
    /// <summary>Gets the canonical subject identifier.</summary>
    public required BaseSubjectId SubjectId { get; init; }
    /// <summary>Gets the BASE-owned authority epoch.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
    /// <summary>Gets the subject incarnation within the authority epoch.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
    /// <summary>Gets the stable semantic scope-binding identifier.</summary>
    public required ImmutableArray<byte> ScopeBindingId { get; init; }
    /// <summary>Gets the canonical binding checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains one Runtime-finalized semantic activation creation.</summary>
public sealed record BaseSemanticActivationCreateIntent
{
    /// <summary>Gets the installed activation definition.</summary>
    public required BaseActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets canonical activation input bytes.</summary>
    public required ImmutableArray<byte> CanonicalInput { get; init; }
    /// <summary>Gets the canonical input checksum.</summary>
    public required ImmutableArray<byte> InputChecksum { get; init; }
    /// <summary>Gets protected scope authority.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets canonical due authority.</summary>
    public required BaseSemanticActivationDueAuthority Due { get; init; }
    /// <summary>Gets the declared priority.</summary>
    public required int Priority { get; init; }
    /// <summary>Gets whether the activation is initially eligible.</summary>
    public required bool InitiallyEligible { get; init; }
    /// <summary>Gets the complete Runtime-owned semantic creation identity.</summary>
    public required BaseSemanticActivationCreationIdentity Identity { get; init; }
}

/// <summary>Contains Runtime-owned identity for one semantic activation creation.</summary>
public sealed record BaseSemanticActivationCreationIdentity
{
    /// <summary>Gets installed semantic definition authority.</summary>
    public required BaseSemanticActivationDefinitionIdentity SemanticDefinition { get; init; }
    /// <summary>Gets the canonical semantic key.</summary>
    public required BaseSemanticActivationKeyDigest Key { get; init; }
    /// <summary>Gets the stable scope-binding identifier.</summary>
    public required ImmutableArray<byte> ScopeBindingId { get; init; }
    /// <summary>Gets the derived activation identifier bytes.</summary>
    public required ImmutableArray<byte> DerivedActivationIdBytes { get; init; }
    /// <summary>Gets the canonical creation-identity checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Identifies the installed module completion operation permitted to retire a slot.</summary>
public sealed record BaseSemanticActivationModuleOperationIdentity
{
    /// <summary>Gets the stable operation identifier.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets the positive operation version.</summary>
    public required int OperationVersion { get; init; }
    /// <summary>Gets the canonical operation checksum.</summary>
    public required string OperationChecksum { get; init; }
}

/// <summary>Base type for the closed semantic activation operation union.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(BaseSemanticActivationEnsureIntent), "ensure")]
[JsonDerivedType(typeof(BaseSemanticActivationRetireIntent), "retire")]
public abstract record BaseSemanticActivationOperation;

/// <summary>Ensures one semantic activation.</summary>
public sealed record BaseSemanticActivationEnsureIntent : BaseSemanticActivationOperation
{
    /// <summary>Gets installed definition authority.</summary>
    public required BaseSemanticActivationDefinitionIdentity Definition { get; init; }
    /// <summary>Gets the bound semantic key digest.</summary>
    public required BaseSemanticActivationKeyDigest Key { get; init; }
    /// <summary>Gets canonical key bytes.</summary>
    public required ImmutableArray<byte> CanonicalKey { get; init; }
    /// <summary>Gets protected scope evidence.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets optional subject lifetime authority.</summary>
    public BaseSemanticActivationSubjectLifetimeBinding? SubjectLifetime { get; init; }
    /// <summary>Gets the activation creation.</summary>
    public required BaseSemanticActivationCreateIntent Activation { get; init; }
    /// <summary>Gets canonical due authority.</summary>
    public required BaseSemanticActivationDueAuthority Due { get; init; }
}

/// <summary>Retires one terminal semantic activation.</summary>
public sealed record BaseSemanticActivationRetireIntent : BaseSemanticActivationOperation
{
    /// <summary>Gets installed definition authority.</summary>
    public required BaseSemanticActivationDefinitionIdentity Definition { get; init; }
    /// <summary>Gets the bound semantic key digest.</summary>
    public required BaseSemanticActivationKeyDigest Key { get; init; }
    /// <summary>Gets canonical key bytes.</summary>
    public required ImmutableArray<byte> CanonicalKey { get; init; }
    /// <summary>Gets protected scope evidence.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets optional subject lifetime authority.</summary>
    public BaseSemanticActivationSubjectLifetimeBinding? SubjectLifetime { get; init; }
    /// <summary>Gets the only installed completion operation permitted to retire.</summary>
    public required BaseSemanticActivationModuleOperationIdentity CompletionOperation { get; init; }
}

/// <summary>Contains one semantic operation in the shared atomic request.</summary>
public sealed record BaseAtomicSemanticActivationExtension
{
    /// <summary>Gets the closed semantic operation.</summary>
    public required BaseSemanticActivationOperation Operation { get; init; }
    /// <summary>Gets the canonical structural digest.</summary>
    public required ImmutableArray<byte> StructuralDigest { get; init; }
}

/// <summary>Classifies the exact semantic state captured from storage.</summary>
public enum BaseSemanticActivationCapturedState
{
    /// <summary>The slot key is authoritatively absent.</summary>
    Missing = 1,
    /// <summary>The slot is live.</summary>
    Live = 2,
    /// <summary>The slot is retired.</summary>
    Retired = 3,
    /// <summary>The slot is represented by permanent compacted absence.</summary>
    CompactedAbsent = 4,
}

/// <summary>Contains the required store authority for semantic execution.</summary>
public sealed record BaseSemanticActivationStoreAuthorityRequirement
{
    /// <summary>Gets application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets logical store identity.</summary>
    public required string LogicalStoreId { get; init; }
    /// <summary>Gets physical store-instance identity.</summary>
    public required string StoreInstanceId { get; init; }
    /// <summary>Gets restore epoch.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets schema generation.</summary>
    public required long SchemaGeneration { get; init; }
    /// <summary>Gets semantic authority generation.</summary>
    public required long SemanticAuthorityGeneration { get; init; }
    /// <summary>Gets installed definition-set checksum.</summary>
    public required ImmutableArray<byte> DefinitionSetChecksum { get; init; }
}

/// <summary>Contains captured semantic store authority.</summary>
public sealed record BaseSemanticActivationStoreAuthority
{
    /// <summary>Gets required authority.</summary>
    public required BaseSemanticActivationStoreAuthorityRequirement Requirement { get; init; }
    /// <summary>Gets provider evidence checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains the stable and rotatable scope-directory binding.</summary>
public sealed record BaseSemanticActivationScopeBinding
{
    /// <summary>Gets scope kind.</summary>
    public required BaseSubjectScopeKind Kind { get; init; }
    /// <summary>Gets stable BASE-owned binding ID.</summary>
    public required ImmutableArray<byte> BindingId { get; init; }
    /// <summary>Gets protected canonical scope.</summary>
    public required ImmutableArray<byte> ProtectedCanonicalScope { get; init; }
    /// <summary>Gets current protected seek digest.</summary>
    public required ImmutableArray<byte> SeekDigest { get; init; }
    /// <summary>Gets protection key ID.</summary>
    public required string ProtectionKeyId { get; init; }
    /// <summary>Gets protection key version.</summary>
    public required int ProtectionKeyVersion { get; init; }
    /// <summary>Gets binding checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Classifies a scope-directory capture.</summary>
public enum BaseSemanticActivationScopeDirectoryState
{
    /// <summary>The directory binding exists.</summary>
    Existing = 1,
    /// <summary>The directory key is absent and the proposed binding may be inserted.</summary>
    Missing = 2,
}

/// <summary>Contains one read-only scope-directory capture.</summary>
public sealed record BaseSemanticActivationScopeDirectoryCapture
{
    /// <summary>Gets captured directory state.</summary>
    public required BaseSemanticActivationScopeDirectoryState State { get; init; }
    /// <summary>Gets existing or proposed resulting binding.</summary>
    public required BaseSemanticActivationScopeBinding ResultingBinding { get; init; }
    /// <summary>Gets exact scope-directory read intervals.</summary>
    public required ImmutableArray<BaseAtomicReadIntervalEvidence> ReadIntervals { get; init; }
    /// <summary>Gets canonical retained bytes.</summary>
    public required long CanonicalBytes { get; init; }
    /// <summary>Gets capture checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains authoritative missing-slot evidence.</summary>
public sealed record BaseSemanticActivationMissingAuthority
{
    /// <summary>Gets semantic key.</summary>
    public required BaseSemanticActivationKeyDigest Key { get; init; }
    /// <summary>Gets captured store authority.</summary>
    public required BaseSemanticActivationStoreAuthority StoreAuthority { get; init; }
    /// <summary>Gets exact absent-key access-path checksum.</summary>
    public required ImmutableArray<byte> AccessPathChecksum { get; init; }
}

/// <summary>Contains current live-slot authority.</summary>
public sealed record BaseSemanticActivationLiveAuthority
{
    /// <summary>Gets installed semantic definition.</summary>
    public required BaseSemanticActivationDefinitionIdentity Definition { get; init; }
    /// <summary>Gets semantic key.</summary>
    public required BaseSemanticActivationKeyDigest KeyDigest { get; init; }
    /// <summary>Gets protected logical scope evidence.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets stable scope binding.</summary>
    public required BaseSemanticActivationScopeBinding ScopeBinding { get; init; }
    /// <summary>Gets optional subject lifetime.</summary>
    public BaseSemanticActivationSubjectLifetimeBinding? SubjectLifetime { get; init; }
    /// <summary>Gets mapped activation ID.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets activation definition.</summary>
    public required BaseActivationDefinitionKey ActivationDefinition { get; init; }
    /// <summary>Gets activation input checksum.</summary>
    public required ImmutableArray<byte> InputChecksum { get; init; }
    /// <summary>Gets canonical due authority.</summary>
    public required BaseSemanticActivationDueAuthority Due { get; init; }
    /// <summary>Gets positive slot generation.</summary>
    public required long SlotGeneration { get; init; }
    /// <summary>Gets store authority.</summary>
    public required BaseSemanticActivationStoreAuthority StoreAuthority { get; init; }
    /// <summary>Gets live authority checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains current retired-slot authority.</summary>
public sealed record BaseSemanticActivationRetirementAuthority
{
    /// <summary>Gets exact definition authority.</summary>
    public required BaseSemanticActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets semantic key.</summary>
    public required BaseSemanticActivationKeyDigest KeyDigest { get; init; }
    /// <summary>Gets optional subject lifetime.</summary>
    public BaseSemanticActivationSubjectLifetimeBinding? SubjectLifetime { get; init; }
    /// <summary>Gets terminal activation ID.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets terminal activation state.</summary>
    public required BaseActivationState TerminalState { get; init; }
    /// <summary>Gets terminal activation generation.</summary>
    public required long TerminalActivationGeneration { get; init; }
    /// <summary>Gets terminal activation checksum.</summary>
    public required ImmutableArray<byte> TerminalActivationChecksum { get; init; }
    /// <summary>Gets the installed completion operation checksum.</summary>
    public required ImmutableArray<byte> CompletionOperationChecksum { get; init; }
    /// <summary>Gets the outer completion receipt checksum.</summary>
    public required ImmutableArray<byte> CompletionReceiptChecksum { get; init; }
    /// <summary>Gets retirement journal position.</summary>
    public required long RetirementPosition { get; init; }
    /// <summary>Gets final slot generation.</summary>
    public required long SlotGeneration { get; init; }
    /// <summary>Gets store authority.</summary>
    public required BaseSemanticActivationStoreAuthority StoreAuthority { get; init; }
    /// <summary>Gets retirement checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains permanent compacted-absence authority.</summary>
public sealed record BaseSemanticActivationAbsenceAuthority
{
    /// <summary>Gets semantic key.</summary>
    public required BaseSemanticActivationKeyDigest Key { get; init; }
    /// <summary>Gets definition authority.</summary>
    public required BaseSemanticActivationDefinitionIdentity Definition { get; init; }
    /// <summary>Gets stable scope binding ID.</summary>
    public required ImmutableArray<byte> ScopeBindingId { get; init; }
    /// <summary>Gets optional subject lifetime.</summary>
    public BaseSemanticActivationSubjectLifetimeBinding? SubjectLifetime { get; init; }
    /// <summary>Gets final slot generation.</summary>
    public required long FinalSlotGeneration { get; init; }
    /// <summary>Gets absence floor generation.</summary>
    public required long AbsenceFloorGeneration { get; init; }
    /// <summary>Gets retirement position.</summary>
    public required long RetirementPosition { get; init; }
    /// <summary>Gets store authority.</summary>
    public required BaseSemanticActivationStoreAuthority StoreAuthority { get; init; }
    /// <summary>Gets absence checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains exact semantic accounting.</summary>
public sealed record BaseSemanticActivationAccounting
{
    /// <summary>Gets semantic operations.</summary>
    public required int Operations { get; init; }
    /// <summary>Gets scope-directory reads.</summary>
    public required int ScopeDirectoryReads { get; init; }
    /// <summary>Gets slot reads.</summary>
    public required int SlotReads { get; init; }
    /// <summary>Gets activation reads.</summary>
    public required int ActivationReads { get; init; }
    /// <summary>Gets read intervals.</summary>
    public required int ReadIntervals { get; init; }
    /// <summary>Gets index operations.</summary>
    public required int IndexOperations { get; init; }
    /// <summary>Gets canonical key bytes.</summary>
    public required long KeyBytes { get; init; }
    /// <summary>Gets scope-directory bytes.</summary>
    public required long ScopeDirectoryBytes { get; init; }
    /// <summary>Gets activation bytes.</summary>
    public required long ActivationBytes { get; init; }
    /// <summary>Gets canonical evidence bytes.</summary>
    public required long EvidenceBytes { get; init; }
    /// <summary>Gets canonical receipt bytes.</summary>
    public required long ReceiptBytes { get; init; }
    /// <summary>Gets retained transient bytes.</summary>
    public required long TransientBytes { get; init; }
    /// <summary>Gets the exact nested L51 activation-creation accounting.</summary>
    public required BaseActivationAccounting ActivationCreation { get; init; }
}

/// <summary>Contains provider-captured semantic slot authority.</summary>
public sealed record BaseCapturedSemanticActivationEvidence
{
    /// <summary>Gets captured state.</summary>
    public required BaseSemanticActivationCapturedState State { get; init; }
    /// <summary>Gets scope-directory capture.</summary>
    public required BaseSemanticActivationScopeDirectoryCapture ScopeDirectory { get; init; }
    /// <summary>Gets missing authority only for Missing.</summary>
    public BaseSemanticActivationMissingAuthority? Missing { get; init; }
    /// <summary>Gets live authority only for Live.</summary>
    public BaseSemanticActivationLiveAuthority? Live { get; init; }
    /// <summary>Gets retirement authority only for Retired.</summary>
    public BaseSemanticActivationRetirementAuthority? Retired { get; init; }
    /// <summary>Gets absence authority only for CompactedAbsent.</summary>
    public BaseSemanticActivationAbsenceAuthority? Absent { get; init; }
    /// <summary>Gets normalized nonempty read intervals.</summary>
    public required ImmutableArray<BaseAtomicReadIntervalEvidence> ReadIntervals { get; init; }
    /// <summary>Gets exact capture accounting.</summary>
    public required BaseSemanticActivationAccounting Accounting { get; init; }
    /// <summary>Gets evidence checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Opaque single-use provider plan for a semantic transition.</summary>
public abstract class BaseSemanticActivationPreparedPlan
{
    /// <summary>Initializes a provider-owned plan.</summary>
    protected BaseSemanticActivationPreparedPlan() { }
}

/// <summary>Contains one semantic write interval.</summary>
public sealed record BaseSemanticActivationWriteIntervalEvidence
{
    /// <summary>Gets access-path ID.</summary>
    public required string AccessPathId { get; init; }
    /// <summary>Gets lower bound.</summary>
    public required ImmutableArray<byte> Lower { get; init; }
    /// <summary>Gets lower inclusivity.</summary>
    public required bool LowerInclusive { get; init; }
    /// <summary>Gets upper bound.</summary>
    public required ImmutableArray<byte> Upper { get; init; }
    /// <summary>Gets upper inclusivity.</summary>
    public required bool UpperInclusive { get; init; }
    /// <summary>Gets interval checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains provider-prepared semantic transition evidence.</summary>
public sealed record BasePreparedSemanticActivation
{
    /// <summary>Gets session-owned single-use plan.</summary>
    public required BaseSemanticActivationPreparedPlan SessionPlan { get; init; }
    /// <summary>Gets operation kind.</summary>
    public required BaseSemanticActivationOperationKind Operation { get; init; }
    /// <summary>Gets prior state.</summary>
    public required BaseSemanticActivationCapturedState PriorState { get; init; }
    /// <summary>Gets resulting state.</summary>
    public required BaseSemanticActivationSlotState ResultingState { get; init; }
    /// <summary>Gets resulting slot generation.</summary>
    public required long ResultingSlotGeneration { get; init; }
    /// <summary>Gets resulting activation ID when live.</summary>
    public string? ResultingActivationId { get; init; }
    /// <summary>Gets normalized read intervals.</summary>
    public required ImmutableArray<BaseAtomicReadIntervalEvidence> ReadIntervals { get; init; }
    /// <summary>Gets normalized write intervals.</summary>
    public required ImmutableArray<BaseSemanticActivationWriteIntervalEvidence> WriteIntervals { get; init; }
    /// <summary>Gets exact accounting.</summary>
    public required BaseSemanticActivationAccounting Accounting { get; init; }
    /// <summary>Gets prepared checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains applied semantic evidence before commit publication.</summary>
public sealed record BaseProvisionalSemanticActivation
{
    /// <summary>Gets operation kind.</summary>
    public required BaseSemanticActivationOperationKind Operation { get; init; }
    /// <summary>Gets prior state.</summary>
    public required BaseSemanticActivationCapturedState PriorState { get; init; }
    /// <summary>Gets resulting state.</summary>
    public required BaseSemanticActivationSlotState ResultingState { get; init; }
    /// <summary>Gets resulting slot generation.</summary>
    public required long ResultingSlotGeneration { get; init; }
    /// <summary>Gets activation ID when live.</summary>
    public string? ActivationId { get; init; }
    /// <summary>Gets activation generation when live.</summary>
    public long? ActivationGeneration { get; init; }
    /// <summary>Gets activation checksum when live.</summary>
    public ImmutableArray<byte> ActivationChecksum { get; init; }
    /// <summary>Gets commit journal position.</summary>
    public required long CommitJournalPosition { get; init; }
    /// <summary>Gets exact accounting.</summary>
    public required BaseSemanticActivationAccounting Accounting { get; init; }
    /// <summary>Gets provisional checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Stores semantic activation evidence in one module-mutation receipt.</summary>
public sealed record BaseSemanticActivationReceiptEvidence
{
    /// <summary>Gets the operation kind.</summary>
    public required BaseSemanticActivationOperationKind Operation { get; init; }
    /// <summary>Gets the installed definition ID.</summary>
    public required string DefinitionId { get; init; }
    /// <summary>Gets the installed definition version.</summary>
    public required int DefinitionVersion { get; init; }
    /// <summary>Gets the definition checksum.</summary>
    public required ImmutableArray<byte> DefinitionChecksum { get; init; }
    /// <summary>Gets the exact semantic key digest.</summary>
    public required BaseSemanticActivationKeyDigest Key { get; init; }
    /// <summary>Gets the resulting state.</summary>
    public required BaseSemanticActivationSlotState State { get; init; }
    /// <summary>Gets the resulting slot generation.</summary>
    public required long SlotGeneration { get; init; }
    /// <summary>Gets ensure disposition when applicable.</summary>
    public BaseSemanticActivationEnsureDisposition? EnsureDisposition { get; init; }
    /// <summary>Gets retirement disposition when applicable.</summary>
    public BaseSemanticActivationRetirementDisposition? RetirementDisposition { get; init; }
    /// <summary>Gets the live activation identifier when disclosed.</summary>
    public string? ActivationId { get; init; }
    /// <summary>Gets the resulting slot checksum.</summary>
    public required ImmutableArray<byte> SlotChecksum { get; init; }
    /// <summary>Gets the commit journal position.</summary>
    public required long JournalPosition { get; init; }
    /// <summary>Gets commit-evidence checksum.</summary>
    public required ImmutableArray<byte> CommitEvidenceChecksum { get; init; }
    /// <summary>Gets the canonical evidence checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Provides the source-generated wire shape for semantic receipt evidence.</summary>
public sealed record BaseSemanticActivationReceiptEvidenceWire
{
    /// <summary>Gets the operation discriminator.</summary>
    public required int Operation { get; init; }
    /// <summary>Gets the definition ID.</summary>
    public required string DefinitionId { get; init; }
    /// <summary>Gets the definition version.</summary>
    public required int DefinitionVersion { get; init; }
    /// <summary>Gets the definition checksum.</summary>
    public required byte[] DefinitionChecksum { get; init; }
    /// <summary>Gets semantic key digest bytes.</summary>
    public required byte[] KeyDigest { get; init; }
    /// <summary>Gets the resulting state.</summary>
    public required int State { get; init; }
    /// <summary>Gets the canonical positive slot generation.</summary>
    public required string SlotGeneration { get; init; }
    /// <summary>Gets ensure disposition when applicable.</summary>
    public int? EnsureDisposition { get; init; }
    /// <summary>Gets retirement disposition when applicable.</summary>
    public int? RetirementDisposition { get; init; }
    /// <summary>Gets the live activation identifier when disclosed.</summary>
    public string? ActivationId { get; init; }
    /// <summary>Gets resulting slot checksum.</summary>
    public required byte[] SlotChecksum { get; init; }
    /// <summary>Gets canonical positive journal position.</summary>
    public required string JournalPosition { get; init; }
    /// <summary>Gets commit-evidence checksum.</summary>
    public required byte[] CommitEvidenceChecksum { get; init; }
    /// <summary>Gets the canonical evidence checksum.</summary>
    public required byte[] Checksum { get; init; }
}
