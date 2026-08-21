using System.Collections.Immutable;

#pragma warning disable CS1591

namespace HPD.Base;

/// <summary>Classifies the safe lifecycle state of one lexical index.</summary>
public enum BaseTextIndexState { Ready = 0, Building = 1, RebuildRequired = 2, UnhealthyIndeterminate = 3 }

/// <summary>Contains bounded non-corpus state for one installed lexical index.</summary>
public sealed record BaseTextIndexStatus
{
    public required string CollectionId { get; init; }
    public required string TextIndexId { get; init; }
    public required int Version { get; init; }
    public required string ProviderId { get; init; }
    public required long Generation { get; init; }
    public required long PurgeGeneration { get; init; }
    public required BaseTextIndexState State { get; init; }
    public required BaseMutationJournalPosition AppliedThrough { get; init; }
    public required BaseMutationJournalPosition SearchVisibleThrough { get; init; }
    public required long CarrierCount { get; init; }
}

/// <summary>Requests one generation-guarded identified lexical rebuild.</summary>
public sealed record BaseTextRebuildRequest
{
    public required string CollectionId { get; init; }
    public required string TextIndexId { get; init; }
    public required long ExpectedGeneration { get; init; }
    public required BaseMutationRequestIdentity Identity { get; init; }
}

/// <summary>Contains verified lexical generation publication evidence.</summary>
public sealed record BaseTextRebuildResult
{
    public required long PreviousGeneration { get; init; }
    public required long PublishedGeneration { get; init; }
    public required BaseMutationJournalPosition VisibleThrough { get; init; }
    public required long RecordCount { get; init; }
    public required ImmutableArray<byte> PublicationChecksum { get; init; }
}

/// <summary>Provides bounded lexical inspection and identified rebuild.</summary>
public interface IBaseTextAdministration
{
    ValueTask<OperationResult<BaseTextIndexStatus[]>> ListAsync(CancellationToken cancellationToken = default);
    ValueTask<OperationResult<BaseTextIndexStatus>> GetAsync(string collectionId, string textIndexId, CancellationToken cancellationToken = default);
    ValueTask<OperationResult<BaseTextRebuildResult>> RebuildAsync(BaseTextRebuildRequest request, CancellationToken cancellationToken = default);
}
