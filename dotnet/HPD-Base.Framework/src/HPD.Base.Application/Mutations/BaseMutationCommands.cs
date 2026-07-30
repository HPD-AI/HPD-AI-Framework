using System.Text.Json.Serialization.Metadata;
using HPD.Base.Application.Collections;
using HPD.Base.Records;

namespace HPD.Base.Application.Mutations;

/// <summary>Closed valid application mutation command.</summary>
public abstract class BaseMutation
{
    private protected BaseMutation(string collectionId, RecordId id)
    {
        CollectionId = collectionId;
        Id = id;
    }

    public string CollectionId { get; }
    public RecordId Id { get; }
}

public sealed class BaseCreate<T> : BaseMutation
{
    internal BaseCreate(BaseCollection<T> collection, RecordId id, T value, string? idempotencyKey)
        : base(collection.Id, id)
    {
        Collection = collection;
        Value = value;
        IdempotencyKey = idempotencyKey;
    }

    public BaseCollection<T> Collection { get; }
    public T Value { get; }
    public string? IdempotencyKey { get; }
}

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

    public BaseCollection<T> Collection { get; }
    public TPatch Value { get; }
    public JsonTypeInfo<TPatch> JsonTypeInfo { get; }
    public RevisionToken? ExpectedRevision { get; }
}

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

    public BaseCollection<T> Collection { get; }
    public T Value { get; }
    public RevisionToken? ExpectedRevision { get; }
}

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

    public BaseCollection<T> Collection { get; }
    public RevisionToken? ExpectedRevision { get; }
    public bool ReturnPrevious { get; }
}

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

    public BaseCollection<T> Collection { get; }
    public T CreateValue { get; }
    public T UpdateValue { get; }
    public RecordUpsertExistenceCondition Condition { get; }
    public RevisionToken? ExpectedRevision { get; }
}
