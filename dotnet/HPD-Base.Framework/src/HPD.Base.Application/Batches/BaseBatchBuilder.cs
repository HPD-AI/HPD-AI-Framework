using HPD.Base.Application.Collections;
using HPD.Base.Application.Records;
using HPD.Base.Application.Results;
using HPD.Base.Application.Sessions;
using HPD.Base.Records;

namespace HPD.Base.Application.Batches;

/// <summary>
/// Builds one bounded canonical mutation batch without exposing a transaction.
/// </summary>
public sealed class BaseBatchBuilder
{
    private readonly BaseSession _session;
    private readonly BaseRecordBatchExecutionMode _mode;
    private readonly object _owner = new();
    private readonly List<BaseRecordBatchItem> _items = [];
    private bool _committed;

    internal BaseBatchBuilder(
        BaseSession session,
        BaseRecordBatchExecutionMode mode)
    {
        _session = session;
        _mode = mode;
    }

    /// <summary>Adds a typed create command.</summary>
    public BaseBatchItem<T> Create<T>(
        BaseCollection<T> collection,
        RecordId id,
        T value,
        string? idempotencyKey = null)
    {
        EnsureMutable();
        string itemId = NextItemId();
        _items.Add(new BaseRecordBatchItem
        {
            ItemId = itemId,
            CollectionId = collection.Id,
            Kind = BaseRecordMutationKind.Create,
            Create = new RecordCreateRequest
            {
                RequestedId = id,
                IdempotencyKey = idempotencyKey,
                Payload = BaseRecordCodec.Encode(collection, value),
            },
        });
        return new BaseBatchItem<T>(
            _owner,
            itemId,
            collection,
            BaseRecordMutationKind.Create);
    }

    /// <summary>Adds a typed replacement command.</summary>
    public BaseBatchItem<T> Replace<T>(
        BaseCollection<T> collection,
        RecordId id,
        T value,
        RevisionToken? expectedRevision = null)
    {
        EnsureMutable();
        string itemId = NextItemId();
        _items.Add(new BaseRecordBatchItem
        {
            ItemId = itemId,
            CollectionId = collection.Id,
            Kind = BaseRecordMutationKind.Replace,
            RecordId = id,
            Replace = new RecordReplaceRequest
            {
                ExpectedRevision = expectedRevision,
                Payload = BaseRecordCodec.Encode(collection, value),
            },
        });
        return new BaseBatchItem<T>(
            _owner,
            itemId,
            collection,
            BaseRecordMutationKind.Replace);
    }

    /// <summary>Adds a typed upsert command.</summary>
    public BaseBatchItem<T> Upsert<T>(
        BaseCollection<T> collection,
        RecordId id,
        T createValue,
        T updateValue,
        RecordUpsertUpdateMode updateMode = RecordUpsertUpdateMode.Replace,
        RecordUpsertExistenceCondition condition = RecordUpsertExistenceCondition.Any,
        RevisionToken? expectedRevision = null)
    {
        EnsureMutable();
        string itemId = NextItemId();
        _items.Add(new BaseRecordBatchItem
        {
            ItemId = itemId,
            CollectionId = collection.Id,
            Kind = BaseRecordMutationKind.Upsert,
            Upsert = new RecordUpsertRequest
            {
                Id = id,
                CreatePayload = BaseRecordCodec.Encode(collection, createValue),
                UpdatePayload = BaseRecordCodec.Encode(collection, updateValue),
                UpdateMode = updateMode,
                Condition = condition,
                ExpectedRevision = expectedRevision,
            },
        });
        return new BaseBatchItem<T>(
            _owner,
            itemId,
            collection,
            BaseRecordMutationKind.Upsert);
    }

    /// <summary>Adds a delete command.</summary>
    public BaseDeleteBatchItem Delete<T>(
        BaseCollection<T> collection,
        RecordId id,
        RevisionToken? expectedRevision = null,
        bool returnPrevious = false)
    {
        EnsureMutable();
        string itemId = NextItemId();
        _items.Add(new BaseRecordBatchItem
        {
            ItemId = itemId,
            CollectionId = collection.Id,
            Kind = BaseRecordMutationKind.Delete,
            RecordId = id,
            Delete = new RecordDeleteRequest
            {
                ExpectedRevision = expectedRevision,
                ReturnPrevious = returnPrevious,
            },
        });
        return new BaseDeleteBatchItem(_owner, itemId);
    }

    /// <summary>Executes the batch exactly once through the canonical Runtime.</summary>
    public async ValueTask<BaseResult<BaseBatchResult>> CommitAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureMutable();
        if (_items.Count == 0)
        {
            throw new InvalidOperationException(
                "A batch must contain at least one mutation.");
        }

        _committed = true;
        var request = new BaseRecordBatchRequest
        {
            Mode = _mode,
            Operations = [.. _items],
        };
        var result = await _session.Runtime.BatchAsync(
            request,
            _session.Principal,
            _session.Operation(BaseOperationKind.Batch, "base"),
            cancellationToken).ConfigureAwait(false);

        return BaseResultMapper.Map(
            result,
            batch => new BaseBatchResult(_owner, batch));
    }

    private string NextItemId() =>
        $"item_{_items.Count.ToString("D4", System.Globalization.CultureInfo.InvariantCulture)}";

    private void EnsureMutable()
    {
        if (_committed)
        {
            throw new InvalidOperationException(
                "A batch cannot be changed or committed more than once.");
        }
    }
}
