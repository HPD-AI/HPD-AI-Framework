namespace HPD.TUI.Models;

public sealed class TreeModel<T>
{
    private readonly List<TreeNode<T>> _roots = [];
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);

    public IReadOnlyList<TreeNode<T>> Roots => _roots;

    public ViewportModel Viewport { get; } = new();

    public string? SelectedKey { get; set; }

    public bool LeafOnlySelection { get; set; }

    public TreeModel<T> AddRoot(TreeNode<T> node)
    {
        _roots.Add(node ?? throw new ArgumentNullException(nameof(node)));
        _expanded.Add(node.Key);
        return this;
    }

    public bool IsExpanded(string key) => _expanded.Contains(key);

    public void Expand(string key) => _expanded.Add(key);

    public void Collapse(string key) => _expanded.Remove(key);

    public void Toggle(string key)
    {
        if (!_expanded.Remove(key))
        {
            _expanded.Add(key);
        }
    }

    public TreeNode<T>? Find(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        foreach (var root in _roots)
        {
            var node = Find(root, key);
            if (node is not null)
            {
                return node;
            }
        }

        return null;
    }

    private static TreeNode<T>? Find(TreeNode<T> node, string key)
    {
        if (node.Key == key)
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var found = Find(child, key);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}

public sealed class TreeNode<T>
{
    private readonly List<TreeNode<T>> _children = [];

    public TreeNode(string key, T value, string label)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Value = value;
        Label = label ?? throw new ArgumentNullException(nameof(label));
    }

    public string Key { get; }

    public T Value { get; }

    public string Label { get; }

    public IReadOnlyList<TreeNode<T>> Children => _children;

    public bool HasChildren => _children.Count > 0;

    public bool IsSelectable { get; init; } = true;

    public TreeNode<T> Add(TreeNode<T> child)
    {
        _children.Add(child ?? throw new ArgumentNullException(nameof(child)));
        return this;
    }
}
