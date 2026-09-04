using HPD.TUI.Core;
using HPD.TUI.Terminal;

namespace HPD.TUI.Rendering;

internal static class AnsiGridRenderer
{
    private static readonly char[] ResetSequence = ['\x1b', '[', '0', 'm'];
    private static readonly char[] HyperlinkOpen = ['\x1b', ']', '8', ';', ';'];
    private static readonly char[] HyperlinkClose = ['\x1b', ']', '8', ';', ';', '\x1b', '\\'];
    private static readonly char[] StringTerminator = ['\x1b', '\\'];

    public static void WriteFull(TerminalGrid grid, AnsiFrameWriter output)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(output);

        Style? currentStyle = null;
        Span<char> styleBuffer = stackalloc char[64];
        TerminalHyperlink? currentHyperlink = null;

        for (var y = 0; y < grid.Height; y++)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                var cell = grid.GetCell(x, y);
                if (cell.IsContinuation)
                {
                    continue;
                }

                WriteStyleTransition(cell.Style, ref currentStyle, styleBuffer, output);
                WriteHyperlinkTransition(grid.GetHyperlink(cell), ref currentHyperlink, output);
                output.Write(grid.GetGrapheme(cell));
            }

            if (currentStyle is not null)
            {
                output.Write(ResetSequence);
                currentStyle = null;
            }

            WriteHyperlinkTransition(null, ref currentHyperlink, output);

            if (y < grid.Height - 1)
            {
                output.Write("\r\n");
            }
        }
    }

    public static void WriteDifferential(TerminalGrid previous, TerminalGrid current, AnsiFrameWriter output)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(output);

        if (previous.Width != current.Width || previous.Height != current.Height)
        {
            WriteFull(current, output);
            return;
        }

        Style? currentStyle = null;
        Span<char> styleBuffer = stackalloc char[64];

        for (var y = 0; y < current.Height; y++)
        {
            for (var x = 0; x < current.Width; x++)
            {
                var cell = current.GetCell(x, y);
                if (cell.IsContinuation || current.CellEquals(previous, x, y))
                {
                    continue;
                }

                WriteCursorMove(x, y, output);
                WriteStyleTransition(cell.Style, ref currentStyle, styleBuffer, output);
                TerminalHyperlink? activeHyperlink = null;
                WriteHyperlinkTransition(current.GetHyperlink(cell), ref activeHyperlink, output);
                output.Write(current.GetGrapheme(cell));
                WriteHyperlinkTransition(null, ref activeHyperlink, output);
            }
        }

        if (currentStyle is not null)
        {
            output.Write(ResetSequence);
        }
    }

    public static void WriteDifferential(ScreenBuffer previous, ScreenBuffer current, AnsiFrameWriter output)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(output);

        if (previous.Width != current.Width || previous.Height != current.Height)
        {
            WriteFull(current.Grid, output);
            return;
        }

        Style? activeStyle = null;
        Span<char> styleBuffer = stackalloc char[64];
        for (var row = 0; row < current.Height; row++)
        {
            if (current.RowEquals(previous, row))
                continue;

            var column = 0;
            while (column < current.Width)
            {
                if (current.Grid.CellEquals(previous.Grid, column, row))
                {
                    column++;
                    continue;
                }

                var start = FindLeadingColumn(previous.Grid, current.Grid, column, row);
                var end = ExpandChangedRun(previous.Grid, current.Grid, start, row);
                WriteCursorMove(start, row, output);
                TerminalHyperlink? activeHyperlink = null;
                for (var x = start; x < end; x++)
                {
                    var cell = current.Grid.GetCell(x, row);
                    if (cell.IsContinuation)
                        continue;
                    WriteStyleTransition(cell.Style, ref activeStyle, styleBuffer, output);
                    WriteHyperlinkTransition(current.Grid.GetHyperlink(cell), ref activeHyperlink, output);
                    output.Write(current.Grid.GetGrapheme(cell));
                }
                WriteHyperlinkTransition(null, ref activeHyperlink, output);
                column = Math.Max(column + 1, end);
            }
        }

        if (activeStyle is not null)
            output.Write(ResetSequence);
    }

    private static int FindLeadingColumn(TerminalGrid previous, TerminalGrid current, int column, int row)
    {
        while (column > 0 &&
               (previous.GetCell(column, row).IsContinuation || current.GetCell(column, row).IsContinuation))
        {
            column--;
        }
        return column;
    }

    private static int ExpandChangedRun(TerminalGrid previous, TerminalGrid current, int start, int row)
    {
        var end = start;
        while (end < current.Width)
        {
            var differs = !current.CellEquals(previous, end, row);
            if (!differs && end > start)
                break;

            var oldCell = previous.GetCell(end, row);
            var newCell = current.GetCell(end, row);
            end = Math.Min(current.Width, end + Math.Max(1, Math.Max((int)oldCell.DisplayWidth, newCell.DisplayWidth)));
        }
        return Math.Max(start + 1, end);
    }

    public static void WriteLine(TerminalGrid grid, int y, AnsiFrameWriter output)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        if (y >= grid.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        var lastNonBlank = -1;
        for (var x = 0; x < grid.Width; x++)
        {
            var cell = grid.GetCell(x, y);
            if (!cell.IsContinuation &&
                (!grid.GetGrapheme(cell).SequenceEqual(" ") || cell.Style != Style.Default || !cell.HyperlinkId.IsNone))
            {
                lastNonBlank = x;
            }
        }

        if (lastNonBlank < 0)
        {
            return;
        }

        Style? currentStyle = null;
        Span<char> styleBuffer = stackalloc char[64];
        TerminalHyperlink? currentHyperlink = null;
        for (var x = 0; x <= lastNonBlank; x++)
        {
            var cell = grid.GetCell(x, y);
            if (cell.IsContinuation)
            {
                continue;
            }

            WriteStyleTransition(cell.Style, ref currentStyle, styleBuffer, output);
            WriteHyperlinkTransition(grid.GetHyperlink(cell), ref currentHyperlink, output);
            output.Write(grid.GetGrapheme(cell));
        }

        if (currentStyle is not null)
        {
            output.Write(ResetSequence);
        }
        WriteHyperlinkTransition(null, ref currentHyperlink, output);
    }

    public static void WriteScrollbackRow(ScrollbackRow row, AnsiFrameWriter output)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(output);
        Style? currentStyle = null;
        Span<char> styleBuffer = stackalloc char[64];
        TerminalHyperlink? currentHyperlink = null;
        foreach (var cell in row.Cells)
        {
            WriteStyleTransition(cell.Style, ref currentStyle, styleBuffer, output);
            WriteHyperlinkTransition(cell.Metadata.Hyperlink, ref currentHyperlink, output);
            output.Write(cell.Grapheme);
        }
        if (currentStyle is not null)
            output.Write(ResetSequence);
        WriteHyperlinkTransition(null, ref currentHyperlink, output);
    }

    public static void WriteCursorMove(int x, int y, AnsiFrameWriter output)
    {
        output.Write("\x1b[");
        output.WriteInt(y + 1);
        output.Write(';');
        output.WriteInt(x + 1);
        output.Write('H');
    }

    private static void WriteStyleTransition(
        Style nextStyle,
        ref Style? currentStyle,
        Span<char> styleBuffer,
        AnsiFrameWriter output)
    {
        if (currentStyle == nextStyle)
        {
            return;
        }

        if (currentStyle is not null)
        {
            output.Write(ResetSequence);
        }

        var styleLength = nextStyle.WriteAnsiPrefix(styleBuffer);
        if (styleLength > 0)
        {
            output.Write(styleBuffer[..styleLength]);
        }

        currentStyle = nextStyle;
    }

    private static void WriteHyperlinkTransition(
        TerminalHyperlink? next,
        ref TerminalHyperlink? current,
        AnsiFrameWriter output)
    {
        if (Equals(current, next))
        {
            return;
        }

        if (current is not null)
        {
            output.Write(HyperlinkClose);
        }

        if (next is not null)
        {
            output.Write(HyperlinkOpen);
            output.Write(next.Destination.AsSpan());
            output.Write(StringTerminator);
        }

        current = next;
    }
}
