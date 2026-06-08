using HPD.TUI.Core;

namespace HPD.TUI.Terminal;

public class ConsoleTerminal : ITerminal
{
    public TerminalSize GetSize()
    {
        var width = Math.Max(1, Console.WindowWidth);
        var height = Math.Max(1, Console.WindowHeight);
        return new TerminalSize(width, height);
    }

    public void Write(ReadOnlySpan<char> text)
    {
        Console.Out.Write(text);
    }

    public void Flush()
    {
        Console.Out.Flush();
    }

    public bool TryReadKey(out KeyEvent key)
    {
        key = default;

        if (Console.IsInputRedirected || !Console.KeyAvailable)
        {
            return false;
        }

        key = ConsoleKeyMapper.Map(Console.ReadKey(intercept: true));
        return true;
    }

    public void HideCursor()
    {
        if (!Console.IsOutputRedirected)
        {
            Console.CursorVisible = false;
        }
    }

    public void ShowCursor()
    {
        if (!Console.IsOutputRedirected)
        {
            Console.CursorVisible = true;
        }
    }

    public void Dispose()
    {
        ShowCursor();
    }
}
