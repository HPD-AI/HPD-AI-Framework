using HPD.Base.Policy;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Schema;

namespace HPD.Base.Runtime.Policy;

public interface IBasePolicyOrchestrator
{
    ValueTask<OperationResult<BasePolicyEvaluation>> EvaluateReadAsync(
        BasePolicyRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<OperationResult<BasePolicyEvaluation>> EvaluateWriteAsync(
        BasePolicyRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record BasePolicyRequest
{
    public required PrincipalContext Principal { get; init; }
    public required OperationContext Operation { get; init; }
    public required CollectionDefinition Collection { get; init; }
    public required PolicyResourceKind ResourceKind { get; init; }
    public RecordQuery? Query { get; init; }
    public RecordEnvelope? ExistingRecord { get; init; }
    public RecordPayload? ProposedPayload { get; init; }
    public RecordEnvelope? ProposedRecord { get; init; }
    public RecordId? RecordId { get; init; }
    public AccessGrant[]? Grants { get; init; }
    public Dictionary<string, string>? PolicyRefs { get; init; }
}

public sealed record BasePolicyEvaluation
{
    public required PolicyDecision Decision { get; init; }
    public FilterExpression? EffectiveRecordFilter { get; init; }
    public FieldMask? EffectiveReadMask { get; init; }
    public FieldMask? EffectiveWriteMask { get; init; }
}

public interface IBaseRecordRedactor
{
    RecordEnvelope RedactRecord(
        RecordEnvelope record,
        CollectionDefinition collection,
        BasePolicyEvaluation policy,
        VisibilityLevel view);

    RecordPage RedactPage(
        RecordPage page,
        CollectionDefinition collection,
        BasePolicyEvaluation policy,
        VisibilityLevel view);
}
