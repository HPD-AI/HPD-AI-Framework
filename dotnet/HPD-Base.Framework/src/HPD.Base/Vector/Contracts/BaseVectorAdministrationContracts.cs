namespace HPD.Base;

/// <summary>Classifies the safe lifecycle state of one vector index.</summary>
public enum BaseVectorIndexState
{
    /// <summary>The index is ready for queries.</summary>
    Ready,
    /// <summary>The index is being rebuilt.</summary>
    Building,
    /// <summary>The index cannot be used until it is rebuilt.</summary>
    RebuildRequired,
    /// <summary>The index is unavailable because publication could not be confirmed.</summary>
    UnhealthyIndeterminate,
}

/// <summary>Contains bounded public state for one installed vector index.</summary>
public sealed record BaseVectorIndexStatus
{
    /// <summary>Gets the collection identifier.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the vector-index identifier.</summary>
    public required string VectorIndexId { get; init; }
    /// <summary>Gets the vector-space identifier.</summary>
    public required string VectorSpaceId { get; init; }
    /// <summary>Gets the published index generation.</summary>
    public required long Generation { get; init; }
    /// <summary>Gets the collection purge generation represented by the index.</summary>
    public required long PurgeGeneration { get; init; }
    /// <summary>Gets the finite applied journal position.</summary>
    public required BaseMutationJournalPosition AppliedThrough { get; init; }
    /// <summary>Gets the lifecycle state.</summary>
    public required BaseVectorIndexState State { get; init; }
    /// <summary>Gets the stable provider identifier.</summary>
    public required string ProviderId { get; init; }
}

/// <summary>Provides bounded vector-index inspection and generation-safe rebuild.</summary>
public interface IBaseVectorAdministration
{
    /// <summary>Lists safe state for every installed vector index.</summary>
    ValueTask<OperationResult<BaseVectorIndexStatus[]>> ListAsync(CancellationToken cancellationToken = default);
    /// <summary>Gets safe state for one vector index.</summary>
    ValueTask<OperationResult<BaseVectorIndexStatus>> GetAsync(string collectionId, string vectorIndexId, CancellationToken cancellationToken = default);
}

internal interface IBaseVectorAdministrationProvider
{
    ValueTask<OperationResult<BaseVectorIndexStatus[]>> ListAsync(CancellationToken cancellationToken);
    ValueTask<OperationResult<BaseVectorIndexStatus>> GetAsync(string collectionId, string vectorIndexId, CancellationToken cancellationToken);
    ValueTask<OperationResult<BaseVectorRebuildResult>> RebuildAsync(BaseVectorRebuildRequest request, CancellationToken cancellationToken);
}
