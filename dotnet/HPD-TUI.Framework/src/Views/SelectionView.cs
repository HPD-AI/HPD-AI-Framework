using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Utilities;

namespace HPD.TUI.Views;

public sealed class SelectionView<T> : Component, IFocusable
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

    public override Measurement Measure(in RenderContext context, int maxWidth)
    {
        var listMeasurement = _list.Measure(in context, maxWidth);
        if (!_model.AllowFilter)
        {
            return listMeasurement;
        }

        var queryWidth = UnicodeWidth.GetWidth(GetSearchText());
        return new Measurement(
            Math.Min(Math.Max(listMeasurement.MinWidth, queryWidth), maxWidth),
            Math.Min(Math.Max(listMeasurement.MaxWidth, queryWidth), maxWidth),
            Math.Min(context.Height, listMeasurement.Height + 1));
    }

    public override void Render(in RenderContext context, int maxWidth, ref DisplayListBuilder output)
    {
        if (!_model.AllowFilter)
        {
            _list.Render(in context, maxWidth, ref output);
            return;
        }

        output.Write(GetSearchText().AsSpan(), context.Theme.Border);
        if (context.Height <= 1)
        {
            return;
        }

        output.WriteLineBreak();
        var listContext = new RenderContext(
            context.Width,
            Math.Max(1, context.Height - 1),
            context.Theme,
            context.ColorSystem,
            context.Elapsed);
        _list.Render(in listContext, maxWidth, ref output);
    }

    public override bool HandleInput(in TuiInputEvent key) => _list.HandleInput(in key);

    private string GetSearchText()
        => string.IsNullOrEmpty(_model.Query)
            ? "Search:"
            : $"Search: {_model.Query}";

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
