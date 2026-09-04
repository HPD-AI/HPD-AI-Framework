using HPD.TUI.Core;

namespace HPD.TUI.Components;

public sealed class Overlay : Component
{
    private readonly IComponent _child;

    public Overlay(
        IComponent child,
        int x,
        int y,
        int width,
        int? height = null,
        OverlayVerticalPlacement verticalPlacement = OverlayVerticalPlacement.Absolute,
        bool clearBackground = false)
    {
        _child = child ?? throw new ArgumentNullException(nameof(child));
        AdoptChild(_child);
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        if (height is { } value)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
        VerticalPlacement = verticalPlacement;
        ClearBackground = clearBackground;
    }

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int? Height { get; set; }

    public OverlayVerticalPlacement VerticalPlacement { get; set; }

    public bool ClearBackground { get; set; }

    public override Measurement Measure(in RenderContext context, int maxWidth)
    {
        return MeasureChild(_child, in context, Math.Min(maxWidth, Width));
    }

    public override void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        var width = Math.Min(maxWidth, Width);
        var height = Math.Clamp(Height ?? context.Height, 1, context.Height);
        var y = ResolveY(context.Height, height);
        var childContext = new RenderContext(
            Math.Max(1, Math.Min(context.Width, width)),
            height,
            context.Theme,
            context.ColorSystem,
            context.Elapsed);

        if (ClearBackground)
        {
            ClearRectangle(in context, width, height, y, ref output);
        }

        output.MoveTo(X, y);
        _child.Render(in childContext, width, ref output);
    }

    public override bool HandleInput(in TuiInputEvent key)
    {
        return _child.HandleInput(in key);
    }

    private int ResolveY(
        int contextHeight,
        int height)
        => VerticalPlacement switch
        {
            OverlayVerticalPlacement.Bottom => Math.Max(0, contextHeight - height - Y),
            _ => Y
        };

    private void ClearRectangle(
        in RenderContext context,
        int width,
        int height,
        int y,
        ref SegmentWriter output)
    {
        if (X >= context.Width || y >= context.Height)
        {
            return;
        }

        var clearWidth = Math.Max(0, Math.Min(width, context.Width - X));
        var clearHeight = Math.Max(0, Math.Min(height, context.Height - y));
        if (clearWidth == 0 || clearHeight == 0)
        {
            return;
        }

        for (var row = 0; row < clearHeight; row++)
        {
            output.MoveTo(X, y + row);
            output.WriteRepeated(' ', clearWidth, context.Theme.Text);
        }
    }
}

public enum OverlayVerticalPlacement
{
    Absolute,
    Bottom
}
