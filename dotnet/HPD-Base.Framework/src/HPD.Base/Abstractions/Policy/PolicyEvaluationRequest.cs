
namespace HPD.Base;

public sealed record PolicyEvaluationRequest
{
    public required OperationContext Operation { get; init; }
    public required PrincipalContext Principal { get; init; }
    public required CollectionDefinition Collection { get; init; }
    public required PolicyResource Resource { get; init; }
    public AccessGrant[]? Grants { get; init; }
    public Dictionary<string, string>? PolicyRefs { get; init; }
}

public sealed record PolicyResource
{
    public required PolicyResourceKind Kind { get; init; }
    public RecordQuery? Query { get; init; }
    public RecordEnvelope? ExistingRecord { get; init; }
    public RecordPayload? ProposedPayload { get; init; }
    public RecordEnvelope? ProposedRecord { get; init; }
    public string? RecordId { get; init; }
    public string? FieldPath { get; init; }
}
