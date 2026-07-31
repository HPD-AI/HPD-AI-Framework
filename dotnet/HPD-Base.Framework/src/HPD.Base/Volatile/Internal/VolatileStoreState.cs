
namespace HPD.Base;

internal sealed class VolatileStoreState
{
    public Dictionary<string, VolatileCollectionState> Collections { get; } = new(StringComparer.Ordinal);
    public long NextRecordId { get; set; }
    public long NextRevision { get; set; }
    public long NextSequence { get; set; }

    public VolatileStoreState Clone()
    {
        var clone = new VolatileStoreState
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

internal sealed class VolatileCollectionState
{
    public Dictionary<string, StoredRecord> RecordsById { get; } = new(StringComparer.Ordinal);

    public VolatileCollectionState Clone()
    {
        var clone = new VolatileCollectionState();
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
