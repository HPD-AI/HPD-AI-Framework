using HPD.TUI.Core;

namespace HPD.TUI.Components;

public sealed class Overlay : Component
{
    public override ComponentDependencies Dependencies => ComponentDependencies.Static;
    private readonly IComponent _child;
    private int _x;
    private int _y;
    private int _width;
    private int? _height;
    private OverlayVerticalPlacement _verticalPlacement;
    private bool _clearBackground;

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

        _x = x;
        _y = y;
        _width = width;
        _height = height;
        _verticalPlacement = verticalPlacement;
        _clearBackground = clearBackground;
    }

    /// <summary>Gets or sets the horizontal placement.</summary>
    public int X { get => _x; set { ArgumentOutOfRangeException.ThrowIfNegative(value); SetLayout(ref _x, value); } }

    /// <summary>Gets or sets the vertical placement offset.</summary>
    public int Y { get => _y; set { ArgumentOutOfRangeException.ThrowIfNegative(value); SetLayout(ref _y, value); } }

    /// <summary>Gets or sets the overlay width.</summary>
    public int Width { get => _width; set { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value); SetLayout(ref _width, value); } }

    /// <summary>Gets or sets the optional overlay height.</summary>
    public int? Height { get => _height; set { if (value is { } height) ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height); SetLayout(ref _height, value); } }

    /// <summary>Gets or sets how the vertical offset is interpreted.</summary>
    public OverlayVerticalPlacement VerticalPlacement { get => _verticalPlacement; set => SetLayout(ref _verticalPlacement, value); }

    /// <summary>Gets or sets whether the overlay clears its rectangle before painting.</summary>
    public bool ClearBackground { get => _clearBackground; set => SetPaint(ref _clearBackground, value); }

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
    {
        var maxWidth = constraints.MaxWidth;
        return MeasureChild(_child, in context, Math.Min(maxWidth, Width));
    }

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
    {
        var maxWidth = output.MaxWidth;
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
        output.Render(_child, in childContext, width);
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
        ref DisplayListBuilder output)
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
