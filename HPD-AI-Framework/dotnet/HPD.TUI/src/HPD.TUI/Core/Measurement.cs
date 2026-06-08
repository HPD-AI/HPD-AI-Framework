namespace HPD.TUI.Core;

public readonly record struct Measurement
{
    public Measurement(int minWidth, int maxWidth, int height = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        if (maxWidth < minWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWidth), "Max width must be greater than or equal to min width.");
        }

        MinWidth = minWidth;
        MaxWidth = maxWidth;
        Height = height;
    }

    public int MinWidth { get; }

    public int MaxWidth { get; }

    public int Height { get; }
}
