using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

/// <summary>
/// Builds one bounded canonical mutation batch without exposing a transaction.
/// </summary>
public sealed class BaseBatchBuilder
{
    private readonly BaseSession _session;
    private readonly BaseRecordBatchExecutionMode _mode;
    private readonly BaseMutationRequestIdentity? _requestIdentity;
    private readonly BaseActivationGuard? _activationGuard;
    private readonly object _owner = new();
    private readonly List<BaseRecordBatchItem> _items = [];
    private bool _committed;

    internal BaseBatchBuilder(
        BaseSession session,
        BaseRecordBatchExecutionMode mode,
        BaseMutationRequestIdentity? requestIdentity = null,
        BaseActivationGuard? activationGuard = null)
    {
        _session = session;
        _mode = mode;
        _requestIdentity = requestIdentity;
        _activationGuard = activationGuard;
    }

    /// <summary>Adds a typed create command.</summary>
    public BaseBatchItem<T> Create<T>(
        BaseCollection<T> collection,
        RecordId id,
        T value)
    {
        EnsureMutable();
        EnsureCollectionAllows(collection.Definition, BaseRecordMutationKind.Create);
        string itemId = NextItemId();
        _items.Add(new BaseRecordBatchItem
        {
            ItemId = itemId,
            CollectionId = collection.Id,
            Kind = BaseRecordMutationKind.Create,
            Create = new RecordCreateRequest
            {
                RequestedId = id,
                Payload = BaseRecordCodec.Encode(value, _session.Serializer(collection)),
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
        EnsureCollectionAllows(collection.Definition, BaseRecordMutationKind.Replace);
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
                Payload = BaseRecordCodec.Encode(value, _session.Serializer(collection)),
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
        RecordUpsertExistenceCondition condition = RecordUpsertExistenceCondition.Any,
        RevisionToken? expectedRevision = null)
    {
        EnsureMutable();
        EnsureCollectionAllows(collection.Definition, BaseRecordMutationKind.Upsert, condition);
        string itemId = NextItemId();
        _items.Add(new BaseRecordBatchItem
        {
            ItemId = itemId,
            CollectionId = collection.Id,
            Kind = BaseRecordMutationKind.Upsert,
            Upsert = new RecordUpsertRequest
            {
                Id = id,
                CreatePayload = BaseRecordCodec.Encode(createValue, _session.Serializer(collection)),
                UpdatePayload = BaseRecordCodec.Encode(updateValue, _session.Serializer(collection)),
                UpdateMode = RecordUpsertUpdateMode.Replace,
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

    /// <summary>Adds a typed patch command.</summary>
    public BaseBatchItem<T> Patch<T, TPatch>(
        BaseCollection<T> collection,
        RecordId id,
        TPatch patch,
        JsonTypeInfo<TPatch> patchJsonTypeInfo,
        RevisionToken? expectedRevision = null)
    {
        EnsureMutable();
        EnsureCollectionAllows(collection.Definition, BaseRecordMutationKind.Patch);
        string itemId = NextItemId();
        _items.Add(new BaseRecordBatchItem
        {
            ItemId = itemId,
            CollectionId = collection.Id,
            Kind = BaseRecordMutationKind.Patch,
            RecordId = id,
            Patch = new RecordPatchRequest
            {
                ExpectedRevision = expectedRevision,
                Patch = BaseRecordCodec.Encode(patch, patchJsonTypeInfo),
            },
        });
        return new BaseBatchItem<T>(
            _owner,
            itemId,
            collection,
            BaseRecordMutationKind.Patch);
    }

    /// <summary>Adds a typed create-or-patch upsert command.</summary>
    public BaseBatchItem<T> UpsertPatch<T, TPatch>(
        BaseCollection<T> collection,
        RecordId id,
        T createValue,
        TPatch patch,
        JsonTypeInfo<TPatch> patchJsonTypeInfo,
        RecordUpsertExistenceCondition condition = RecordUpsertExistenceCondition.Any,
        RevisionToken? expectedRevision = null)
    {
        EnsureMutable();
        EnsureCollectionAllows(collection.Definition, BaseRecordMutationKind.Upsert, condition);
        string itemId = NextItemId();
        _items.Add(new BaseRecordBatchItem
        {
            ItemId = itemId,
            CollectionId = collection.Id,
            Kind = BaseRecordMutationKind.Upsert,
            Upsert = new RecordUpsertRequest
            {
                Id = id,
                CreatePayload = BaseRecordCodec.Encode(createValue, _session.Serializer(collection)),
                UpdatePayload = BaseRecordCodec.Encode(patch, patchJsonTypeInfo),
                UpdateMode = RecordUpsertUpdateMode.Patch,
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
        EnsureCollectionAllows(collection.Definition, BaseRecordMutationKind.Delete);
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
            RequestIdentity = _requestIdentity,
            ActivationGuard = _activationGuard,
        };
        var result = await _session.Runtime.BatchAsync(
            request,
            _session.Principal,
            _session.Operation(BaseOperationKind.Batch, "base"),
            cancellationToken).ConfigureAwait(false);

        return BaseResultMapper.Map(
            result,
            batch => new BaseBatchResult(_owner, batch, _session));
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

    private static void EnsureCollectionAllows(
        CollectionDefinition collection,
        BaseRecordMutationKind kind,
        RecordUpsertExistenceCondition? condition = null)
    {
        bool allowed = collection.MutationMode switch
        {
            BaseCollectionMutationMode.Mutable => true,
            BaseCollectionMutationMode.AppendOnly or BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge =>
                kind == BaseRecordMutationKind.Create
                || kind == BaseRecordMutationKind.Upsert && condition == RecordUpsertExistenceCondition.CreateOnly,
            BaseCollectionMutationMode.ReadOnly => false,
            _ => false,
        };
        if (allowed)
            return;

        string code = !Enum.IsDefined(collection.MutationMode)
            ? BaseCollectionErrorCodes.MutationModeInvalid
            : collection.MutationMode == BaseCollectionMutationMode.ReadOnly
                ? BaseCollectionErrorCodes.ReadOnlyMutationForbidden
                : kind is BaseRecordMutationKind.Patch or BaseRecordMutationKind.Replace or BaseRecordMutationKind.Upsert
                    ? BaseCollectionErrorCodes.AppendOnlyUpdateForbidden
                    : BaseCollectionErrorCodes.AppendOnlyDeleteForbidden;
        throw new InvalidOperationException($"{code}: The collection mutation mode does not permit this batch operation.");
    }
}
