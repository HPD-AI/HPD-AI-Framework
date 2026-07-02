namespace HPD.TUI.Models;

public class CollectionModel<T>
{
    private readonly ListCollectionSource<T> _listSource;
    private readonly ICollectionSource<T> _source;
    private readonly ItemsView _items;

    public CollectionModel()
        : this(new ListCollectionSource<T>())
    {
    }

    public CollectionModel(ICollectionSource<T> source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _listSource = source as ListCollectionSource<T> ?? new ListCollectionSource<T>();
        _items = new ItemsView(_source);
    }

    public IReadOnlyList<CollectionItem<T>> Items => _items;

    public ICollectionSource<T> Source => _source;

    public ViewportModel Viewport { get; } = new();

    public FilterState<T> Filter { get; } = new();

    public int? MaxVisibleItems { get; init; }

    public int VisibleCount
    {
        get
        {
            var count = 0;
            for (var i = 0; i < Items.Count; i++)
            {
                if (IsVisible(i))
                {
                    count++;
                }
            }

            return count;
        }
    }

    public string Query
    {
        get => Filter.Query;
        set => Filter.Query = value;
    }

    public void AppendQuery(Rune rune)
    {
        Span<char> buffer = stackalloc char[2];
        if (rune.TryEncodeToUtf16(buffer, out var written))
        {
            Query += new string(buffer[..written]);
        }
    }

    public bool BackspaceQuery()
    {
        if (Query.Length == 0)
        {
            return false;
        }

        var removeAt = Query.Length - 1;
        if (removeAt > 0 && char.IsLowSurrogate(Query[removeAt]) && char.IsHighSurrogate(Query[removeAt - 1]))
        {
            removeAt--;
        }

        Query = Query[..removeAt];
        return true;
    }

    public bool AllowFilter { get; init; }

    public bool WrapNavigation { get; init; }

    public string EmptyText { get; init; } = "No results";

    public CollectionModel<T> Add(CollectionItem<T> item)
    {
        EnsureListBacked();
        _listSource.Add(item);
        return this;
    }

    public CollectionModel<T> Add(T value, string title, string? description = null, string? category = null)
    {
        EnsureListBacked();
        _listSource.Add(value, title, description, category);
        return this;
    }

    public CollectionItem<T>? GetItemOrDefault(int index)
    {
        return index < 0 || index >= _source.Count ? null : _source.GetItem(index);
    }

    public bool IsVisible(int index)
    {
        var item = GetItemOrDefault(index);
        if (item is null)
        {
            return false;
        }

        if (!AllowFilter)
        {
            return true;
        }

        return Filter.Matches(item);
    }

    public int GetVisibleIndex(int sourceIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= Items.Count || !IsVisible(sourceIndex))
        {
            return -1;
        }

        var visibleIndex = 0;
        for (var i = 0; i < sourceIndex; i++)
        {
            if (IsVisible(i))
            {
                visibleIndex++;
            }
        }

        return visibleIndex;
    }

    public int GetSourceIndexAtVisibleIndex(int visibleIndex)
    {
        if (visibleIndex < 0)
        {
            return -1;
        }

        var currentVisible = 0;
        for (var i = 0; i < Items.Count; i++)
        {
            if (!IsVisible(i))
            {
                continue;
            }

            if (currentVisible == visibleIndex)
            {
                return i;
            }

            currentVisible++;
        }

        return -1;
    }

    private void EnsureListBacked()
    {
        if (!ReferenceEquals(_source, _listSource))
        {
            throw new InvalidOperationException("Items can only be added directly to list-backed collection models.");
        }
    }

    private sealed class ItemsView : IReadOnlyList<CollectionItem<T>>
    {
        private readonly ICollectionSource<T> _source;

        public ItemsView(ICollectionSource<T> source)
        {
            _source = source;
        }

        public int Count => _source.Count;

        public CollectionItem<T> this[int index] => _source.GetItem(index);

        public IEnumerator<CollectionItem<T>> GetEnumerator()
        {
            for (var i = 0; i < _source.Count; i++)
            {
                yield return _source.GetItem(i);
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
