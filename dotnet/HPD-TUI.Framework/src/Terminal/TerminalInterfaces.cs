namespace HPD.TUI.Terminal;

public interface ITerminalDisplay : IDisposable
{
    TerminalSize GetSize();

    void Write(ReadOnlySpan<char> text);

    void Flush();

    void HideCursor();

    void ShowCursor();
}

public interface ITerminalInput : IAsyncDisposable
{
    ValueTask<TerminalInputEvent> ReadAsync(CancellationToken cancellationToken = default);
}

public interface ITerminalSession : ITerminalDisplay, IAsyncDisposable
{
    ITerminalInput Input { get; }
}
