namespace HPD.TUI.Layout;

public readonly record struct Thickness
{
    public Thickness(int all)
        : this(all, all, all, all)
    {
    }

    public Thickness(int vertical, int horizontal)
        : this(vertical, horizontal, vertical, horizontal)
    {
    }

    public Thickness(int top, int right, int bottom, int left)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(top);
        ArgumentOutOfRangeException.ThrowIfNegative(right);
        ArgumentOutOfRangeException.ThrowIfNegative(bottom);
        ArgumentOutOfRangeException.ThrowIfNegative(left);

        Top = top;
        Right = right;
        Bottom = bottom;
        Left = left;
    }

    public static Thickness None => default;

    public int Top { get; }

    public int Right { get; }

    public int Bottom { get; }

    public int Left { get; }

    public int Horizontal => Left + Right;

    public int Vertical => Top + Bottom;
}
