using HPD.Base.Policy;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime.Stores;
using HPD.Base.Runtime.Schema;
using HPD.Base.Runtime.Policy;
using HPD.Base.Schema;
using HPD.Base.Stores;

namespace HPD.Base.Runtime.Mutations;

internal sealed record BaseMutationCommand
{
    public required int Index { get; init; }
    public required string ItemId { get; init; }
    public required string CollectionId { get; init; }
    public required BaseRecordMutationKind Kind { get; init; }
    public required CollectionDefinition Collection { get; init; }
    public required OperationContext Context { get; init; }
    public required string EventId { get; init; }
    public required BaseResolvedMutationStore Store { get; init; }
    public RecordCreateRequest? Create { get; init; }
    public RecordId? RecordId { get; init; }
    public RecordPatchRequest? Patch { get; init; }
    public RecordReplaceRequest? Replace { get; init; }
    public RecordDeleteRequest? Delete { get; init; }
    public RecordUpsertRequest? Upsert { get; init; }
    public BaseValidatedPayload? CreatePayload { get; init; }
    public BaseValidatedPayload? UpdatePayload { get; init; }
}

internal sealed record BaseMutationAttempt
{
    public required BaseMutationCommand Command { get; init; }
    public required OperationStatus Status { get; init; }
    public BaseError? Error { get; init; }
    public bool ProviderError { get; init; }
    public BaseRecordMutationFact? Mutation { get; init; }
    public BasePolicyEvaluation? Policy { get; init; }
    public RevisionInfo? Revision { get; init; }
}
