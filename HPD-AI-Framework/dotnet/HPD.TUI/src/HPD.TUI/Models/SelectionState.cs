namespace HPD.TUI.Models;

public sealed class SelectionState<T>
{
    public string? SelectedKey { get; set; }

    public T? GetSelectedValue(CollectionModel<T> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (string.IsNullOrEmpty(SelectedKey))
        {
            return default;
        }

        for (var i = 0; i < model.Items.Count; i++)
        {
            var item = model.Items[i];
            if (StringComparer.Ordinal.Equals(item.Key, SelectedKey))
            {
                return item.Value;
            }
        }

        return default;
    }
}

public sealed class MultiSelectionState<T>
{
    private readonly HashSet<string> _selectedKeys = new(StringComparer.Ordinal);

    public IReadOnlySet<string> SelectedKeys => _selectedKeys;

    public int MinSelected { get; init; }

    public int? MaxSelected { get; init; }

    public bool IsSelected(string key) => _selectedKeys.Contains(key);

    public bool Toggle(CollectionItem<T> item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Disabled)
        {
            return false;
        }

        if (_selectedKeys.Remove(item.Key))
        {
            return true;
        }

        if (MaxSelected is { } max && _selectedKeys.Count >= max)
        {
            return false;
        }

        _selectedKeys.Add(item.Key);
        return true;
    }

    public IReadOnlyList<T> GetSelectedValues(CollectionModel<T> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var values = new List<T>(_selectedKeys.Count);
        for (var i = 0; i < model.Items.Count; i++)
        {
            var item = model.Items[i];
            if (_selectedKeys.Contains(item.Key))
            {
                values.Add(item.Value);
            }
        }

        return values;
    }
}
