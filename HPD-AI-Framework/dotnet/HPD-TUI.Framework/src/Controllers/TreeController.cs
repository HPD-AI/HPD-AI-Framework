using HPD.TUI.Core;
using HPD.TUI.Models;

namespace HPD.TUI.Controllers;

public sealed class TreeController<T>
{
    private readonly TreeModel<T> _model;

    public TreeController(TreeModel<T> model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        EnsureSelection();
    }

    public Action<TreeNode<T>>? Submitted { get; set; }

    public Action? Canceled { get; set; }

    public bool HandleInput(in KeyEvent key)
    {
        switch (key.Key)
        {
            case KeyCode.UpArrow:
                Move(-1);
                return true;
            case KeyCode.DownArrow:
                Move(1);
                return true;
            case KeyCode.PageUp:
                Page(-1);
                return true;
            case KeyCode.PageDown:
                Page(1);
                return true;
            case KeyCode.Home:
                MoveFirst();
                return true;
            case KeyCode.End:
                MoveLast();
                return true;
            case KeyCode.LeftArrow:
                CollapseOrMoveToParent();
                return true;
            case KeyCode.RightArrow:
                ExpandOrMoveToChild();
                return true;
            case KeyCode.Enter:
                Submit();
                return true;
            case KeyCode.Escape:
                Canceled?.Invoke();
                return true;
            default:
                return false;
        }
    }

    public IReadOnlyList<TreeVisibleNode<T>> GetVisibleNodes()
    {
        var visible = new List<TreeVisibleNode<T>>();
        foreach (var root in _model.Roots)
        {
            AddVisible(root, parent: null, depth: 0, visible);
        }

        return visible;
    }

    public IReadOnlyList<TreeNode<T>> GetSelectedPath()
    {
        if (string.IsNullOrEmpty(_model.SelectedKey))
        {
            return [];
        }

        foreach (var root in _model.Roots)
        {
            var path = new List<TreeNode<T>>();
            if (TryBuildPath(root, _model.SelectedKey, path))
            {
                return path;
            }
        }

        return [];
    }

    public void Move(int delta)
    {
        var visible = GetVisibleNodes();
        if (visible.Count == 0 || delta == 0)
        {
            return;
        }

        var index = GetSelectedIndex(visible);
        for (var attempts = 0; attempts < visible.Count; attempts++)
        {
            index = Math.Clamp(index + delta, 0, visible.Count - 1);
            if (CanSelect(visible[index].Node))
            {
                _model.SelectedKey = visible[index].Node.Key;
                EnsureSelectedVisible(visible);
                return;
            }

            if (index == 0 || index == visible.Count - 1)
            {
                return;
            }
        }
    }

    public void EnsureSelection()
    {
        if (!string.IsNullOrEmpty(_model.SelectedKey) && _model.Find(_model.SelectedKey) is { } selected && CanSelect(selected))
        {
            return;
        }

        foreach (var item in GetVisibleNodes())
        {
            if (CanSelect(item.Node))
            {
                _model.SelectedKey = item.Node.Key;
                EnsureSelectedVisible();
                return;
            }
        }
    }

    public void Page(int direction)
    {
        var visible = GetVisibleNodes();
        if (visible.Count == 0 || direction == 0)
        {
            return;
        }

        var rows = _model.Viewport.WindowSize > 0 ? _model.Viewport.WindowSize : 10;
        var index = Math.Max(0, GetSelectedIndex(visible));
        index = Math.Clamp(index + (Math.Sign(direction) * rows), 0, visible.Count - 1);

        for (var attempts = 0; attempts < visible.Count; attempts++)
        {
            if (CanSelect(visible[index].Node))
            {
                _model.SelectedKey = visible[index].Node.Key;
                EnsureSelectedVisible(visible);
                return;
            }

            index = Math.Clamp(index + Math.Sign(direction), 0, visible.Count - 1);
        }
    }

    public void MoveFirst()
    {
        foreach (var item in GetVisibleNodes())
        {
            if (CanSelect(item.Node))
            {
                _model.SelectedKey = item.Node.Key;
                EnsureSelectedVisible();
                return;
            }
        }
    }

    public void MoveLast()
    {
        var visible = GetVisibleNodes();
        for (var i = visible.Count - 1; i >= 0; i--)
        {
            if (CanSelect(visible[i].Node))
            {
                _model.SelectedKey = visible[i].Node.Key;
                EnsureSelectedVisible(visible);
                return;
            }
        }
    }

    private void Submit()
    {
        EnsureSelection();
        if (!string.IsNullOrEmpty(_model.SelectedKey) && _model.Find(_model.SelectedKey) is { } selected && CanSelect(selected))
        {
            Submitted?.Invoke(selected);
        }
    }

    private void CollapseOrMoveToParent()
    {
        EnsureSelection();
        var visible = GetVisibleNodes();
        var index = GetSelectedIndex(visible);
        if (index < 0)
        {
            return;
        }

        var item = visible[index];
        if (item.Node.HasChildren && _model.IsExpanded(item.Node.Key))
        {
            _model.Collapse(item.Node.Key);
            return;
        }

        if (item.ParentKey is not null && _model.Find(item.ParentKey) is { } parent && CanSelect(parent))
        {
                _model.SelectedKey = parent.Key;
                EnsureSelectedVisible();
        }
    }

    private void ExpandOrMoveToChild()
    {
        EnsureSelection();
        var selected = string.IsNullOrEmpty(_model.SelectedKey) ? null : _model.Find(_model.SelectedKey);
        if (selected is null || !selected.HasChildren)
        {
            return;
        }

        if (!_model.IsExpanded(selected.Key))
        {
            _model.Expand(selected.Key);
            return;
        }

        foreach (var child in selected.Children)
        {
            if (CanSelect(child))
            {
                _model.SelectedKey = child.Key;
                EnsureSelectedVisible();
                return;
            }
        }
    }

    private void EnsureSelectedVisible(IReadOnlyList<TreeVisibleNode<T>>? visible = null)
    {
        visible ??= GetVisibleNodes();
        var index = GetSelectedIndex(visible);
        if (index >= 0)
        {
            _model.Viewport.EnsureVisible(index, visible.Count);
        }
    }

    private bool CanSelect(TreeNode<T> node)
    {
        return node.IsSelectable && (!_model.LeafOnlySelection || !node.HasChildren);
    }

    private int GetSelectedIndex(IReadOnlyList<TreeVisibleNode<T>> visible)
    {
        for (var i = 0; i < visible.Count; i++)
        {
            if (visible[i].Node.Key == _model.SelectedKey)
            {
                return i;
            }
        }

        return -1;
    }

    private void AddVisible(TreeNode<T> node, string? parent, int depth, List<TreeVisibleNode<T>> visible)
    {
        visible.Add(new TreeVisibleNode<T>(node, depth, parent));
        if (!node.HasChildren || !_model.IsExpanded(node.Key))
        {
            return;
        }

        foreach (var child in node.Children)
        {
            AddVisible(child, node.Key, depth + 1, visible);
        }
    }

    private static bool TryBuildPath(TreeNode<T> node, string key, List<TreeNode<T>> path)
    {
        path.Add(node);
        if (node.Key == key)
        {
            return true;
        }

        foreach (var child in node.Children)
        {
            if (TryBuildPath(child, key, path))
            {
                return true;
            }
        }

        path.RemoveAt(path.Count - 1);
        return false;
    }
}

public readonly record struct TreeVisibleNode<T>(TreeNode<T> Node, int Depth, string? ParentKey);
