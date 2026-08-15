namespace HPD.Base;

internal sealed record BaseVectorRuntimeRequest
{
    public required CollectionDefinition Collection { get; init; }
    public required VectorIndexDefinition Index { get; init; }
    public required BaseVector Vector { get; init; }
    public required BaseVectorCandidateConstraint Constraint { get; init; }
    public required int Take { get; init; }
    public BaseVectorConsistencyRequirement? Consistency { get; init; }
    public required PrincipalContext Principal { get; init; }
    public required OperationContext Operation { get; init; }
}

internal sealed record BaseVectorRuntimeMatch
{
    public required RecordEnvelope Record { get; init; }
    public required int Rank { get; init; }
    public required BaseVectorMeasure Measure { get; init; }
}

internal sealed record BaseVectorRuntimeResult
{
    public required BaseVectorRuntimeMatch[] Matches { get; init; }
    public required string VectorIndexId { get; init; }
    public required long VectorIndexGeneration { get; init; }
    public required string ProviderId { get; init; }
    public required BaseVectorResultAccuracy Accuracy { get; init; }
    public required BaseVectorConsistencyToken ConsistencyToken { get; init; }
}

internal interface IBaseVectorRuntime
{
    ValueTask<OperationResult<BaseVectorRuntimeResult>> ExecuteAsync(BaseVectorRuntimeRequest request, CancellationToken cancellationToken);
    ValueTask<OperationResult<BaseVectorConsistencyToken>> CaptureAsync(CollectionDefinition collection, VectorIndexDefinition index, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken);
}
