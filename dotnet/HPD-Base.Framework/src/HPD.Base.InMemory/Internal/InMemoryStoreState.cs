using HPD.Base.Records;

namespace HPD.Base.InMemory.Internal;

internal sealed class InMemoryStoreState
{
    public object Gate { get; } = new();
    public Dictionary<string, InMemoryCollectionState> Collections { get; } = new(StringComparer.Ordinal);
    public long NextRecordId { get; set; }
    public long NextRevision { get; set; }
    public long NextSequence { get; set; }
}

internal sealed class InMemoryCollectionState
{
    public Dictionary<string, StoredRecord> RecordsById { get; } = new(StringComparer.Ordinal);
}

internal sealed record StoredRecord(
    string CollectionId,
    RecordId Id,
    RecordPayload Payload,
    RecordMetadata Metadata,
    long Sequence);
