using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Models;

namespace HPD.TUI.Views;

public sealed class SelectionView<T> : IFocusable
{
    private readonly SelectionModel<T> _model;
    private readonly SelectionController<T> _controller;
    private readonly CollectionListView<T> _list;

    public SelectionView(SelectionModel<T> model, SelectionController<T> controller)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _list = new CollectionListView<T>(
            _model,
            _controller.Navigation,
            CollectionListMode.SingleSelect,
            handleInput: key => _controller.HandleInput(in key));
    }

    public SelectionModel<T> Model => _model;

    public SelectionController<T> Controller => _controller;

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

    public static SelectionView<T> Create(IEnumerable<T> values, Func<T, string> titleSelector)
    {
        var model = SelectionModel<T>.From(values, titleSelector);
        return new SelectionView<T>(model, new SelectionController<T>(model));
    }

    public static SelectionView<T> Create(IEnumerable<T> values, Func<T, string> titleSelector, Action<T>? submitted)
    {
        var model = SelectionModel<T>.From(values, titleSelector);
        var controller = new SelectionController<T>(model);
        if (submitted is not null)
        {
            controller.Submitted = item => submitted(item.Value);
        }

        return new SelectionView<T>(model, controller);
    }
}
