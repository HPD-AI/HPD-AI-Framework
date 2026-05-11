using HPD.TUI.Core;

namespace HPD.TUI.Components;

public sealed class Overlay : IComponent
{
    private readonly IComponent _child;

    public Overlay(IComponent child, int x, int y, int width)
    {
        _child = child ?? throw new ArgumentNullException(nameof(child));
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);

        X = x;
        Y = y;
        Width = width;
    }

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        return _child.Measure(in context, Math.Min(maxWidth, Width));
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        output.MoveTo(X, Y);
        _child.Render(in context, Math.Min(maxWidth, Width), ref output);
    }

    public void HandleInput(in KeyEvent key)
    {
        _child.HandleInput(in key);
    }

    public void Invalidate()
    {
        _child.Invalidate();
    }
}
