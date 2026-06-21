namespace HPD.TUI.Layout;

public readonly record struct BorderGlyphs(
    char TopLeft,
    char Top,
    char TopRight,
    char Right,
    char BottomRight,
    char Bottom,
    char BottomLeft,
    char Left)
{
    public static BorderGlyphs None { get; } = new(' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ');

    public static BorderGlyphs Square { get; } = new('┌', '─', '┐', '│', '┘', '─', '└', '│');

    public static BorderGlyphs Rounded { get; } = new('╭', '─', '╮', '│', '╯', '─', '╰', '│');

    public static BorderGlyphs Ascii { get; } = new('+', '-', '+', '|', '+', '-', '+', '|');
}
