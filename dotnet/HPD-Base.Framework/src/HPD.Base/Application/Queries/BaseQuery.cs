using System.Runtime.CompilerServices;

namespace HPD.Base;

/// <summary>
/// Builds and executes a bounded typed query over the canonical BASE query AST.
/// </summary>
public sealed class BaseQuery<T>
{
    private readonly BaseSession _session;
    private readonly BaseCollection<T> _collection;
    private readonly FilterExpression[] _filters;
    private readonly QuerySort[] _sort;
    private readonly int? _limit;
    private readonly string? _cursor;

    /// <summary>Atomically patches the bounded selected set through one installed merge-patch profile.</summary>
    public ValueTask<BaseResult<BaseSelectionMutationResult>> PatchSelectedAsync(
        BaseMergePatchSelectionProfile<T> profile,
        RecordPatchRequest patch,
        BasePreviousStateRequirement previousState,
        BaseMutationRequestIdentity? requestIdentity = null,
        BaseSelectionMutationExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return ExecuteSelectionAsync(profile.Profile, patch, previousState, requestIdentity, options, cancellationToken);
    }

    /// <summary>Atomically deletes the bounded selected set through one installed delete profile.</summary>
    public ValueTask<BaseResult<BaseSelectionMutationResult>> DeleteSelectedAsync(
        BaseDeleteSelectionProfile<T> profile,
        BasePreviousStateRequirement previousState,
        BaseMutationRequestIdentity? requestIdentity = null,
        BaseSelectionMutationExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return ExecuteSelectionAsync(profile.Profile, null, previousState, requestIdentity, options, cancellationToken);
    }

    internal BaseQuery(
        BaseSession session,
        BaseCollection<T> collection,
        FilterExpression[]? filters = null,
        QuerySort[]? sort = null,
        int? limit = null,
        string? cursor = null)
    {
        _session = session;
        _collection = collection;
        _filters = filters ?? [];
        _sort = sort ?? [];
        _limit = limit;
        _cursor = cursor;
    }

    /// <summary>Adds a typed equality predicate.</summary>
    public BaseQuery<T> Where<TValue>(
        BaseField<T, TValue> field,
        TValue value)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (!field.Operators.HasFlag(BaseFieldOperator.Equal))
        {
            throw new InvalidOperationException(
                $"Field '{field.Id}' does not support equality queries.");
        }

        return WithFilter(new FilterExpression
        {
            Kind = FilterNodeKind.Compare,
            Field = field.Id,
            Operator = FilterOperator.Equal,
            Value = BaseQueryValue.From(value),
        });
    }

    /// <summary>Adds one immutable typed predicate.</summary>
    public BaseQuery<T> Where(BasePredicate<T> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return WithFilter(predicate.Expression);
    }

    /// <summary>Adds a typed less-than-or-equal predicate.</summary>
    public BaseQuery<T> WhereLessThanOrEqual<TValue>(
        BaseField<T, TValue> field,
        TValue value)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (!field.Operators.HasFlag(BaseFieldOperator.Order))
        {
            throw new InvalidOperationException(
                $"Field '{field.Id}' does not support ordering queries.");
        }

        return WithFilter(new FilterExpression
        {
            Kind = FilterNodeKind.Compare,
            Field = field.Id,
            Operator = FilterOperator.LessThanOrEqual,
            Value = BaseQueryValue.From(value),
        });
    }

    /// <summary>Adds an ascending typed sort.</summary>
    public BaseQuery<T> OrderBy<TValue>(BaseField<T, TValue> field) =>
        AddSort(field, QuerySortDirection.Asc);

    /// <summary>Adds a descending typed sort.</summary>
    public BaseQuery<T> OrderByDescending<TValue>(BaseField<T, TValue> field) =>
        AddSort(field, QuerySortDirection.Desc);

    /// <summary>Adds a subsequent ascending typed sort.</summary>
    public BaseQuery<T> ThenBy<TValue>(BaseField<T, TValue> field) => AddSort(field, QuerySortDirection.Asc);

    /// <summary>Adds a subsequent descending typed sort.</summary>
    public BaseQuery<T> ThenByDescending<TValue>(BaseField<T, TValue> field) => AddSort(field, QuerySortDirection.Desc);

    /// <summary>Adds the stable record identifier as an explicit final ordering key.</summary>
    public BaseQuery<T> ThenByRecordId() => new(
        _session,
        _collection,
        _filters,
        [.. _sort, new QuerySort("id")],
        _limit,
        _cursor);

    /// <summary>Continues this exact query from an opaque provider cursor.</summary>
    public BaseQuery<T> ContinueFrom(string cursor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cursor);
        return new BaseQuery<T>(
            _session, _collection, _filters, _sort, _limit, cursor);
    }

    /// <summary>Applies an explicit positive result bound.</summary>
    public BaseQuery<T> Take(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
        return new BaseQuery<T>(
            _session,
            _collection,
            _filters,
            _sort,
            maximum,
            _cursor);
    }

    /// <summary>Executes one bounded page.</summary>
    public async ValueTask<BaseResult<BasePage<BaseRecord<T>>>> PageAsync(
        CancellationToken cancellationToken = default)
    {
        int limit = _limit ?? throw new InvalidOperationException(
            "A query must declare Take(maximum) before page execution.");
        var query = Build(limit, cursor: _cursor);
        var result = await _session.Runtime.ListAsync(
            _collection.Id,
            query,
            _session.Principal,
            _session.Operation(BaseOperationKind.Query, _collection.Id),
            cancellationToken).ConfigureAwait(false);

        return BaseResultMapper.Map(result, page => Decode(page));
    }

    /// <summary>Returns whether any matching record is visible.</summary>
    public async ValueTask<BaseResult<bool>> AnyAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await ExecutePageAsync(1, cancellationToken).ConfigureAwait(false);
        return result.Match<BaseResult<bool>>(
            success => new BaseSuccess<bool>(
                success.Value.Items.Length != 0,
                success.Status,
                success.Warnings,
                success.Revision,
                success.Events,
                success.Diagnostics),
            failure => new BaseFailure<bool>(
                failure.Status,
                failure.Error,
                failure.Warnings,
                failure.Diagnostics));
    }

    /// <summary>Returns the first visible record or null.</summary>
    public async ValueTask<BaseResult<BaseRecord<T>?>> FirstOrDefaultAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await ExecutePageAsync(1, cancellationToken).ConfigureAwait(false);
        return result.Match<BaseResult<BaseRecord<T>?>>(
            success => new BaseSuccess<BaseRecord<T>?>(
                success.Value.Items.FirstOrDefault(),
                success.Status,
                success.Warnings,
                success.Revision,
                success.Events,
                success.Diagnostics),
            failure => new BaseFailure<BaseRecord<T>?>(
                failure.Status,
                failure.Error,
                failure.Warnings,
                failure.Diagnostics));
    }

    /// <summary>
    /// Returns the only visible record, null when none exists, or a bounded
    /// failure when more than one record matches.
    /// </summary>
    public async ValueTask<BaseResult<BaseRecord<T>?>> SingleOrDefaultAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await ExecutePageAsync(2, cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<BasePage<BaseRecord<T>>> failure)
        {
            return new BaseFailure<BaseRecord<T>?>(
                failure.Status,
                failure.Error,
                failure.Warnings,
                failure.Diagnostics);
        }

        var success = (BaseSuccess<BasePage<BaseRecord<T>>>)result;
        if (success.Value.Items.Length > 1 || success.Value.Page.HasMore)
        {
            return new BaseFailure<BaseRecord<T>?>(
                OperationStatus.Conflict,
                new BaseError
                {
                    Code = "base.application.query.notSingle",
                    Message = "The query matched more than one visible record.",
                    Category = ErrorCategory.Conflict,
                },
                success.Warnings,
                success.Diagnostics);
        }

        return new BaseSuccess<BaseRecord<T>?>(
            success.Value.Items.SingleOrDefault(),
            success.Status,
            success.Warnings,
            success.Revision,
            success.Events,
            success.Diagnostics);
    }

    /// <summary>Returns at most the explicitly declared number of records.</summary>
    public async ValueTask<BaseResult<BaseRecord<T>[]>> ToArrayAsync(
        int maximumItems,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumItems, 1);
        int effectiveLimit = _limit is { } configured
            ? Math.Min(configured, maximumItems)
            : maximumItems;
        var items = new List<BaseRecord<T>>(effectiveLimit);
        BaseSuccess<BasePage<BaseRecord<T>>>? finalPage = null;
        var offset = 0;
        string? cursor = _cursor;

        while (items.Count < effectiveLimit)
        {
            int remaining = Math.Min(
                effectiveLimit - items.Count,
                _session.MaxQueryPageSize);
            BaseResult<BasePage<BaseRecord<T>>> pageResult = await ExecutePageAsync(
                remaining,
                offset,
                cursor,
                cancellationToken).ConfigureAwait(false);
            if (pageResult is BaseFailure<BasePage<BaseRecord<T>>> failure)
            {
                return new BaseFailure<BaseRecord<T>[]>(
                    failure.Status,
                    failure.Error,
                    failure.Warnings,
                    failure.Diagnostics);
            }

            finalPage = (BaseSuccess<BasePage<BaseRecord<T>>>)pageResult;
            BaseRecord<T>[] pageItems = finalPage.Value.Items;
            items.AddRange(pageItems.Take(remaining));
            if (!finalPage.Value.Page.HasMore || items.Count >= effectiveLimit)
            {
                break;
            }

            if (pageItems.Length == 0)
            {
                return ContinuationFailure();
            }

            cursor = finalPage.Value.Page.NextCursor;
            if (cursor is null) offset += pageItems.Length;
        }

        return new BaseSuccess<BaseRecord<T>[]>(
            [.. items],
            finalPage?.Status ?? OperationStatus.Ok,
            finalPage?.Warnings,
            finalPage?.Revision,
            finalPage?.Events,
            finalPage?.Diagnostics);
    }

    /// <summary>
    /// Streams up to the explicit maximum while owning continuation mechanics.
    /// </summary>
    public async IAsyncEnumerable<BaseRecord<T>> StreamAsync(
        int maximumItems,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumItems, 1);
        int effectiveLimit = _limit is { } configured
            ? Math.Min(configured, maximumItems)
            : maximumItems;
        var emitted = 0;
        var offset = 0;
        string? cursor = _cursor;

        while (emitted < effectiveLimit)
        {
            BasePage<BaseRecord<T>> page = (await ExecutePageAsync(
                Math.Min(
                    effectiveLimit - emitted,
                    _session.MaxQueryPageSize),
                offset,
                cursor,
                cancellationToken).ConfigureAwait(false)).RequireValue();
            if (page.Items.Length == 0 && page.Page.HasMore)
            {
                throw BaseOperationException.From(ContinuationFailure());
            }

            foreach (BaseRecord<T> record in page.Items)
            {
                if (emitted >= effectiveLimit)
                {
                    yield break;
                }

                yield return record;
                emitted++;
            }

            if (!page.Page.HasMore)
            {
                yield break;
            }

            cursor = page.Page.NextCursor;
            if (cursor is null) offset += page.Items.Length;
        }
    }

    internal RecordQuery Build(int limit, int offset = 0, string? cursor = null) =>
        new()
        {
            Filter = _filters.Length switch
            {
                0 => null,
                1 => _filters[0],
                _ => new FilterExpression
                {
                    Kind = FilterNodeKind.And,
                    Children = _filters,
                },
            },
            Sort = _sort.Length == 0 ? null : _sort,
            Page = new QueryPage
            {
                Mode = cursor is null ? QueryPaginationMode.Offset : QueryPaginationMode.Cursor,
                Offset = cursor is null ? offset : null,
                Limit = limit,
                Cursor = cursor,
            },
            Count = QueryCountMode.None,
        };

    private ValueTask<BaseResult<BaseSelectionMutationResult>> ExecuteSelectionAsync(
        BaseSelectionOperationProfile profile,
        RecordPatchRequest? patch,
        BasePreviousStateRequirement previousState,
        BaseMutationRequestIdentity? requestIdentity,
        BaseSelectionMutationExecutionOptions? options,
        CancellationToken cancellationToken)
    {
        var runtime = (IBaseSelectionMutationRuntime?)_session.Services.GetService(typeof(IBaseSelectionMutationRuntime))
            ?? throw new InvalidOperationException(BaseSelectionErrorCodes.CapabilityMissing);
        if (_limit is not { } limit || _cursor is not null || _sort.Length == 0
            || !string.Equals(_sort[^1].Field, "id", StringComparison.Ordinal))
            return ValueTask.FromResult<BaseResult<BaseSelectionMutationResult>>(new BaseFailure<BaseSelectionMutationResult>(
                OperationStatus.ValidationFailed,
                new BaseError { Code = BaseSelectionErrorCodes.ContractInvalid, Message = "The selection query contract is invalid.", Category = ErrorCategory.Validation },
                null, null));
        return runtime.ExecuteAsync(_session, _collection.Definition, profile, Build(limit), patch,
            previousState, requestIdentity, options, cancellationToken);
    }

    private BaseQuery<T> WithFilter(FilterExpression filter) =>
        new(
            _session,
            _collection,
            [.. _filters, filter],
            _sort,
            _limit,
            _cursor);

    private BaseQuery<T> AddSort<TValue>(
        BaseField<T, TValue> field,
        QuerySortDirection direction)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (!field.Operators.HasFlag(BaseFieldOperator.Order))
        {
            throw new InvalidOperationException(
                $"Field '{field.Id}' does not support ordering.");
        }

        return new BaseQuery<T>(
            _session,
            _collection,
            _filters,
            [.. _sort, new QuerySort(field.Id, direction)],
            _limit,
            _cursor);
    }

    private async ValueTask<BaseResult<BasePage<BaseRecord<T>>>> ExecutePageAsync(
        int limit,
        int offset,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var query = Build(limit, offset, cursor);
        var result = await _session.Runtime.ListAsync(
            _collection.Id,
            query,
            _session.Principal,
            _session.Operation(BaseOperationKind.Query, _collection.Id),
            cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, Decode);
    }

    private ValueTask<BaseResult<BasePage<BaseRecord<T>>>> ExecutePageAsync(
        int limit,
        CancellationToken cancellationToken) =>
        ExecutePageAsync(limit, 0, _cursor, cancellationToken);

    private static BaseFailure<BaseRecord<T>[]> ContinuationFailure() =>
        new(
            OperationStatus.StoreError,
            new BaseError
            {
                Code = "base.application.query.invalidContinuation",
                Message = "BASE returned an invalid empty continuation page.",
                Category = ErrorCategory.Store,
            },
            warnings: null,
            diagnostics: null);

    private BasePage<BaseRecord<T>> Decode(RecordPage page) =>
        new()
        {
            Items = page.Items
                .Select(envelope => BaseRecordCodec.Decode(_collection, envelope))
                .ToArray(),
            Page = page.Page,
            Count = page.Count,
        };
}
