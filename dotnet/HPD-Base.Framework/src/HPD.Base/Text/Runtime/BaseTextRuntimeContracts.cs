using System.Collections.Immutable;

namespace HPD.Base;

internal sealed record BaseTextRuntimeRequest
{
    public required CollectionDefinition Collection { get; init; }
    public required BaseTextIndexDefinition Index { get; init; }
    public required BaseTextQuery Query { get; init; }
    public required BaseTextCandidateConstraint Constraint { get; init; }
    public required int Take { get; init; }
    public BaseTextCursor? After { get; init; }
    public required BaseTextConsistencyRequirement Consistency { get; init; }
    public required PrincipalContext Principal { get; init; }
    public required OperationContext Operation { get; init; }
}

internal sealed record BaseTextRuntimeMatch
{
    public required RecordEnvelope Record { get; init; }
    public required BaseTextScore Score { get; init; }
    public required RevisionToken Revision { get; init; }
}

internal sealed record BaseTextRuntimeResult
{
    public required ImmutableArray<BaseTextRuntimeMatch> Matches { get; init; }
    public BaseTextCursor? Next { get; init; }
    public required BaseTextConsistencyToken Consistency { get; init; }
}

internal interface IBaseTextRuntime
{
    ValueTask<OperationResult<BaseTextRuntimeResult>> ExecuteAsync(BaseTextRuntimeRequest request, CancellationToken cancellationToken);
}
