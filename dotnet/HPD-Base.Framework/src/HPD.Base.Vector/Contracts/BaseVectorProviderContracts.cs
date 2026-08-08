namespace HPD.Base;

/// <summary>Classifies a vector provider's authority relationship.</summary>
public enum BaseVectorProviderConsistency
{
    /// <summary>Ranking and hydration share the authoritative record transaction.</summary>
    TransactionalCurrent,
    /// <summary>Ranking is maintained from the durable mutation journal.</summary>
    DerivedJournal,
}

/// <summary>Describes one installed vector provider's stable capabilities.</summary>
public sealed record BaseVectorProviderDescriptor
{
    /// <summary>Gets the stable provider identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the consistency architecture.</summary>
    public required BaseVectorProviderConsistency Consistency { get; init; }
    /// <summary>Gets whether ranking is exact.</summary>
    public required bool Exact { get; init; }
    /// <summary>Gets the maximum accepted top-K.</summary>
    public required int MaximumTopK { get; init; }
}

/// <summary>Identifies one exact authoritative candidate revision.</summary>
public readonly record struct BaseVectorCandidateIdentity(RecordId RecordId, RevisionToken IndexedRevision, BaseMutationJournalPosition IndexedPosition);

/// <summary>Contains one bounded ranked provider candidate.</summary>
public sealed record BaseVectorCandidate
{
    /// <summary>Gets the record identifier.</summary>
    public required RecordId RecordId { get; init; }
    /// <summary>Gets the indexed authoritative revision.</summary>
    public required RevisionToken IndexedRevision { get; init; }
    /// <summary>Gets the indexed journal position.</summary>
    public required BaseMutationJournalPosition IndexedPosition { get; init; }
    /// <summary>Gets the one-based provider rank.</summary>
    public required int Rank { get; init; }
    /// <summary>Gets the labeled finite measure.</summary>
    public required BaseVectorMeasure Measure { get; init; }
}

/// <summary>Classifies exact pre-ranking candidate enforcement.</summary>
public enum BaseVectorConstraintEnforcement
{
    /// <summary>The entire normalized constraint is enforced before ranking.</summary>
    PreRankingExact,
    /// <summary>The provider cannot enforce the normalized constraint exactly.</summary>
    Unsupported,
}

/// <summary>Represents an opaque, generation-bound in-process provider plan.</summary>
public abstract class BaseVectorProviderPlan { }

/// <summary>Contains a provider's proof that it can enforce the exact normalized constraint.</summary>
public sealed record BaseVectorConstraintPreparation
{
    /// <summary>Gets the accepted constraint digest.</summary>
    public required BaseVectorConstraintDigest ConstraintDigest { get; init; }
    /// <summary>Gets the enforcement classification.</summary>
    public required BaseVectorConstraintEnforcement Enforcement { get; init; }
    /// <summary>Gets the opaque provider plan.</summary>
    public required BaseVectorProviderPlan Plan { get; init; }
}

/// <summary>Contains immutable facts used to prepare exact candidate enforcement.</summary>
public sealed record BaseVectorProviderPreparationRequest
{
    /// <summary>Gets the vector index.</summary>
    public required VectorIndexDefinition Index { get; init; }
    /// <summary>Gets the normalized candidate constraint.</summary>
    public required BaseVectorCandidateConstraint Constraint { get; init; }
    /// <summary>Gets the canonical constraint digest.</summary>
    public required BaseVectorConstraintDigest ConstraintDigest { get; init; }
    /// <summary>Gets the authority snapshot.</summary>
    public required BaseVectorAuthoritySnapshot Snapshot { get; init; }
}

/// <summary>Contains immutable facts for one bounded provider search.</summary>
public sealed record BaseVectorExecutionRequest
{
    /// <summary>Gets the vector index.</summary>
    public required VectorIndexDefinition Index { get; init; }
    /// <summary>Gets the validated query vector.</summary>
    public required BaseVector Vector { get; init; }
    /// <summary>Gets the bounded requested result count.</summary>
    public required int Take { get; init; }
    /// <summary>Gets the accepted provider plan.</summary>
    public required BaseVectorProviderPlan Plan { get; init; }
    /// <summary>Gets the authority snapshot.</summary>
    public required BaseVectorAuthoritySnapshot Snapshot { get; init; }
    /// <summary>Gets the consistency requirement.</summary>
    public required BaseVectorConsistencyRequirement Consistency { get; init; }
    /// <summary>Gets the safe operation correlation identifier.</summary>
    public string? CorrelationId { get; init; }
}

/// <summary>Contains one bounded provider ranking result without application records.</summary>
public sealed record BaseVectorProviderResult
{
    /// <summary>Gets the exact authority snapshot used by ranking.</summary>
    public required BaseVectorAuthoritySnapshot Snapshot { get; init; }
    /// <summary>Gets ranked candidates.</summary>
    public required BaseVectorCandidate[] Candidates { get; init; }
    /// <summary>Gets the accuracy classification.</summary>
    public required BaseVectorResultAccuracy Accuracy { get; init; }
}

/// <summary>Executes vector constraint preparation and bounded ranking.</summary>
public interface IBaseVectorProvider
{
    /// <summary>Gets the stable provider descriptor.</summary>
    BaseVectorProviderDescriptor Descriptor { get; }
    /// <summary>Prepares and proves exact pre-ranking enforcement.</summary>
    ValueTask<BaseVectorConstraintPreparation> PrepareAsync(BaseVectorProviderPreparationRequest request, CancellationToken cancellationToken = default);
    /// <summary>Executes one bounded search using an accepted provider plan.</summary>
    ValueTask<BaseVectorProviderResult> SearchAsync(BaseVectorExecutionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Provides one finite authority snapshot and exact-revision batch hydration.</summary>
public interface IBaseVectorHydrationSession : IAsyncDisposable
{
    /// <summary>Gets the bound authority snapshot.</summary>
    BaseVectorAuthoritySnapshot Snapshot { get; }
    /// <summary>Reads every candidate at its exact indexed revision.</summary>
    ValueTask<OperationResult<RecordEnvelope[]>> GetExactAsync(CollectionDefinition collection, BaseVectorCandidateIdentity[] candidates, OperationContext context, CancellationToken cancellationToken = default);
}

/// <summary>Opens provider-specific authority snapshots used for ranking and hydration.</summary>
public interface IBaseVectorAuthority
{
    /// <summary>Opens one finite authority snapshot. A derived provider captures the authoritative head once for <see cref="BaseVectorConsistencyRequirement.Current"/> and waits only for that captured position.</summary>
    ValueTask<OperationResult<IBaseVectorHydrationSession>> OpenAsync(CollectionDefinition collection, VectorIndexDefinition index, BaseVectorConsistencyRequirement consistency, OperationContext context, CancellationToken cancellationToken = default);
}
