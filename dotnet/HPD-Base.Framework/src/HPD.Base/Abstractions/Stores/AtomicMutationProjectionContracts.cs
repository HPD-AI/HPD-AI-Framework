using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Identifies the closed JSON value kind carried to a transactional projection.</summary>
public enum BaseAtomicProjectionValueKind
{
    /// <summary>Identifies a JSON null value.</summary>
    Null,
    /// <summary>Identifies a JSON Boolean value.</summary>
    Boolean,
    /// <summary>Identifies a JSON integer value.</summary>
    Integer,
    /// <summary>Identifies a non-integer JSON number value.</summary>
    Number,
    /// <summary>Identifies a JSON string value.</summary>
    String,
    /// <summary>Identifies a JSON array value.</summary>
    Array,
    /// <summary>Identifies a JSON object value.</summary>
    Object,
}

/// <summary>Contains one immutable canonical field value for transactional projection.</summary>
public readonly struct BaseAtomicProjectionValue
{
    internal BaseAtomicProjectionValue(BaseAtomicProjectionValueKind kind, ImmutableArray<byte> canonicalJsonUtf8)
    {
        Kind = kind;
        CanonicalJsonUtf8 = canonicalJsonUtf8;
    }

    /// <summary>Gets the closed value kind.</summary>
    public BaseAtomicProjectionValueKind Kind { get; }

    /// <summary>Gets the canonical UTF-8 JSON bytes.</summary>
    public ImmutableArray<byte> CanonicalJsonUtf8 { get; }
}

/// <summary>Contains one immutable stable field projected from an authoritative record.</summary>
public readonly record struct BaseAtomicProjectionField
{
    internal BaseAtomicProjectionField(string stableFieldId, BaseAtomicProjectionValue value)
    {
        StableFieldId = stableFieldId;
        Value = value;
    }

    /// <summary>Gets the generated stable field identifier.</summary>
    public string StableFieldId { get; }

    /// <summary>Gets the canonical field value.</summary>
    public BaseAtomicProjectionValue Value { get; }
}

/// <summary>Contains an immutable authoritative record snapshot for transactional projections.</summary>
public sealed class BaseAtomicProjectionRecord
{
    internal BaseAtomicProjectionRecord(
        RecordId id,
        RevisionToken revision,
        ImmutableArray<BaseAtomicProjectionField> fields)
    {
        Id = id;
        Revision = revision;
        Fields = fields;
    }

    /// <summary>Gets the authoritative record identifier.</summary>
    public RecordId Id { get; }

    /// <summary>Gets the authoritative record revision.</summary>
    public RevisionToken Revision { get; }

    /// <summary>Gets the canonical stable fields in ordinal identifier order.</summary>
    public ImmutableArray<BaseAtomicProjectionField> Fields { get; }
}

/// <summary>Contains one immutable canonical mutation fact for transactional projections.</summary>
public sealed class BaseAtomicMutationProjectionFact
{
    internal BaseAtomicMutationProjectionFact(
        string? itemId,
        BaseRecordMutationKind requestedOperation,
        BaseCommittedRecordMutationKind committedOperation,
        RecordUpsertOutcome? upsertOutcome,
        string collectionId,
        string eventId,
        BaseMutationJournalPosition journalPosition,
        BaseAtomicProjectionRecord? before,
        BaseAtomicProjectionRecord? after,
        ImmutableArray<string> changedFieldIds)
    {
        ItemId = itemId;
        RequestedOperation = requestedOperation;
        CommittedOperation = committedOperation;
        UpsertOutcome = upsertOutcome;
        CollectionId = collectionId;
        EventId = eventId;
        JournalPosition = journalPosition;
        Before = before;
        After = after;
        ChangedFieldIds = changedFieldIds;
    }

    /// <summary>Gets the optional atomic batch item identifier.</summary>
    public string? ItemId { get; }
    /// <summary>Gets the requested logical mutation.</summary>
    public BaseRecordMutationKind RequestedOperation { get; }
    /// <summary>Gets the provisional physical mutation.</summary>
    public BaseCommittedRecordMutationKind CommittedOperation { get; }
    /// <summary>Gets the selected upsert branch.</summary>
    public RecordUpsertOutcome? UpsertOutcome { get; }
    /// <summary>Gets the stable collection identifier.</summary>
    public string CollectionId { get; }
    /// <summary>Gets the stable event identifier.</summary>
    public string EventId { get; }
    /// <summary>Gets the provider-local provisional journal position.</summary>
    public BaseMutationJournalPosition JournalPosition { get; }
    /// <summary>Gets the authoritative state before the mutation.</summary>
    public BaseAtomicProjectionRecord? Before { get; }
    /// <summary>Gets the authoritative state after the mutation.</summary>
    public BaseAtomicProjectionRecord? After { get; }
    /// <summary>Gets the changed stable field identifiers in ordinal order.</summary>
    public ImmutableArray<string> ChangedFieldIds { get; }
}

/// <summary>Contains one immutable purge-generation transition for transactional projections.</summary>
public sealed class BaseCollectionPurgeProjectionFact
{
    internal BaseCollectionPurgeProjectionFact(string collectionId, long previousGeneration, long publishedGeneration)
    {
        CollectionId = collectionId;
        PreviousGeneration = previousGeneration;
        PublishedGeneration = publishedGeneration;
    }

    /// <summary>Gets the stable collection identifier.</summary>
    public string CollectionId { get; }
    /// <summary>Gets the generation visible before the purge.</summary>
    public long PreviousGeneration { get; }
    /// <summary>Gets the generation provisionally published by the purge.</summary>
    public long PublishedGeneration { get; }
}

/// <summary>Contains one deeply immutable transactional projection request.</summary>
public sealed class BaseAtomicMutationProjectionRequest
{
    internal BaseAtomicMutationProjectionRequest(
        ImmutableArray<BaseAtomicMutationProjectionFact> mutations,
        BaseCollectionPurgeProjectionFact? purge)
    {
        Mutations = mutations;
        Purge = purge;
    }

    /// <summary>Gets the complete ordered mutation facts.</summary>
    public ImmutableArray<BaseAtomicMutationProjectionFact> Mutations { get; }
    /// <summary>Gets the optional purge-generation transition.</summary>
    public BaseCollectionPurgeProjectionFact? Purge { get; }
}
