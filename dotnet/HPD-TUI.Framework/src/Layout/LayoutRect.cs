namespace HPD.TUI.Layout;

public readonly record struct LayoutRect
{
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

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }

    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsEmpty => Width == 0 || Height == 0;

    public LayoutRect Inset(Thickness thickness)
    {
        var width = Math.Max(0, Width - thickness.Horizontal);
        var height = Math.Max(0, Height - thickness.Vertical);
        return new LayoutRect(X + thickness.Left, Y + thickness.Top, width, height);
    }
}
