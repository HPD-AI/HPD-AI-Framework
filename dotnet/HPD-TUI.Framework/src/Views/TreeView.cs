using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Utilities;

namespace HPD.TUI.Views;

public sealed class TreeView<T> : IFocusable
{
    private readonly TreeModel<T> _model;
    private readonly TreeController<T> _controller;

    public TreeView(TreeModel<T> model, TreeController<T>? controller = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _controller = controller ?? new TreeController<T>(_model);
    }

    public TreeModel<T> Model => _model;

    public TreeController<T> Controller => _controller;

    public bool IsFocused { get; set; }

    public TreeViewMode Mode { get; init; } = TreeViewMode.Outline;

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var width = 0;
        foreach (var node in _controller.GetVisibleNodes())
        {
            width = Math.Max(width, Math.Min(maxWidth, (node.Depth * 2) + 2 + UnicodeWidth.GetWidth(node.Node.Label)));
        }

        return new Measurement(Math.Min(width, maxWidth), Math.Min(width, maxWidth));
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        if (Mode == TreeViewMode.Breadcrumb)
        {
            RenderBreadcrumb(in context, maxWidth, ref output);
            return;
        }

        var visible = _controller.GetVisibleNodes();
        if (Mode == TreeViewMode.Compact)
        {
            RenderCompact(visible, in context, maxWidth, ref output);
            return;
        }

        var selectedIndex = GetSelectedIndex(visible);
        _model.Viewport.SetWindowSize(Math.Min(context.Height, visible.Count), visible.Count);
        _model.Viewport.EnsureVisible(selectedIndex, visible.Count);

        var end = Math.Min(visible.Count, _model.Viewport.Offset + _model.Viewport.WindowSize);
        for (var i = _model.Viewport.Offset; i < end; i++)
        {
            var item = visible[i];
            var selected = item.Node.Key == _model.SelectedKey;
            var style = selected ? context.Theme.Accent : context.Theme.Text;

            WriteIndent(item.Depth, context.Theme.Border, ref output);
            output.Write(GetGuide(item).AsSpan(), context.Theme.Border);
            output.Write(" ", context.Theme.Border);
            output.Write(item.Node.Label.AsSpan(), style);

            if (i < end - 1)
            {
                output.WriteLineBreak();
            }
        }
    }

    public bool HandleInput(in TuiInputEvent key)
    {
        var keyEvent = key.KeyEvent;
        return _controller.HandleInput(in keyEvent);
    }

    public static TreeView<T> Create(IEnumerable<TreeNode<T>> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var model = new TreeModel<T>();
        foreach (var root in roots)
        {
            model.AddRoot(root);
        }

        return new TreeView<T>(model);
    }

    private void RenderCompact(IReadOnlyList<TreeVisibleNode<T>> visible, in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        var first = true;
        foreach (var item in visible)
        {
            if (!first)
            {
                output.Write(" / ", context.Theme.Border);
            }

            var selected = item.Node.Key == _model.SelectedKey;
            output.Write(item.Node.Label.AsSpan(0, Math.Min(item.Node.Label.Length, Math.Max(0, maxWidth - output.CursorX))), selected ? context.Theme.Accent : context.Theme.Text);
            first = false;

            if (output.CursorX >= maxWidth)
            {
                break;
            }
        }
    }

    private void RenderBreadcrumb(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        var path = _controller.GetSelectedPath();
        for (var i = 0; i < path.Count; i++)
        {
            if (i > 0)
            {
                output.Write(" > ", context.Theme.Border);
            }

            var selected = i == path.Count - 1;
            output.Write(path[i].Label.AsSpan(0, Math.Min(path[i].Label.Length, Math.Max(0, maxWidth - output.CursorX))), selected ? context.Theme.Accent : context.Theme.Text);
        }
    }

    private string GetGuide(TreeVisibleNode<T> item)
    {
        if (!item.Node.HasChildren)
        {
            return "•";
        }

        return _model.IsExpanded(item.Node.Key) ? "▾" : "▸";
    }

    private static void WriteIndent(int depth, Style style, ref SegmentWriter output)
    {
        for (var i = 0; i < depth; i++)
        {
            output.Write("  ", style);
        }
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

        return 0;
    }
}

public enum TreeViewMode
{
    Outline,
    Compact,
    Breadcrumb
}
