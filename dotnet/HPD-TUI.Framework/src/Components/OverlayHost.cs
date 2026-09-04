using HPD.TUI.Core;

namespace HPD.TUI.Components;

public sealed class OverlayHost : Component
{
    private readonly IComponent _content;
    private readonly List<Overlay> _overlays = [];

    public OverlayHost(IComponent content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        AdoptChild(_content);
    }

    public IReadOnlyList<Overlay> Overlays => _overlays;

    public void Push(Overlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        AdoptChild(overlay);
        _overlays.Add(overlay);
        InvalidatePaint();
    }

    public bool Pop()
    {
        if (_overlays.Count == 0)
        {
            return false;
        }

        var overlay = _overlays[^1];
        ReleaseChild(overlay);
        _overlays.RemoveAt(_overlays.Count - 1);
        InvalidatePaint();
        return true;
    }

    public void ClearOverlays()
    {
        if (_overlays.Count == 0) return;
        foreach (var overlay in _overlays.ToArray()) ReleaseChild(overlay);
        _overlays.Clear();
        InvalidatePaint();
    }

    public override Measurement Measure(in RenderContext context, int maxWidth)
    {
        return MeasureChild(_content, in context, maxWidth);
    }

    public override void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        _content.Render(in context, maxWidth, ref output);

        foreach (var overlay in _overlays)
        {
            overlay.Render(in context, maxWidth, ref output);
        }
    }

    public override bool HandleInput(in TuiInputEvent key)
    {
        if (_overlays.Count > 0)
        {
            return _overlays[^1].HandleInput(in key);
        }

        return _content.HandleInput(in key);
    }
}
