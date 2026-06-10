namespace HPD.TUI.Layout;

public readonly record struct LayoutConstraints
{
    public LayoutConstraints(int minWidth, int maxWidth, int minHeight, int maxHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(maxWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(minHeight);
        ArgumentOutOfRangeException.ThrowIfNegative(maxHeight);
        if (maxWidth < minWidth)
        {
            throw new ArgumentException("MaxWidth must be greater than or equal to MinWidth.", nameof(maxWidth));
        }

        if (maxHeight < minHeight)
        {
            throw new ArgumentException("MaxHeight must be greater than or equal to MinHeight.", nameof(maxHeight));
        }

        MinWidth = minWidth;
        MaxWidth = maxWidth;
        MinHeight = minHeight;
        MaxHeight = maxHeight;
    }

    public int MinWidth { get; }

    public int MaxWidth { get; }

    public int MinHeight { get; }

    public int MaxHeight { get; }

    public static LayoutConstraints Tight(int width, int height) => new(width, width, height, height);

    public static LayoutConstraints Loose(int maxWidth, int maxHeight) => new(0, maxWidth, 0, maxHeight);

    public int ClampWidth(int width) => Math.Clamp(width, MinWidth, MaxWidth);

    public int ClampHeight(int height) => Math.Clamp(height, MinHeight, MaxHeight);
}
