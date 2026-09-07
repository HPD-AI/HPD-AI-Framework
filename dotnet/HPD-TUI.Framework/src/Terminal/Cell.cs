using HPD.TUI.Core;

namespace HPD.TUI.Terminal;

/// <summary>Describes one terminal cell backed by its owning grid's grapheme arena.</summary>
public readonly record struct Cell(
    int GraphemeOffset,
    ushort GraphemeLength,
    byte DisplayWidth,
    Style Style,
    TerminalHyperlinkId HyperlinkId,
    bool IsContinuation = false)
{
    /// <summary>Gets an empty cell descriptor.</summary>
    public static Cell Blank { get; } = new(-1, 0, 1, Style.Default, TerminalHyperlinkId.None);

    /// <summary>Gets whether this cell contains the default blank glyph.</summary>
    public bool IsBlank => !IsContinuation && GraphemeLength == 0 && Style == Style.Default && HyperlinkId.IsNone;
}
