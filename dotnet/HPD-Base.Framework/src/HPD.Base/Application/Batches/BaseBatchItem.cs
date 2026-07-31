
namespace HPD.Base;

/// <summary>
/// Identifies one typed record-producing item in a batch.
/// </summary>
public sealed class BaseBatchItem<T>
{
    internal BaseBatchItem(
        object owner,
        string itemId,
        BaseCollection<T> collection,
        BaseRecordMutationKind kind)
    {
        Owner = owner;
        ItemId = itemId;
        Collection = collection;
        Kind = kind;
    }

    internal object Owner { get; }
    internal string ItemId { get; }
    internal BaseCollection<T> Collection { get; }
    internal BaseRecordMutationKind Kind { get; }
}

/// <summary>
/// Identifies one delete item in a batch.
/// </summary>
public sealed class BaseDeleteBatchItem
{
    internal BaseDeleteBatchItem(
        object owner,
        string itemId)
    {
        Owner = owner;
        ItemId = itemId;
    }

    internal object Owner { get; }
    internal string ItemId { get; }
}
