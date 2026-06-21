using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Utilities;

namespace HPD.TUI.Views;

public sealed class CollectionListView<T> : IFocusable
{
    private readonly CollectionModel<T> _model;
    private readonly CollectionNavigationController<T> _navigation;
    private readonly Func<CollectionItem<T>, bool>? _isChecked;
    private readonly Func<KeyEvent, bool>? _handleInput;

    public CollectionListView(
        CollectionModel<T> model,
        CollectionNavigationController<T> navigation,
        CollectionListMode mode = CollectionListMode.SingleSelect,
        Func<CollectionItem<T>, bool>? isChecked = null,
        Func<KeyEvent, bool>? handleInput = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        Mode = mode;
        _isChecked = isChecked;
        _handleInput = handleInput;
    }

    public CollectionModel<T> Model => _model;

    public CollectionNavigationController<T> Navigation => _navigation;

    public CollectionListMode Mode { get; set; }

    public bool ShowCategories { get; set; }

    public bool IsFocused { get; set; }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var width = 0;
        for (var i = 0; i < _model.Items.Count; i++)
        {
            if (!_model.IsVisible(i))
            {
                continue;
            }

            var item = _model.Items[i];
            var itemWidth = UnicodeWidth.GetWidth(item.Title) + GetPrefixWidth();
            if (!string.IsNullOrEmpty(item.Description))
            {
                itemWidth += UnicodeWidth.GetWidth(item.Description) + 1;
            }

            width = Math.Max(width, itemWidth);
        }

        if (width == 0)
        {
            width = UnicodeWidth.GetWidth(_model.EmptyText);
        }

        width = Math.Min(width, maxWidth);
        return new Measurement(Math.Min(width, maxWidth), width);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        if (_model.VisibleCount == 0)
        {
            output.Write(_model.EmptyText.AsSpan(), context.Theme.Border);
            return;
        }

        var visibleCount = _model.VisibleCount;
        var activeVisibleIndex = _model.GetVisibleIndex(_navigation.ActiveIndex);
        _navigation.Viewport.SetWindowSize(Math.Min(context.Height, visibleCount), visibleCount);
        _navigation.Viewport.EnsureVisible(activeVisibleIndex, visibleCount);

        var end = Math.Min(visibleCount, _navigation.Viewport.Offset + _navigation.Viewport.WindowSize);
        string? previousCategory = null;
        for (var visibleIndex = _navigation.Viewport.Offset; visibleIndex < end; visibleIndex++)
        {
            var sourceIndex = _model.GetSourceIndexAtVisibleIndex(visibleIndex);
            if (sourceIndex < 0)
            {
                continue;
            }

            var item = _model.Items[sourceIndex];
            if (ShowCategories && !string.IsNullOrEmpty(item.Category) && !StringComparer.Ordinal.Equals(previousCategory, item.Category))
            {
                if (visibleIndex > _navigation.Viewport.Offset)
                {
                    output.WriteLineBreak();
                }

                output.Write(item.Category.AsSpan(), context.Theme.Border);
                output.WriteLineBreak();
                previousCategory = item.Category;
            }

            RenderItem(in context, ref output, sourceIndex, item);

            if (visibleIndex < end - 1)
            {
                output.WriteLineBreak();
            }
        }
    }

    public void HandleInput(in KeyEvent key)
    {
        _handleInput?.Invoke(key);
    }

    public void Invalidate()
    {
    }

    private void RenderItem(in RenderContext context, ref SegmentWriter output, int sourceIndex, CollectionItem<T> item)
    {
        var active = sourceIndex == _navigation.ActiveIndex;
        var style = item.Disabled
            ? context.Theme.Border
            : active
                ? context.Theme.Accent
                : item.Style ?? context.Theme.Text;

        if (Mode is CollectionListMode.SingleSelect or CollectionListMode.Checklist)
        {
            output.Write(active ? "> " : "  ", style);
        }

        if (Mode == CollectionListMode.Checklist)
        {
            output.Write(_isChecked?.Invoke(item) == true ? "[x] " : "[ ] ", style);
        }

        output.Write(item.Title.AsSpan(), style);

        if (!string.IsNullOrEmpty(item.Description))
        {
            output.Write(" ", context.Theme.Border);
            output.Write(item.Description.AsSpan(), context.Theme.Border);
        }
    }

    private int GetPrefixWidth()
    {
        return Mode switch
        {
            CollectionListMode.SingleSelect => 2,
            CollectionListMode.Checklist => 6,
            _ => 0
        };
    }
}

public enum CollectionListMode
{
    Plain,
    SingleSelect,
    Checklist,
    Compact
}
