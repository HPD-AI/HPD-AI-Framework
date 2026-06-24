using HPD.TUI.Core;

namespace HPD.TUI.Components;

public sealed class OverlayHost : IComponent
{
    private readonly IComponent _content;
    private readonly List<Overlay> _overlays = [];

    public OverlayHost(IComponent content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public IReadOnlyList<Overlay> Overlays => _overlays;

    public void Push(Overlay overlay)
    {
        _overlays.Add(overlay ?? throw new ArgumentNullException(nameof(overlay)));
    }

    public bool Pop()
    {
        if (_overlays.Count == 0)
        {
            return false;
        }

        _overlays.RemoveAt(_overlays.Count - 1);
        return true;
    }

    public void ClearOverlays() => _overlays.Clear();

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        return _content.Measure(in context, maxWidth);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        _content.Render(in context, maxWidth, ref output);

        foreach (var overlay in _overlays)
        {
            overlay.Render(in context, maxWidth, ref output);
        }
    }

    public bool HandleInput(in TuiInputEvent key)
    {
        if (_overlays.Count > 0)
        {
            return _overlays[^1].HandleInput(in key);
        }

        return _content.HandleInput(in key);
    }
}
