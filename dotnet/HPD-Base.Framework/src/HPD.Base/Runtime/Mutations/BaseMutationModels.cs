
namespace HPD.Base;

internal sealed record BaseFinalizedRecordMutationPlan(
    System.Collections.Immutable.ImmutableArray<BaseAtomicMutationPlanItem> Items,
    System.Collections.Immutable.ImmutableArray<BaseSubjectReferenceValidationPlanItem> SubjectValidations,
    System.Collections.Immutable.ImmutableArray<BasePolicyEvaluation> PolicyEvaluations,
    System.Collections.Immutable.ImmutableArray<BaseFinalizedRelationPolicy> RelationPolicies);

internal sealed record BaseFinalizedRelationPolicy(
    string SourceStatementId,
    string SourceFieldId,
    string TargetCollectionId,
    RecordId TargetRecordId,
    BasePolicyEvaluation Evaluation);

internal sealed record BaseMutationCommand
{
    /// <summary>Gets or sets the index.</summary>
    public required int Index { get; init; }
    /// <summary>Gets or sets the item ID.</summary>
    public required string ItemId { get; init; }
    /// <summary>Gets or sets the collection ID.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets or sets the kind.</summary>
    public required BaseRecordMutationKind Kind { get; init; }
    /// <summary>Gets or sets the collection.</summary>
    public required CollectionDefinition Collection { get; init; }
    /// <summary>Gets or sets the context.</summary>
    public required OperationContext Context { get; init; }
    /// <summary>Gets or sets the event ID.</summary>
    public required string EventId { get; init; }
    /// <summary>Gets or sets the store.</summary>
    public required BaseResolvedMutationStore Store { get; init; }
    /// <summary>Gets or sets the create.</summary>
    public RecordCreateRequest? Create { get; init; }
    /// <summary>Gets or sets the record ID.</summary>
    public RecordId? RecordId { get; init; }
    /// <summary>Gets whether BASE Runtime assigned the create identifier before provider capture.</summary>
    public bool RuntimeAssignedRecordId { get; init; }
    /// <summary>Gets or sets the patch.</summary>
    public RecordPatchRequest? Patch { get; init; }
    /// <summary>Gets or sets the replace.</summary>
    public RecordReplaceRequest? Replace { get; init; }
    /// <summary>Gets or sets the delete.</summary>
    public RecordDeleteRequest? Delete { get; init; }
    /// <summary>Gets or sets the upsert.</summary>
    public RecordUpsertRequest? Upsert { get; init; }
    /// <summary>Gets or sets the create payload.</summary>
    public BaseValidatedPayload? CreatePayload { get; init; }
    /// <summary>Gets or sets the update payload.</summary>
    public BaseValidatedPayload? UpdatePayload { get; init; }
}

internal sealed record BaseMutationAttempt
{
    /// <summary>Gets or sets the command.</summary>
    public required BaseMutationCommand Command { get; init; }
    /// <summary>Gets or sets the status.</summary>
    public required OperationStatus Status { get; init; }
    /// <summary>Gets or sets the error.</summary>
    public BaseError? Error { get; init; }
    /// <summary>Gets or sets the provider error.</summary>
    public bool ProviderError { get; init; }
    /// <summary>Gets or sets the mutation.</summary>
    public BaseRecordMutationFact? Mutation { get; init; }
    /// <summary>Gets or sets the policy.</summary>
    public BasePolicyEvaluation? Policy { get; init; }
    /// <summary>Gets or sets the revision.</summary>
    public RevisionInfo? Revision { get; init; }
}
