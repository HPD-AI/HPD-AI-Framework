namespace HPD.TUI.Models;

public interface ICollectionSource<T>
{
    int Count { get; }

    CollectionItem<T> GetItem(int index);
}

public interface IAsyncCollectionSource<T>
{
    ValueTask<int> GetCountAsync(CancellationToken cancellationToken = default);

    ValueTask<CollectionItem<T>> GetItemAsync(int index, CancellationToken cancellationToken = default);
}

public sealed class ListCollectionSource<T> : ICollectionSource<T>
{
    private readonly List<CollectionItem<T>> _items = [];

    public int Count => _items.Count;

    public IReadOnlyList<CollectionItem<T>> Items => _items;

    public CollectionItem<T> GetItem(int index) => _items[index];

    public ListCollectionSource<T> Add(CollectionItem<T> item)
    {
        _items.Add(item ?? throw new ArgumentNullException(nameof(item)));
        return this;
    }

    public ListCollectionSource<T> Add(T value, string title, string? description = null, string? category = null)
    {
        return Add(new CollectionItem<T>(CreateKey(_items.Count), value, title, description, category));
    }

    public void Clear()
    {
        _items.Clear();
    }

    private static string CreateKey(int index) => index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class VirtualCollectionSource<T> : ICollectionSource<T>
{
    private readonly Func<int, CollectionItem<T>> _itemFactory;

    public VirtualCollectionSource(int count, Func<int, CollectionItem<T>> itemFactory)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Count = count;
        _itemFactory = itemFactory ?? throw new ArgumentNullException(nameof(itemFactory));
    }

    public int Count { get; }

    public CollectionItem<T> GetItem(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _itemFactory(index);
    }
}

public sealed class ProjectedCollectionSource<TSource, TResult> : ICollectionSource<TResult>
{
    private readonly ICollectionSource<TSource> _source;
    private readonly Func<CollectionItem<TSource>, CollectionItem<TResult>> _projector;

    public ProjectedCollectionSource(
        ICollectionSource<TSource> source,
        Func<CollectionItem<TSource>, CollectionItem<TResult>> projector)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _projector = projector ?? throw new ArgumentNullException(nameof(projector));
    }

    public int Count => _source.Count;

    public CollectionItem<TResult> GetItem(int index) => _projector(_source.GetItem(index));
}

public sealed class FilteredCollectionSource<T> : ICollectionSource<T>
{
    private readonly ICollectionSource<T> _source;
    private readonly Func<CollectionItem<T>, bool> _predicate;
    private readonly List<int> _visibleIndexes = [];

    public FilteredCollectionSource(ICollectionSource<T> source, Func<CollectionItem<T>, bool> predicate)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        Refresh();
    }

    public int Count => _visibleIndexes.Count;

    public CollectionItem<T> GetItem(int index) => _source.GetItem(_visibleIndexes[index]);

    public void Refresh()
    {
        _visibleIndexes.Clear();
        for (var i = 0; i < _source.Count; i++)
        {
            if (_predicate(_source.GetItem(i)))
            {
                _visibleIndexes.Add(i);
            }
        }
    }
}

public sealed class AsyncCollectionSource<T> : ICollectionSource<T>, IAsyncCollectionSource<T>
{
    private readonly IAsyncCollectionSource<T> _source;
    private readonly ListCollectionSource<T> _snapshot = new();

    public AsyncCollectionSource(IAsyncCollectionSource<T> source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public int Count => _snapshot.Count;

    public IReadOnlyList<CollectionItem<T>> Snapshot => _snapshot.Items;

    public CollectionItem<T> GetItem(int index) => _snapshot.GetItem(index);

    public async ValueTask<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return await _source.GetCountAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<CollectionItem<T>> GetItemAsync(int index, CancellationToken cancellationToken = default)
    {
        return await _source.GetItemAsync(index, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        _snapshot.Clear();
        var count = await _source.GetCountAsync(cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _snapshot.Add(await _source.GetItemAsync(i, cancellationToken).ConfigureAwait(false));
        }
    }
}

public sealed class DelegateAsyncCollectionSource<T> : IAsyncCollectionSource<T>
{
    private readonly Func<CancellationToken, ValueTask<int>> _count;
    private readonly Func<int, CancellationToken, ValueTask<CollectionItem<T>>> _getItem;

    public DelegateAsyncCollectionSource(
        Func<CancellationToken, ValueTask<int>> count,
        Func<int, CancellationToken, ValueTask<CollectionItem<T>>> getItem)
    {
        _count = count ?? throw new ArgumentNullException(nameof(count));
        _getItem = getItem ?? throw new ArgumentNullException(nameof(getItem));
    }

    public ValueTask<int> GetCountAsync(CancellationToken cancellationToken = default) => _count(cancellationToken);

    public ValueTask<CollectionItem<T>> GetItemAsync(int index, CancellationToken cancellationToken = default) => _getItem(index, cancellationToken);
}
