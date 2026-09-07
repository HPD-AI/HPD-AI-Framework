using HPD.TUI.Core;
using HPD.TUI.Terminal;

namespace HPD.TUI.Rendering;

public static class TuiCapture
{
    public static TerminalGrid RenderToGrid(
        IComponent component,
        int width,
        int height,
        Theme? theme = null,
        ColorSystem colorSystem = ColorSystem.TrueColor,
        TimeSpan elapsed = default)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var context = new RenderContext(width, height, theme ?? Theme.Default, colorSystem, elapsed);
        var grid = new TerminalGrid(width, height);
        Render(component, grid, in context);
        return grid;
    }

    public static void RenderToGrid(
        IComponent component,
        TerminalGrid grid,
        Theme? theme = null,
        ColorSystem colorSystem = ColorSystem.TrueColor,
        TimeSpan elapsed = default)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(grid);

        grid.Clear();
        var context = new RenderContext(grid.Width, grid.Height, theme ?? Theme.Default, colorSystem, elapsed);
        Render(component, grid, in context);
    }

    public static string[] RenderToLines(
        IComponent component,
        int width,
        int height,
        Theme? theme = null,
        bool trimTrailingBlankLines = false,
        ColorSystem colorSystem = ColorSystem.TrueColor,
        TimeSpan elapsed = default)
    {
        using var grid = RenderToGrid(component, width, height, theme, colorSystem, elapsed);
        return ToPlainTextLines(grid, trimTrailingBlankLines);
    }

    public static string RenderToString(
        IComponent component,
        int width,
        int height,
        Theme? theme = null,
        bool trimTrailingBlankLines = false,
        ColorSystem colorSystem = ColorSystem.TrueColor,
        TimeSpan elapsed = default)
    {
        return string.Join('\n', RenderToLines(component, width, height, theme, trimTrailingBlankLines, colorSystem, elapsed));
    }

    public static string RenderToAnsi(
        IComponent component,
        int width,
        int height,
        Theme? theme = null,
        ColorSystem colorSystem = ColorSystem.TrueColor,
        TimeSpan elapsed = default)
    {
        using var grid = RenderToGrid(component, width, height, theme, colorSystem, elapsed);
        return ToAnsi(grid);
    }

    public static string[] ToPlainTextLines(TerminalGrid grid, bool trimTrailingBlankLines = false)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var lineCount = grid.Height;
        if (trimTrailingBlankLines)
        {
            while (lineCount > 0 && IsBlankLine(grid, lineCount - 1))
            {
                lineCount--;
            }
        }

        var lines = new string[lineCount];
        for (var y = 0; y < lineCount; y++)
        {
            lines[y] = ToPlainTextLine(grid, y);
        }

        return lines;
    }

    public static string ToPlainTextLine(TerminalGrid grid, int y)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        if (y >= grid.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        var builder = new StringBuilder(grid.Width);
        for (var x = 0; x < grid.Width; x++)
        {
            var cell = grid.GetCell(x, y);
            if (cell.IsContinuation)
            {
                continue;
            }

            builder.Append(grid.GetGrapheme(cell));
        }

        return builder.ToString();
    }

    public static int GetUsedLineCount(TerminalGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        // CursorY is the segment sink's transient write head. Its final value depends on whether
        // rasterization replayed the full display list or only damaged operations, so it cannot
        // describe semantic screen extent.
        var lineCount = grid.HasTerminalCursor
            ? Math.Clamp(grid.TerminalCursorY + 1, 1, grid.Height)
            : 1;
        for (var row = grid.Height - 1; row >= lineCount; row--)
            if (!IsBlankLine(grid, row))
            {
                lineCount = row + 1;
                break;
            }
        return lineCount;
    }

    public static void WriteLineTo(TerminalGrid grid, int y, ref DisplayListBuilder output)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        if (y >= grid.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        for (var x = 0; x < grid.Width; x++)
        {
            var cell = grid.GetCell(x, y);
            if (cell.IsContinuation)
            {
                continue;
            }

            output.Write(
                grid.GetGrapheme(cell),
                cell.Style,
                new TerminalRunMetadata(grid.GetHyperlink(cell)));
        }
    }

    public static string ToAnsi(TerminalGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        using var output = new AnsiFrameWriter();
        AnsiGridRenderer.WriteFull(grid, output);
        return output.ToString();
    }

    private static bool IsBlankLine(TerminalGrid grid, int y)
    {
        for (var x = 0; x < grid.Width; x++)
        {
            var cell = grid.GetCell(x, y);
            if (cell.IsContinuation)
            {
                continue;
            }

            if (!grid.GetGrapheme(cell).SequenceEqual(" "))
            {
                return false;
            }

            if (cell.Style != Style.Default)
            {
                return false;
            }
        }

        return true;
    }

    private static void Render(IComponent component, TerminalGrid grid, in RenderContext context)
    {
        using var displayList = new RetainedDisplayList();
        displayList.Prepare(component, in context, grid.Width);
        displayList.Replay(grid);
    }
}
