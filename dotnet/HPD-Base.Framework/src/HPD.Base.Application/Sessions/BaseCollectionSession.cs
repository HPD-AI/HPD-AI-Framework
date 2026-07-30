using HPD.Base.Application.Collections;
using HPD.Base.Application.Records;
using HPD.Base.Application.Queries;
using HPD.Base.Application.Results;
using HPD.Base.Records;

namespace HPD.Base.Application.Sessions;

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
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var request = new RecordCreateRequest
        {
            RequestedId = id,
            IdempotencyKey = idempotencyKey,
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

    /// <summary>Deletes one record under an optional revision precondition.</summary>
    public async ValueTask<BaseResult<DeleteResult>> DeleteAsync(
        RecordId id,
        RevisionToken? expectedRevision = null,
        bool returnPrevious = false,
        CancellationToken cancellationToken = default)
    {
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
        RecordUpsertUpdateMode updateMode = RecordUpsertUpdateMode.Replace,
        RecordUpsertExistenceCondition condition = RecordUpsertExistenceCondition.Any,
        RevisionToken? expectedRevision = null,
        CancellationToken cancellationToken = default)
    {
        var request = new RecordUpsertRequest
        {
            Id = id,
            CreatePayload = BaseRecordCodec.Encode(_collection, createValue),
            UpdatePayload = BaseRecordCodec.Encode(_collection, updateValue),
            UpdateMode = updateMode,
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
            RecordUpsertUpdateMode.Replace,
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
        if (failure.Status != HPD.Base.Results.OperationStatus.Conflict)
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
}
