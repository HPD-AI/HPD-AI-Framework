
namespace HPD.Base;

/// <summary>Defines the ibase policy orchestrator contract.</summary>
public interface IBasePolicyOrchestrator
{
    /// <summary>Executes the evaluate read async operation.</summary>
    ValueTask<OperationResult<BasePolicyEvaluation>> EvaluateReadAsync(
        BasePolicyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Executes the evaluate write async operation.</summary>
    ValueTask<OperationResult<BasePolicyEvaluation>> EvaluateWriteAsync(
        BasePolicyRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Represents a base policy request.</summary>
public sealed record BasePolicyRequest
{
    /// <summary>Gets or sets the principal.</summary>
    public required PrincipalContext Principal { get; init; }
    /// <summary>Gets or sets the operation.</summary>
    public required OperationContext Operation { get; init; }
    /// <summary>Gets or sets the collection.</summary>
    public required CollectionDefinition Collection { get; init; }
    /// <summary>Gets or sets the resource kind.</summary>
    public required PolicyResourceKind ResourceKind { get; init; }
    /// <summary>Gets or sets the query.</summary>
    public RecordQuery? Query { get; init; }
    /// <summary>Gets or sets the existing record.</summary>
    public RecordEnvelope? ExistingRecord { get; init; }
    /// <summary>Gets or sets the proposed payload.</summary>
    public RecordPayload? ProposedPayload { get; init; }
    /// <summary>Gets or sets the proposed record.</summary>
    public RecordEnvelope? ProposedRecord { get; init; }
    /// <summary>Gets or sets the record ID.</summary>
    public RecordId? RecordId { get; init; }
    /// <summary>Gets or sets the grants.</summary>
    public AccessGrant[]? Grants { get; init; }
    /// <summary>Gets or sets the policy refs.</summary>
    public Dictionary<string, string>? PolicyRefs { get; init; }
}

/// <summary>Represents a base policy evaluation.</summary>
public sealed record BasePolicyEvaluation
{
    /// <summary>Gets or sets the decision.</summary>
    public required PolicyDecision Decision { get; init; }
    /// <summary>Gets or sets the effective record filter.</summary>
    public FilterExpression? EffectiveRecordFilter { get; init; }
    /// <summary>Gets or sets the effective read mask.</summary>
    public FieldMask? EffectiveReadMask { get; init; }
    /// <summary>Gets or sets the effective write mask.</summary>
    public FieldMask? EffectiveWriteMask { get; init; }
}

/// <summary>Defines the ibase record redactor contract.</summary>
public interface IBaseRecordRedactor
{
    /// <summary>Executes the redact record operation.</summary>
    RecordEnvelope RedactRecord(
        RecordEnvelope record,
        CollectionDefinition collection,
        BasePolicyEvaluation policy,
        VisibilityLevel view);

    /// <summary>Executes the redact page operation.</summary>
    RecordPage RedactPage(
        RecordPage page,
        CollectionDefinition collection,
        BasePolicyEvaluation policy,
        VisibilityLevel view);
}
