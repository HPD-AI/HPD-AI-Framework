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

    internal BaseQuery(
        BaseSession session,
        BaseCollection<T> collection,
        FilterExpression[]? filters = null,
        QuerySort[]? sort = null,
        int? limit = null)
    {
        _session = session;
        _collection = collection;
        _filters = filters ?? [];
        _sort = sort ?? [];
        _limit = limit;
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
                $"Field '{field.Path}' does not support equality queries.");
        }

        return WithFilter(new FilterExpression
        {
            Kind = FilterNodeKind.Compare,
            Field = field.Path,
            Operator = FilterOperator.Equal,
            Value = BaseQueryValue.From(value),
        });
    }

    /// <summary>Adds an ascending typed sort.</summary>
    public BaseQuery<T> OrderBy<TValue>(BaseField<T, TValue> field) =>
        AddSort(field, QuerySortDirection.Asc);

    /// <summary>Adds a descending typed sort.</summary>
    public BaseQuery<T> OrderByDescending<TValue>(BaseField<T, TValue> field) =>
        AddSort(field, QuerySortDirection.Desc);

    /// <summary>Applies an explicit positive result bound.</summary>
    public BaseQuery<T> Take(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
        return new BaseQuery<T>(
            _session,
            _collection,
            _filters,
            _sort,
            maximum);
    }

    /// <summary>Executes one bounded page.</summary>
    public async ValueTask<BaseResult<BasePage<T>>> PageAsync(
        CancellationToken cancellationToken = default)
    {
        int limit = _limit ?? throw new InvalidOperationException(
            "A query must declare Take(maximum) before page execution.");
        var query = Build(limit);
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
        if (result is BaseFailure<BasePage<T>> failure)
        {
            return new BaseFailure<BaseRecord<T>?>(
                failure.Status,
                failure.Error,
                failure.Warnings,
                failure.Diagnostics);
        }

        var success = (BaseSuccess<BasePage<T>>)result;
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
        BaseSuccess<BasePage<T>>? finalPage = null;
        var offset = 0;

        while (items.Count < effectiveLimit)
        {
            int remaining = Math.Min(
                effectiveLimit - items.Count,
                _session.MaxQueryPageSize);
            BaseResult<BasePage<T>> pageResult = await ExecutePageAsync(
                remaining,
                offset,
                cancellationToken).ConfigureAwait(false);
            if (pageResult is BaseFailure<BasePage<T>> failure)
            {
                return new BaseFailure<BaseRecord<T>[]>(
                    failure.Status,
                    failure.Error,
                    failure.Warnings,
                    failure.Diagnostics);
            }

            finalPage = (BaseSuccess<BasePage<T>>)pageResult;
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

            offset += pageItems.Length;
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

        while (emitted < effectiveLimit)
        {
            BasePage<T> page = (await ExecutePageAsync(
                Math.Min(
                    effectiveLimit - emitted,
                    _session.MaxQueryPageSize),
                offset,
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

            offset += page.Items.Length;
        }
    }

    internal RecordQuery Build(int limit, int offset = 0) =>
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
                Mode = QueryPaginationMode.Offset,
                Offset = offset,
                Limit = limit,
            },
            Count = QueryCountMode.None,
        };

    private BaseQuery<T> WithFilter(FilterExpression filter) =>
        new(
            _session,
            _collection,
            [.. _filters, filter],
            _sort,
            _limit);

    private BaseQuery<T> AddSort<TValue>(
        BaseField<T, TValue> field,
        QuerySortDirection direction)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (!field.Operators.HasFlag(BaseFieldOperator.Order))
        {
            throw new InvalidOperationException(
                $"Field '{field.Path}' does not support ordering.");
        }

        return new BaseQuery<T>(
            _session,
            _collection,
            _filters,
            [.. _sort, new QuerySort(field.Path, direction)],
            _limit);
    }

    private async ValueTask<BaseResult<BasePage<T>>> ExecutePageAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var query = Build(limit, offset);
        var result = await _session.Runtime.ListAsync(
            _collection.Id,
            query,
            _session.Principal,
            _session.Operation(BaseOperationKind.Query, _collection.Id),
            cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, Decode);
    }

    private ValueTask<BaseResult<BasePage<T>>> ExecutePageAsync(
        int limit,
        CancellationToken cancellationToken) =>
        ExecutePageAsync(limit, 0, cancellationToken);

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

    private BasePage<T> Decode(RecordPage page) =>
        new()
        {
            Items = page.Items
                .Select(envelope => BaseRecordCodec.Decode(_collection, envelope))
                .ToArray(),
            Page = page.Page,
            Count = page.Count,
        };
}
