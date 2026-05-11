using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.TUI.Controllers;

public sealed class DialogHost : IComponent
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

    public void Push(Overlay overlay, IComponent? focus = null, Action? onClose = null)
    {
        ArgumentNullException.ThrowIfNull(overlay);

        _layers.Add(new DialogLayer(overlay, focus, onClose));
        _focus.PushFocus(focus ?? overlay);
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

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        return _content.Measure(in context, maxWidth);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        _content.Render(in context, maxWidth, ref output);

        foreach (var layer in _layers)
        {
            layer.Overlay.Render(in context, maxWidth, ref output);
        }
    }

    public void HandleInput(in KeyEvent key)
    {
        if (_layers.Count > 0)
        {
            if (key.Key == KeyCode.Escape)
            {
                Pop();
                return;
            }

            _focus.HandleInput(in key);
            return;
        }

        _content.HandleInput(in key);
    }

    public void Invalidate()
    {
        _content.Invalidate();
        foreach (var layer in _layers)
        {
            layer.Overlay.Invalidate();
        }
    }
}

public sealed record DialogLayer(Overlay Overlay, IComponent? Focus, Action? OnClose);
