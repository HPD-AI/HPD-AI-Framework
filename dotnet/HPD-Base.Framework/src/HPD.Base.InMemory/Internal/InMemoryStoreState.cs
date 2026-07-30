using HPD.Base.Records;

namespace HPD.Base.InMemory.Internal;

internal sealed class InMemoryStoreState
{
    public Dictionary<string, InMemoryCollectionState> Collections { get; } = new(StringComparer.Ordinal);
    public long NextRecordId { get; set; }
    public long NextRevision { get; set; }
    public long NextSequence { get; set; }

    public InMemoryStoreState Clone()
    {
        var clone = new InMemoryStoreState
        {
            NextRecordId = NextRecordId,
            NextRevision = NextRevision,
            NextSequence = NextSequence
        };

        foreach (var (id, collection) in Collections)
            clone.Collections.Add(id, collection.Clone());

        return clone;
    }
}

internal sealed class InMemoryCollectionState
{
    public Dictionary<string, StoredRecord> RecordsById { get; } = new(StringComparer.Ordinal);

    public InMemoryCollectionState Clone()
    {
        var clone = new InMemoryCollectionState();
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
    long Sequence);
