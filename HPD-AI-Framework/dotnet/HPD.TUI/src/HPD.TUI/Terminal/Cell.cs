using HPD.TUI.Core;

namespace HPD.TUI.Terminal;

public readonly record struct Cell(Rune Rune, Style Style, bool IsContinuation = false)
{
    public static Cell Blank { get; } = new(new Rune(' '), Style.Default);
}
