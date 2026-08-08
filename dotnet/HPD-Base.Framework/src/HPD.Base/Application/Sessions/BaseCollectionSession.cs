using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

/// <summary>
/// Executes typed operations for one collection through the canonical Runtime.
/// </summary>
public sealed class BaseCollectionSession<T>
{
    private readonly BaseSession _session;
    private readonly BaseCollection<T> _collection;

    internal BaseCollectionSession(
        BaseSession session,
        BaseCollection<T> collection)
    {
        _session = session;
        _collection = collection;
    }

    /// <summary>Gets the typed collection contract.</summary>
    public BaseCollection<T> Contract => _collection;
    internal BaseSession Session => _session;

    /// <summary>Begins a typed bounded query.</summary>
    public BaseQuery<T> Query() => new(_session, _collection);

    /// <summary>Gets one policy-projected typed record.</summary>
    public async ValueTask<BaseResult<BaseRecord<T>>> GetAsync(
        RecordId id,
        CancellationToken cancellationToken = default)
    {
        var result = await _session.Runtime.GetAsync(
            _collection.Id,
            id,
            _session.Principal,
            _session.Operation(BaseOperationKind.Get, _collection.Id, id),
            cancellationToken).ConfigureAwait(false);

        return BaseResultMapper.Map(
            result,
            envelope => BaseRecordCodec.Decode(_collection, envelope));
    }

    /// <summary>Creates one typed record.</summary>
    public async ValueTask<BaseResult<BaseRecord<T>>> CreateAsync(
        RecordId id,
        T value,
        CancellationToken cancellationToken = default)
    {
        if (MutationFailure<BaseRecord<T>>(BaseRecordMutationKind.Create) is { } failure)
            return failure;

        var request = new RecordCreateRequest
        {
            RequestedId = id,
            Payload = BaseRecordCodec.Encode(_collection, value),
        };
        var result = await _session.Runtime.CreateAsync(
            _collection.Id,
            request,
            _session.Principal,
            _session.Operation(BaseOperationKind.Create, _collection.Id, id),
            cancellationToken).ConfigureAwait(false);

        return BaseResultMapper.Map(
            result,
            envelope => BaseRecordCodec.Decode(_collection, envelope));
    }

    /// <summary>Fully replaces one typed record.</summary>
    public async ValueTask<BaseResult<BaseRecord<T>>> ReplaceAsync(
        RecordId id,
        T value,
        RevisionToken? expectedRevision = null,
        CancellationToken cancellationToken = default)
    {
        if (MutationFailure<BaseRecord<T>>(BaseRecordMutationKind.Replace) is { } failure)
            return failure;

        var request = new RecordReplaceRequest
        {
            ExpectedRevision = expectedRevision,
            Payload = BaseRecordCodec.Encode(_collection, value),
        };
        var result = await _session.Runtime.ReplaceAsync(
            _collection.Id,
            id,
            request,
            _session.Principal,
            _session.Operation(BaseOperationKind.Replace, _collection.Id, id),
            cancellationToken).ConfigureAwait(false);

        return BaseResultMapper.Map(
            result,
            envelope => BaseRecordCodec.Decode(_collection, envelope));
    }

    /// <summary>Applies a typed merge patch using explicit source-generated JSON metadata.</summary>
    public async ValueTask<BaseResult<BaseRecord<T>>> PatchAsync<TPatch>(
        RecordId id,
        TPatch patch,
        JsonTypeInfo<TPatch> patchJsonTypeInfo,
        RevisionToken? expectedRevision = null,
        CancellationToken cancellationToken = default)
    {
        if (MutationFailure<BaseRecord<T>>(BaseRecordMutationKind.Patch) is { } failure)
            return failure;

        var request = new RecordPatchRequest
        {
            ExpectedRevision = expectedRevision,
            Patch = BaseRecordCodec.Encode(patch, patchJsonTypeInfo),
        };
        var result = await _session.Runtime.PatchAsync(
            _collection.Id,
            id,
            request,
            _session.Principal,
            _session.Operation(BaseOperationKind.Patch, _collection.Id, id),
            cancellationToken).ConfigureAwait(false);

        return BaseResultMapper.Map(
            result,
            envelope => BaseRecordCodec.Decode(_collection, envelope));
    }

    /// <summary>Deletes one record under an optional revision precondition.</summary>
    public async ValueTask<BaseResult<DeleteResult>> DeleteAsync(
        RecordId id,
        RevisionToken? expectedRevision = null,
        bool returnPrevious = false,
        CancellationToken cancellationToken = default)
    {
        if (MutationFailure<DeleteResult>(BaseRecordMutationKind.Delete) is { } failure)
            return failure;

        var request = new RecordDeleteRequest
        {
            ExpectedRevision = expectedRevision,
            ReturnPrevious = returnPrevious,
        };
        var result = await _session.Runtime.DeleteAsync(
            _collection.Id,
            id,
            request,
            _session.Principal,
            _session.Operation(BaseOperationKind.Delete, _collection.Id, id),
            cancellationToken).ConfigureAwait(false);

        return BaseResultMapper.Map(result, value => value);
    }

    /// <summary>
    /// Atomically creates or updates a record by its stable identifier.
    /// </summary>
    public async ValueTask<BaseResult<BaseUpsertResult<T>>> UpsertAsync(
        RecordId id,
        T createValue,
        T updateValue,
        RecordUpsertExistenceCondition condition = RecordUpsertExistenceCondition.Any,
        RevisionToken? expectedRevision = null,
        CancellationToken cancellationToken = default)
    {
        if (MutationFailure<BaseUpsertResult<T>>(BaseRecordMutationKind.Upsert, condition) is { } failure)
            return failure;

        var request = new RecordUpsertRequest
        {
            Id = id,
            CreatePayload = BaseRecordCodec.Encode(_collection, createValue),
            UpdatePayload = BaseRecordCodec.Encode(_collection, updateValue),
            UpdateMode = RecordUpsertUpdateMode.Replace,
            Condition = condition,
            ExpectedRevision = expectedRevision,
        };
        var result = await _session.Runtime.UpsertAsync(
            _collection.Id,
            request,
            _session.Principal,
            _session.Operation(BaseOperationKind.Upsert, _collection.Id, id),
            cancellationToken).ConfigureAwait(false);

        return BaseResultMapper.Map(
            result,
            upsert => new BaseUpsertResult<T>
            {
                Outcome = upsert.Outcome,
                Record = BaseRecordCodec.Decode(_collection, upsert.Record),
            });
    }

    /// <summary>Atomically creates a record or applies a typed merge patch.</summary>
    public async ValueTask<BaseResult<BaseUpsertResult<T>>> UpsertPatchAsync<TPatch>(
        RecordId id,
        T createValue,
        TPatch patch,
        JsonTypeInfo<TPatch> patchJsonTypeInfo,
        RecordUpsertExistenceCondition condition = RecordUpsertExistenceCondition.Any,
        RevisionToken? expectedRevision = null,
        CancellationToken cancellationToken = default)
    {
        if (MutationFailure<BaseUpsertResult<T>>(BaseRecordMutationKind.Upsert, condition) is { } failure)
            return failure;

        var request = new RecordUpsertRequest
        {
            Id = id,
            CreatePayload = BaseRecordCodec.Encode(_collection, createValue),
            UpdatePayload = BaseRecordCodec.Encode(patch, patchJsonTypeInfo),
            UpdateMode = RecordUpsertUpdateMode.Patch,
            Condition = condition,
            ExpectedRevision = expectedRevision,
        };
        var result = await _session.Runtime.UpsertAsync(
            _collection.Id,
            request,
            _session.Principal,
            _session.Operation(BaseOperationKind.Upsert, _collection.Id, id),
            cancellationToken).ConfigureAwait(false);

        return BaseResultMapper.Map(
            result,
            upsert => new BaseUpsertResult<T>
            {
                Outcome = upsert.Outcome,
                Record = BaseRecordCodec.Decode(_collection, upsert.Record),
            });
    }

    /// <summary>
    /// Creates a record when absent or reads the existing record without updating it.
    /// </summary>
    public async ValueTask<BaseResult<BaseEnsureResult<T>>> EnsureAsync(
        RecordId id,
        T createValue,
        CancellationToken cancellationToken = default)
    {
        var attempted = await UpsertAsync(
            id,
            createValue,
            createValue,
            RecordUpsertExistenceCondition.CreateOnly,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (attempted is BaseSuccess<BaseUpsertResult<T>> created)
        {
            return new BaseSuccess<BaseEnsureResult<T>>(
                new BaseEnsureResult<T>
                {
                    Outcome = BaseEnsureOutcome.Created,
                    Record = created.Value.Record,
                },
                created.Status,
                created.Warnings,
                created.Revision,
                created.Events,
                created.Diagnostics);
        }

        var failure = (BaseFailure<BaseUpsertResult<T>>)attempted;
        if (failure.Status != OperationStatus.Conflict)
        {
            return new BaseFailure<BaseEnsureResult<T>>(
                failure.Status,
                failure.Error,
                failure.Warnings,
                failure.Diagnostics);
        }

        var existing = await GetAsync(id, cancellationToken).ConfigureAwait(false);
        return existing.Match<BaseResult<BaseEnsureResult<T>>>(
            success => new BaseSuccess<BaseEnsureResult<T>>(
                new BaseEnsureResult<T>
                {
                    Outcome = BaseEnsureOutcome.AlreadyExisted,
                    Record = success.Value,
                },
                success.Status,
                success.Warnings,
                success.Revision,
                success.Events,
                success.Diagnostics),
            getFailure => new BaseFailure<BaseEnsureResult<T>>(
                getFailure.Status,
                getFailure.Error,
                getFailure.Warnings,
                getFailure.Diagnostics));
    }

    private BaseFailure<TResult>? MutationFailure<TResult>(
        BaseRecordMutationKind kind,
        RecordUpsertExistenceCondition? upsertCondition = null)
    {
        BaseCollectionMutationMode mode = _collection.Definition.MutationMode;
        bool allowed = mode switch
        {
            BaseCollectionMutationMode.Mutable => true,
            BaseCollectionMutationMode.AppendOnly or BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge =>
                kind == BaseRecordMutationKind.Create
                || kind == BaseRecordMutationKind.Upsert && upsertCondition == RecordUpsertExistenceCondition.CreateOnly,
            BaseCollectionMutationMode.ReadOnly => false,
            _ => false,
        };
        if (allowed)
            return null;

        string code = !Enum.IsDefined(mode)
            ? BaseCollectionErrorCodes.MutationModeInvalid
            : mode == BaseCollectionMutationMode.ReadOnly
                ? BaseCollectionErrorCodes.ReadOnlyMutationForbidden
                : kind is BaseRecordMutationKind.Patch or BaseRecordMutationKind.Replace or BaseRecordMutationKind.Upsert
                    ? BaseCollectionErrorCodes.AppendOnlyUpdateForbidden
                    : BaseCollectionErrorCodes.AppendOnlyDeleteForbidden;
        return new BaseFailure<TResult>(
            OperationStatus.ValidationFailed,
            new BaseError
            {
                Code = code,
                Message = "The collection mutation mode does not permit this operation.",
                Category = ErrorCategory.Validation,
            },
            null,
            null);
    }
}
