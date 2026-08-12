
using System.Collections.Immutable;

namespace HPD.Base;

internal sealed class InMemoryStoreState
{
    /// <summary>Gets the collections.</summary>
    public Dictionary<string, InMemoryCollectionState> Collections { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets or sets the next record ID.</summary>
    public long NextRecordId { get; set; }
    /// <summary>Gets or sets the next revision.</summary>
    public long NextRevision { get; set; }
    /// <summary>Gets or sets the global committed mutation position.</summary>
    public long GlobalMutationPosition { get; set; }
    /// <summary>Gets process-local atomic request receipts.</summary>
    public Dictionary<string, InMemoryMutationReceipt> Receipts { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets BASE-owned immutable vector projection slots by canonical collection/index key.</summary>
    public Dictionary<string, InMemoryVectorProjectionState> VectorProjections { get; } = new(StringComparer.Ordinal);

    /// <summary>Executes the clone operation.</summary>
    public InMemoryStoreState Clone()
    {
        var clone = new InMemoryStoreState
        {
            NextRecordId = NextRecordId,
            NextRevision = NextRevision,
            GlobalMutationPosition = GlobalMutationPosition,
        };

        foreach (var (id, collection) in Collections)
            clone.Collections.Add(id, collection.Clone());
        foreach (var (id, receipt) in Receipts)
            clone.Receipts.Add(id, receipt.DeepClone());
        foreach (var (id, projection) in VectorProjections)
            clone.VectorProjections.Add(id, projection);

        return clone;
    }
}

internal sealed class InMemoryVectorProjectionState
{
    internal long AppliedThrough { get; set; }
    internal long Generation { get; set; } = 1;
    internal long PurgeGeneration { get; set; }
    internal Dictionary<string, InMemoryVectorCarrier> Carriers { get; } = new(StringComparer.Ordinal);
    internal InMemoryVectorProjectionState Clone()
    {
        var clone = new InMemoryVectorProjectionState { AppliedThrough = AppliedThrough, Generation = Generation, PurgeGeneration = PurgeGeneration };
        foreach ((string id, InMemoryVectorCarrier carrier) in Carriers) clone.Carriers.Add(id, carrier.Copy());
        return clone;
    }
}

internal sealed record InMemoryVectorCarrier(RecordId RecordId, RevisionToken Revision, long Position, BaseVector Vector)
{
    internal InMemoryVectorCarrier Copy() => this with { Vector = BaseVector.Create(Vector.ToArray()) };
}

internal sealed record InMemoryMutationReceipt(
    byte[] Fingerprint,
    byte[] StructuralDigest,
    BaseAtomicReceiptResult Result,
    DateTimeOffset ExpiresAt)
{
    public InMemoryMutationReceipt DeepClone() => new(
        [.. Fingerprint],
        [.. StructuralDigest],
        CloneReceipt(Result),
        ExpiresAt);

    private static BaseAtomicReceiptResult CloneReceipt(BaseAtomicReceiptResult result) => new()
    {
        Kind = result.Kind,
        Mutations = result.Mutations.Select(static fact => BaseOwnedMutationFact.Freeze(fact.MaterializeOwned(), fact.CodecVersion)).ToImmutableArray(),
        SelectionMutation = result.SelectionMutation is null ? null : result.SelectionMutation with { },
    };
}

internal sealed class InMemoryCollectionState
{
    public long NextAppendPosition { get; set; }
    public long PurgeGeneration { get; set; }
    /// <summary>Gets the records by ID.</summary>
    public Dictionary<string, StoredRecord> RecordsById { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets or sets the optional immutable ordinal successor index used by vector projection scans.</summary>
    public ImmutableSortedSet<string>? RecordIdsOrdinal { get; set; }

    /// <summary>Executes the clone operation.</summary>
    public InMemoryCollectionState Clone()
    {
        var clone = new InMemoryCollectionState
        {
            NextAppendPosition = NextAppendPosition,
            PurgeGeneration = PurgeGeneration,
            RecordIdsOrdinal = RecordIdsOrdinal,
        };
        foreach (var (id, record) in RecordsById)
        {
            clone.RecordsById.Add(id, record with
            {
                Payload = RecordCloneHelpers.ClonePayload(record.Payload),
                Metadata = RecordCloneHelpers.CloneMetadata(record.Metadata)
            });
        }

        return clone;
    }
}

internal sealed record StoredRecord(
    string CollectionId,
    RecordId Id,
    RecordPayload Payload,
    RecordMetadata Metadata,
    long AppendPosition,
    long LatestMutationPosition = 0);
