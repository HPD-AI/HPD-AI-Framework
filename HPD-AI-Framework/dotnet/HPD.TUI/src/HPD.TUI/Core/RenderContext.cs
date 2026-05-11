namespace HPD.TUI.Core;

public readonly record struct RenderContext
{
    public RenderContext(
        int width,
        int height,
        Theme theme,
        ColorSystem colorSystem = ColorSystem.TrueColor,
        TimeSpan elapsed = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
        Theme = theme ?? throw new ArgumentNullException(nameof(theme));
        ColorSystem = colorSystem;
        Elapsed = elapsed;
    }

    public int Width { get; }

    public int Height { get; }

    public Theme Theme { get; }

    public ColorSystem ColorSystem { get; }

    public TimeSpan Elapsed { get; }
}

public enum ColorSystem
{
    Legacy = 0,
    Ansi256 = 1,
    TrueColor = 2
}
