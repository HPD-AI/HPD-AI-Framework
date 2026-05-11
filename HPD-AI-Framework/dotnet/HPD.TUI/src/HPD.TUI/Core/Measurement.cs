namespace HPD.TUI.Core;

public readonly record struct Measurement
{
    public Measurement(int minWidth, int maxWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minWidth);

        if (maxWidth < minWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWidth), "Max width must be greater than or equal to min width.");
        }

        MinWidth = minWidth;
        MaxWidth = maxWidth;
    }

    public int MinWidth { get; }

    public int MaxWidth { get; }
}
