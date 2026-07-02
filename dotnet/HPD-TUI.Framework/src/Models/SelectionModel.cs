namespace HPD.TUI.Models;

public sealed class SelectionModel<T> : CollectionModel<T>
{
    public const int DefaultMaxVisibleItems = 12;

    public SelectionModel()
    {
        MaxVisibleItems = DefaultMaxVisibleItems;
    }

    public SelectionModel(ICollectionSource<T> source)
        : base(source)
    {
        MaxVisibleItems = DefaultMaxVisibleItems;
    }

    public T? CurrentValue { get; set; }

    public SelectionState<T> Selection { get; } = new();

    public new SelectionModel<T> Add(CollectionItem<T> item)
    {
        base.Add(item);
        return this;
    }

    public new SelectionModel<T> Add(T value, string title, string? description = null, string? category = null)
    {
        base.Add(value, title, description, category);
        return this;
    }

    public static SelectionModel<T> From(IEnumerable<T> values, Func<T, string> titleSelector)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(titleSelector);

        var model = new SelectionModel<T>();
        foreach (var value in values)
        {
            model.Add(value, titleSelector(value));
        }

        return model;
    }
}
