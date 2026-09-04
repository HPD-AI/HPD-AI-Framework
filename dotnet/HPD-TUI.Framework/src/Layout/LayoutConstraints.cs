namespace HPD.TUI.Layout;

/// <summary>Defines the minimum and maximum size available during component measurement.</summary>
public readonly record struct LayoutConstraints
{
    /// <summary>Creates validated two-dimensional layout constraints.</summary>
    /// <param name="minWidth">Minimum permitted width.</param>
    /// <param name="maxWidth">Maximum permitted width.</param>
    /// <param name="minHeight">Minimum permitted height.</param>
    /// <param name="maxHeight">Maximum permitted height.</param>
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

    /// <summary>Gets the minimum permitted width.</summary>
    public int MinWidth { get; }

    /// <summary>Gets the maximum permitted width.</summary>
    public int MaxWidth { get; }

    /// <summary>Gets the minimum permitted height.</summary>
    public int MinHeight { get; }

    /// <summary>Gets the maximum permitted height.</summary>
    public int MaxHeight { get; }

    /// <summary>Creates constraints fixed to one exact size.</summary>
    public static LayoutConstraints Tight(int width, int height) => new(width, width, height, height);

    /// <summary>Creates constraints with zero minima and the supplied maxima.</summary>
    public static LayoutConstraints Loose(int maxWidth, int maxHeight) => new(0, maxWidth, 0, maxHeight);

    /// <summary>Clamps a width to this constraint interval.</summary>
    public int ClampWidth(int width) => Math.Clamp(width, MinWidth, MaxWidth);

    /// <summary>Clamps a height to this constraint interval.</summary>
    public int ClampHeight(int height) => Math.Clamp(height, MinHeight, MaxHeight);
}
