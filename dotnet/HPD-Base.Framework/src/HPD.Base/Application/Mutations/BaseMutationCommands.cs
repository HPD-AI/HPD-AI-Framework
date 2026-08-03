using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

/// <summary>Closed valid application mutation command.</summary>
public abstract class BaseMutation
{
    /// <summary>Initializes a new instance.</summary>
    private protected BaseMutation(string collectionId, RecordId id)
    {
        CollectionId = collectionId;
        Id = id;
    }

    /// <summary>Gets the collection ID.</summary>
    public string CollectionId { get; }
    /// <summary>Gets the ID.</summary>
    public RecordId Id { get; }
}

/// <summary>Represents a base create.</summary>
public sealed class BaseCreate<T> : BaseMutation
{
    internal BaseCreate(BaseCollection<T> collection, RecordId id, T value)
        : base(collection.Id, id)
    {
        Collection = collection;
        Value = value;
    }

    /// <summary>Gets the collection.</summary>
    public BaseCollection<T> Collection { get; }
    /// <summary>Gets the value.</summary>
    public T Value { get; }
}

/// <summary>Represents a base patch.</summary>
public sealed class BasePatch<T, TPatch> : BaseMutation
{
    internal BasePatch(
        BaseCollection<T> collection,
        RecordId id,
        TPatch value,
        JsonTypeInfo<TPatch> jsonTypeInfo,
        RevisionToken? expectedRevision) : base(collection.Id, id)
    {
        Collection = collection;
        Value = value;
        JsonTypeInfo = jsonTypeInfo;
        ExpectedRevision = expectedRevision;
    }

    /// <summary>Gets the collection.</summary>
    public BaseCollection<T> Collection { get; }
    /// <summary>Gets the value.</summary>
    public TPatch Value { get; }
    /// <summary>Gets the JSON type info.</summary>
    public JsonTypeInfo<TPatch> JsonTypeInfo { get; }
    /// <summary>Gets the expected revision.</summary>
    public RevisionToken? ExpectedRevision { get; }
}

/// <summary>Represents a base replace.</summary>
public sealed class BaseReplace<T> : BaseMutation
{
    internal BaseReplace(
        BaseCollection<T> collection,
        RecordId id,
        T value,
        RevisionToken? expectedRevision) : base(collection.Id, id)
    {
        Collection = collection;
        Value = value;
        ExpectedRevision = expectedRevision;
    }

    /// <summary>Gets the collection.</summary>
    public BaseCollection<T> Collection { get; }
    /// <summary>Gets the value.</summary>
    public T Value { get; }
    /// <summary>Gets the expected revision.</summary>
    public RevisionToken? ExpectedRevision { get; }
}

/// <summary>Represents a base delete.</summary>
public sealed class BaseDelete<T> : BaseMutation
{
    internal BaseDelete(
        BaseCollection<T> collection,
        RecordId id,
        RevisionToken? expectedRevision,
        bool returnPrevious) : base(collection.Id, id)
    {
        Collection = collection;
        ExpectedRevision = expectedRevision;
        ReturnPrevious = returnPrevious;
    }

    /// <summary>Gets the collection.</summary>
    public BaseCollection<T> Collection { get; }
    /// <summary>Gets the expected revision.</summary>
    public RevisionToken? ExpectedRevision { get; }
    /// <summary>Gets the return previous.</summary>
    public bool ReturnPrevious { get; }
}

/// <summary>Represents a base upsert.</summary>
public sealed class BaseUpsert<T> : BaseMutation
{
    internal BaseUpsert(
        BaseCollection<T> collection,
        RecordId id,
        T createValue,
        T updateValue,
        RecordUpsertExistenceCondition condition,
        RevisionToken? expectedRevision) : base(collection.Id, id)
    {
        Collection = collection;
        CreateValue = createValue;
        UpdateValue = updateValue;
        Condition = condition;
        ExpectedRevision = expectedRevision;
    }

    /// <summary>Gets the collection.</summary>
    public BaseCollection<T> Collection { get; }
    /// <summary>Gets the create value.</summary>
    public T CreateValue { get; }
    /// <summary>Gets the update value.</summary>
    public T UpdateValue { get; }
    /// <summary>Gets the condition.</summary>
    public RecordUpsertExistenceCondition Condition { get; }
    /// <summary>Gets the expected revision.</summary>
    public RevisionToken? ExpectedRevision { get; }
}
