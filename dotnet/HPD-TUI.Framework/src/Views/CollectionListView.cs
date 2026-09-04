using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Utilities;

namespace HPD.TUI.Views;

public sealed class CollectionListView<T> : Component, IFocusable
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

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
    {
        var maxWidth = constraints.MaxWidth;
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
        var height = _model.VisibleCount == 0
            ? 1
            : ResolveWindowSize(context.Height, _model.VisibleCount);

        return new Measurement(Math.Min(width, maxWidth), width, height);
    }

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
    {
        var maxWidth = output.MaxWidth;
        if (_model.VisibleCount == 0)
        {
            output.Write(_model.EmptyText.AsSpan(), context.Theme.Border);
            return;
        }

        var visibleCount = _model.VisibleCount;
        var activeVisibleIndex = _model.GetVisibleIndex(_navigation.ActiveIndex);
        _navigation.Viewport.SetWindowSize(ResolveWindowSize(context.Height, visibleCount), visibleCount);
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

    public override bool HandleInput(in TuiInputEvent key)
    {
        return _handleInput?.Invoke(key.KeyEvent) == true;
    }

    private void RenderItem(in RenderContext context, ref DisplayListBuilder output, int sourceIndex, CollectionItem<T> item)
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

    private int ResolveWindowSize(int availableHeight, int visibleCount)
    {
        var windowSize = Math.Min(Math.Max(1, availableHeight), visibleCount);
        if (_model.MaxVisibleItems is { } maxVisibleItems && maxVisibleItems > 0)
        {
            windowSize = Math.Min(windowSize, maxVisibleItems);
        }

        return Math.Max(1, windowSize);
    }
}

public enum CollectionListMode
{
    Plain,
    SingleSelect,
    Checklist,
    Compact
}
