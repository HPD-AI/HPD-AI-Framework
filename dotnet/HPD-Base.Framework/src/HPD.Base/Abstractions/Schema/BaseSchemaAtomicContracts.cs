using System.Collections.Immutable;
using System.Text.Json.Serialization;

#pragma warning disable CS1591 // XML documentation is completed before the L54 contract checkpoint closes.

namespace HPD.Base;

public enum BaseLogicalIndexGenerationState
{
    Absent = 0, Building = 1, CatchingUp = 2, Ready = 3, RebuildRequired = 4,
    Quarantined = 5, Retiring = 6, Tombstoned = 7,
}

public enum BaseLogicalKeyIntervalKind { ReadDependency = 0, WriteReservation = 1 }

public sealed record BaseSchemaExecutionLimits
{
    public required long MaximumRecords { get; init; }
    public required long MaximumCanonicalBytes { get; init; }
    public required long MaximumJsonNodes { get; init; }
    public required long MaximumConstraintEvaluations { get; init; }
    public required long MaximumPredicateEvaluations { get; init; }
    public required long MaximumKeys { get; init; }
    public required long MaximumKeyBytes { get; init; }
    public required long MaximumUniqueCandidates { get; init; }
    public required long MaximumUniqueChecks { get; init; }
    public required long MaximumIntervals { get; init; }
    public required long MaximumIntervalBytes { get; init; }
    public required long MaximumEvidenceBytes { get; init; }
    public required long MaximumTransientBytes { get; init; }
}

public sealed record BaseSchemaWorkAccounting
{
    public required long Records { get; init; }
    public required long CanonicalBytes { get; init; }
    public required long JsonNodes { get; init; }
    public required long ConstraintEvaluations { get; init; }
    public required long PredicateEvaluations { get; init; }
    public required long Keys { get; init; }
    public required long KeyBytes { get; init; }
    public required long UniqueCandidates { get; init; }
    public required long UniqueChecks { get; init; }
    public required long Intervals { get; init; }
    public required long IntervalBytes { get; init; }
    public required long EvidenceBytes { get; init; }
    public required long TransientBytes { get; init; }
}

public sealed record BaseCollectionSchemaAuthority
{
    public required string CollectionId { get; init; }
    public required long CollectionGeneration { get; init; }
    public required BaseSchemaAuthorityChecksum LogicalSchemaChecksum { get; init; }
    public required ImmutableArray<BaseScalarConstraintChecksum> Constraints { get; init; }
    public required ImmutableArray<BaseLogicalIndexChecksum> Indexes { get; init; }
}

public sealed record BaseCollectionSchemaRequirement
{
    public required string CollectionId { get; init; }
    public required BaseSchemaAuthorityChecksum LogicalSchemaChecksum { get; init; }
    public required ImmutableArray<BaseScalarConstraintChecksum> Constraints { get; init; }
    public required ImmutableArray<BaseLogicalIndexChecksum> Indexes { get; init; }
}

public sealed record BaseAtomicSchemaCaptureRequest
{
    public required ImmutableArray<BaseCollectionSchemaRequirement> Requirements { get; init; }
    public required BaseSchemaExecutionLimits Limits { get; init; }
    public required BaseSchemaAuthorityChecksum Checksum { get; init; }
}

public sealed record BaseLogicalIndexCurrentAuthority
{
    public required BaseLogicalIndexChecksum Index { get; init; }
    public required BaseLogicalIndexGenerationState State { get; init; }
    public required long Generation { get; init; }
    public required BaseSchemaAuthorityChecksum PublicationChecksum { get; init; }
}

public sealed record BaseAtomicSchemaAuthority
{
    public required BaseSchemaAuthorityChecksum Checksum { get; init; }
    public required long SchemaGeneration { get; init; }
    public required ImmutableArray<BaseCollectionSchemaAuthority> Collections { get; init; }
    public required ImmutableArray<BaseLogicalIndexCurrentAuthority> Indexes { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(BaseMissingFieldState), "missing")]
[JsonDerivedType(typeof(BasePresentNullFieldState), "presentNull")]
[JsonDerivedType(typeof(BasePresentValueFieldState), "presentValue")]
public abstract record BaseFieldState;
public sealed record BaseMissingFieldState : BaseFieldState;
public sealed record BasePresentNullFieldState : BaseFieldState;
public sealed record BasePresentValueFieldState : BaseFieldState
{
    public required ImmutableArray<byte> CanonicalBytes { get; init; }
}

public sealed record BaseSchemaCapturedRecord
{
    public required int MutationOrdinal { get; init; }
    public required string CollectionId { get; init; }
    public required RecordId RecordId { get; init; }
    public required bool Present { get; init; }
    public ImmutableArray<byte>? CanonicalBytes { get; init; }
    public RevisionToken? Revision { get; init; }
    public required long CollectionGeneration { get; init; }
}

public sealed record BaseSchemaOverlayRecord
{
    public required int MutationOrdinal { get; init; }
    public required int StatementOrdinal { get; init; }
    public required string CollectionId { get; init; }
    public required RecordId RecordId { get; init; }
    public required BaseCapturedMutationDisposition Disposition { get; init; }
    public required bool Present { get; init; }
    public ImmutableArray<byte>? CanonicalBytes { get; init; }
    public required BaseSchemaAuthorityChecksum OverlayDigest { get; init; }
}

public sealed record BaseAtomicConstraintEvidence
{
    public required int MutationOrdinal { get; init; }
    public required int FieldOrdinal { get; init; }
    public required BaseScalarConstraintChecksum ConstraintChecksum { get; init; }
    public required BaseFieldState State { get; init; }
    public required BaseSchemaWorkAccounting Accounting { get; init; }
}

public sealed record BaseLogicalKeyInterval
{
    public required BaseLogicalIndexChecksum Index { get; init; }
    public required ImmutableArray<byte> EqualityKey { get; init; }
    public required long IndexGeneration { get; init; }
    public required BaseLogicalKeyIntervalKind Kind { get; init; }
}

public sealed record BaseAtomicIndexTransitionEvidence
{
    public required int MutationOrdinal { get; init; }
    public required BaseLogicalIndexChecksum IndexChecksum { get; init; }
    public required bool WasMember { get; init; }
    public ImmutableArray<byte>? OldEqualityKey { get; init; }
    public required bool IsMember { get; init; }
    public ImmutableArray<byte>? NewEqualityKey { get; init; }
    public required ImmutableArray<BaseLogicalKeyInterval> Intervals { get; init; }
    public required BaseSchemaWorkAccounting Accounting { get; init; }
}

public sealed record BaseAtomicSchemaCaptureExtension
{
    public required BaseAtomicSchemaAuthority Authority { get; init; }
    public required ImmutableArray<BaseSchemaCapturedRecord> Originals { get; init; }
    public required BaseSchemaExecutionLimits Limits { get; init; }
    public required BaseSchemaWorkAccounting Accounting { get; init; }
}

public sealed record BaseAtomicSchemaFinalizedExtension
{
    public required BaseAtomicSchemaAuthority Authority { get; init; }
    public required BaseSchemaExecutionLimits Limits { get; init; }
    public required ImmutableArray<BaseSchemaOverlayRecord> StatementLocal { get; init; }
    public required ImmutableArray<BaseSchemaOverlayRecord> FinalOverlay { get; init; }
    public required ImmutableArray<BaseAtomicConstraintEvidence> Constraints { get; init; }
    public required ImmutableArray<BaseAtomicIndexTransitionEvidence> Indexes { get; init; }
    public required BaseSchemaWorkAccounting Accounting { get; init; }
    public required BaseSchemaAuthorityChecksum Checksum { get; init; }
}

public abstract class BaseAtomicSchemaPreparedPlan { protected BaseAtomicSchemaPreparedPlan() { } }

public sealed record BaseAtomicSchemaPreparedExtension
{
    public required BaseAtomicSchemaPreparedPlan Plan { get; init; }
    public required BaseSchemaAuthorityChecksum FinalizedChecksum { get; init; }
}

public sealed record BaseSchemaAppliedIndexTransition
{
    public required int MutationOrdinal { get; init; }
    public required BaseLogicalIndexChecksum Index { get; init; }
    public required long ResultingGeneration { get; init; }
    public required BaseSchemaAuthorityChecksum AppliedChecksum { get; init; }
}

public sealed record BaseAtomicSchemaProvisionalExtension
{
    public required ImmutableArray<BaseSchemaAppliedIndexTransition> AppliedIndexes { get; init; }
    public required BaseSchemaWorkAccounting Accounting { get; init; }
    public required BaseSchemaAuthorityChecksum ProvisionalChecksum { get; init; }
}

public sealed record BaseAtomicSchemaCommittedEvidence
{
    public required BaseSchemaAuthorityChecksum AuthorityChecksum { get; init; }
    public required BaseSchemaAuthorityChecksum FinalizedChecksum { get; init; }
    public required BaseSchemaAuthorityChecksum ProvisionalChecksum { get; init; }
    public required BaseSchemaWorkAccounting Accounting { get; init; }
}
