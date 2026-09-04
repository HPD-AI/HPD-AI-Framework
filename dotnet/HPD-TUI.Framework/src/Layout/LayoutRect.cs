namespace HPD.TUI.Layout;

/// <summary>Describes a non-negative rectangular region in terminal-cell coordinates.</summary>
public readonly record struct LayoutRect
{
    /// <summary>Creates a rectangular region.</summary>
    public LayoutRect(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>Gets the left column.</summary>
    public int X { get; }

    /// <summary>Gets the top row.</summary>
    public int Y { get; }

    /// <summary>Gets the width in terminal columns.</summary>
    public int Width { get; }

    /// <summary>Gets the height in terminal rows.</summary>
    public int Height { get; }

    /// <summary>Gets the exclusive right column.</summary>
    public int Right => X + Width;

    /// <summary>Gets the exclusive bottom row.</summary>
    public int Bottom => Y + Height;

    /// <summary>Gets whether the rectangle has no area.</summary>
    public bool IsEmpty => Width == 0 || Height == 0;

    /// <summary>Returns the region remaining after applying the supplied edge thickness.</summary>
    public LayoutRect Inset(Thickness thickness)
    {
        var width = Math.Max(0, Width - thickness.Horizontal);
        var height = Math.Max(0, Height - thickness.Vertical);
        return new LayoutRect(X + thickness.Left, Y + thickness.Top, width, height);
    }
}
