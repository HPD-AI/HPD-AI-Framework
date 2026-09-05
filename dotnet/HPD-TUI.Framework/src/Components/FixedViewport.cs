using HPD.TUI.Core;
using HPD.TUI.Layout;

namespace HPD.TUI.Components;

/// <summary>Owns one child inside an exact-height layout region.</summary>
public sealed class FixedViewport : Component
{
    private int _height;

    /// <summary>Creates a fixed viewport around <paramref name="content"/>.</summary>
    public FixedViewport(IComponent content, int height)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Content = content;
        _height = height;
        AdoptChild(content);
    }

    /// <summary>Gets the component rendered with this region's height constraint.</summary>
    public IComponent Content { get; }

    /// <summary>Gets or sets the exact number of owned rows.</summary>
    public int Height
    {
        get => _height;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            SetLayout(ref _height, value);
        }
    }

    /// <inheritdoc />
    public override Measurement Measure(in RenderContext context, LayoutConstraints constraints)
    {
        var measured = Content.Measure(in context,
            new LayoutConstraints(constraints.MinWidth, constraints.MaxWidth, 0, Height));
        return new(constraints.ClampWidth(measured.MinWidth),
            constraints.ClampWidth(measured.MaxWidth), constraints.ClampHeight(Height));
    }

    /// <inheritdoc />
    public override void Render(in RenderContext context, ref DisplayListBuilder output)
    {
        var width = Math.Min(output.MaxWidth, context.Width);
        var height = Math.Min(Height, Math.Max(1, context.Height - output.CursorY));
        var originX = output.CursorX;
        var originY = output.CursorY;
        var childContext = new RenderContext(width, height, context.Theme, context.ColorSystem,
            context.Elapsed, context.Capabilities);
        output.Render(Content, in childContext, width);
        output.MoveTo(originX, originY + height - 1);
    }
}
