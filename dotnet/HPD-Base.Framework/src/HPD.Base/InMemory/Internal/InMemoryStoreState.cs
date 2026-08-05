
namespace HPD.Base;

internal sealed class InMemoryStoreState
{
    /// <summary>Gets the collections.</summary>
    public Dictionary<string, InMemoryCollectionState> Collections { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets or sets the next record ID.</summary>
    public long NextRecordId { get; set; }
    /// <summary>Gets or sets the next revision.</summary>
    public long NextRevision { get; set; }
    /// <summary>Gets process-local atomic request receipts.</summary>
    public Dictionary<string, InMemoryMutationReceipt> Receipts { get; } = new(StringComparer.Ordinal);

    /// <summary>Executes the clone operation.</summary>
    public InMemoryStoreState Clone()
    {
        var clone = new InMemoryStoreState
        {
            NextRecordId = NextRecordId,
            NextRevision = NextRevision
        };

        foreach (var (id, collection) in Collections)
            clone.Collections.Add(id, collection.Clone());
        foreach (var (id, receipt) in Receipts)
            clone.Receipts.Add(id, receipt.DeepClone());

        return clone;
    }
}

internal sealed record InMemoryMutationReceipt(
    byte[] Fingerprint,
    byte[] StructuralDigest,
    BaseRecordMutationFact[] Mutations,
    DateTimeOffset ExpiresAt)
{
    public InMemoryMutationReceipt DeepClone() => new(
        [.. Fingerprint],
        [.. StructuralDigest],
        Mutations.Select(RecordCloneHelpers.CloneMutationFact).ToArray(),
        ExpiresAt);
}

internal sealed class InMemoryCollectionState
{
    public long NextAppendPosition { get; set; }
    public long PurgeGeneration { get; set; }
    /// <summary>Gets the records by ID.</summary>
    public Dictionary<string, StoredRecord> RecordsById { get; } = new(StringComparer.Ordinal);

    /// <summary>Executes the clone operation.</summary>
    public InMemoryCollectionState Clone()
    {
        var clone = new InMemoryCollectionState
        {
            NextAppendPosition = NextAppendPosition,
            PurgeGeneration = PurgeGeneration,
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
    long AppendPosition);
