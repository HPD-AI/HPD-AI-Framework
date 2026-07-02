namespace HPD.TUI.Models;

public sealed class MultiSelectionModel<T> : CollectionModel<T>
{
    public const int DefaultMaxVisibleItems = 12;

    public MultiSelectionState<T> Selection { get; private set; } = new();

    public IReadOnlySet<string> SelectedKeys => Selection.SelectedKeys;

    public IReadOnlySet<int> SelectedIndexes => CreateSelectedIndexSet();

    public int MinSelected
    {
        get => Selection.MinSelected;
        init => Selection = new MultiSelectionState<T> { MinSelected = value, MaxSelected = Selection.MaxSelected };
    }

    public int? MaxSelected
    {
        get => Selection.MaxSelected;
        init => Selection = new MultiSelectionState<T> { MinSelected = Selection.MinSelected, MaxSelected = value };
    }

    public MultiSelectionModel()
    {
        MaxVisibleItems = DefaultMaxVisibleItems;
    }

    public MultiSelectionModel(ICollectionSource<T> source)
        : base(source)
    {
        MaxVisibleItems = DefaultMaxVisibleItems;
    }

    public new MultiSelectionModel<T> Add(CollectionItem<T> item)
    {
        base.Add(item);
        return this;
    }

    public new MultiSelectionModel<T> Add(T value, string title, string? description = null, string? category = null)
    {
        base.Add(value, title, description, category);
        return this;
    }

    public bool IsSelected(int index)
    {
        var item = GetItemOrDefault(index);
        return item is not null && Selection.IsSelected(item.Key);
    }

    public bool IsSelected(string key) => Selection.IsSelected(key);

    public bool Toggle(int index)
    {
        var item = GetItemOrDefault(index);
        if (item is null || item.Disabled)
        {
            return false;
        }

        return Selection.Toggle(item);
    }

    public bool Toggle(string key)
    {
        var item = FindItem(key);
        if (item is null)
        {
            return false;
        }

        return Selection.Toggle(item);
    }

    public IReadOnlyList<T> GetSelectedValues() => Selection.GetSelectedValues(this);

    public static MultiSelectionModel<T> From(IEnumerable<T> values, Func<T, string> titleSelector)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(titleSelector);

        var model = new MultiSelectionModel<T>();
        foreach (var value in values)
        {
            model.Add(value, titleSelector(value));
        }

        return model;
    }

    private CollectionItem<T>? FindItem(string key)
    {
        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            if (StringComparer.Ordinal.Equals(item.Key, key))
            {
                return item;
            }
        }

        return null;
    }

    private IReadOnlySet<int> CreateSelectedIndexSet()
    {
        var indexes = new HashSet<int>();
        for (var i = 0; i < Items.Count; i++)
        {
            if (Selection.IsSelected(Items[i].Key))
            {
                indexes.Add(i);
            }
        }

        return indexes;
    }
}
