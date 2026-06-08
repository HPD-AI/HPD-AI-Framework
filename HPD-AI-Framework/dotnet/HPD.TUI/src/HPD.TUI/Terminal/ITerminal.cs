using HPD.TUI.Core;

namespace HPD.TUI.Terminal;

public interface ITerminal : IDisposable
{
    TerminalSize GetSize();

    void Write(ReadOnlySpan<char> text);

    void Flush();

    bool TryReadKey(out KeyEvent key);

    void HideCursor();

    void ShowCursor();
}
