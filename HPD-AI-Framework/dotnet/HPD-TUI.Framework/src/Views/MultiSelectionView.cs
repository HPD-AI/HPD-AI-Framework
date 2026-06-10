using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Models;

namespace HPD.TUI.Views;

public sealed class MultiSelectionView<T> : IFocusable
{
    private readonly MultiSelectionModel<T> _model;
    private readonly MultiSelectionController<T> _controller;
    private readonly CollectionListView<T> _list;

    public MultiSelectionView(MultiSelectionModel<T> model, MultiSelectionController<T> controller)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _list = new CollectionListView<T>(
            _model,
            _controller.Navigation,
            CollectionListMode.Checklist,
            item => _model.IsSelected(item.Key),
            key => _controller.HandleInput(in key));
    }

    public MultiSelectionModel<T> Model => _model;

    public MultiSelectionController<T> Controller => _controller;

    public CollectionListView<T> List => _list;

    public bool IsFocused
    {
        get => _list.IsFocused;
        set => _list.IsFocused = value;
    }

    public Measurement Measure(in RenderContext context, int maxWidth) => _list.Measure(in context, maxWidth);

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output) => _list.Render(in context, maxWidth, ref output);

    public void HandleInput(in KeyEvent key) => _list.HandleInput(in key);

    public void Invalidate() => _list.Invalidate();
}
