using HPD.TUI.Core;

namespace HPD.TUI.Rendering;

public sealed class TuiOutputOptions
{
    public int Width { get; init; } = 80;

    public int Height { get; init; } = 24;

    public bool UseAnsi { get; init; }

    public bool TrimTrailingBlankLines { get; init; } = true;

    public Theme? Theme { get; init; }

    public ColorSystem ColorSystem { get; init; } = ColorSystem.TrueColor;
}
