using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.TUI.Controllers;

public sealed class DialogHost : Component
{
    private readonly IComponent _content;
    private readonly FocusManager _focus;
    private readonly List<DialogLayer> _layers = [];

    public DialogHost(IComponent content, FocusManager focus)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _focus = focus ?? throw new ArgumentNullException(nameof(focus));
    }

    public int Count => _layers.Count;

    public bool HasOpenDialog => _layers.Count > 0;

    public IReadOnlyList<DialogLayer> Layers => _layers;

    public void Push(
        Overlay overlay,
        IComponent? focus = null,
        Action? onClose = null,
        bool focusHandlesEscape = false)
    {
        ArgumentNullException.ThrowIfNull(overlay);

        _layers.Add(new DialogLayer(overlay, focus, onClose, focusHandlesEscape));
        _focus.PushFocus(focus ?? overlay);
    }

    public void PushInline(
        IComponent component,
        IComponent? focus = null,
        Action? onClose = null,
        bool focusHandlesEscape = false)
    {
        ArgumentNullException.ThrowIfNull(component);

        _layers.Add(new DialogLayer(null, focus ?? component, onClose, focusHandlesEscape));
        _focus.PushFocus(focus ?? component);
    }

    public bool Pop()
    {
        if (_layers.Count == 0)
        {
            return false;
        }

        var layer = _layers[^1];
        _layers.RemoveAt(_layers.Count - 1);
        layer.OnClose?.Invoke();
        _focus.PopFocus();
        return true;
    }

    public void Clear()
    {
        while (Pop())
        {
        }
    }

    public override Measurement Measure(in RenderContext context, int maxWidth)
    {
        return _content.Measure(in context, maxWidth);
    }

    public override void Render(in RenderContext context, int maxWidth, ref DisplayListBuilder output)
    {
        _content.Render(in context, maxWidth, ref output);

        foreach (var layer in _layers)
        {
            layer.Overlay?.Render(in context, maxWidth, ref output);
        }
    }

    public override bool HandleInput(in TuiInputEvent key)
    {
        if (_layers.Count > 0)
        {
            var layer = _layers[^1];
            if (key.Key == KeyCode.Escape && !layer.FocusHandlesEscape)
            {
                Pop();
                return true;
            }

            if (_focus.HandleInput(in key))
            {
                return true;
            }

            return false;
        }

        return _content.HandleInput(in key);
    }
}

public sealed record DialogLayer(
    Overlay? Overlay,
    IComponent? Focus,
    Action? OnClose,
    bool FocusHandlesEscape);
