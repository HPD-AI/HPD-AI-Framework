using HPD.TUI.Core;

namespace HPD.TUI.Terminal;

public readonly record struct TerminalInputEvent(
    TerminalInputEventKind Kind,
    KeyEvent Key = default,
    string? Text = null,
    TerminalSize Size = default)
{
    public static TerminalInputEvent FromKey(KeyEvent key) =>
        new(TerminalInputEventKind.Key, key);

    public static TerminalInputEvent FromPaste(string text) =>
        new(TerminalInputEventKind.Paste, new KeyEvent(KeyCode.Paste, Text: text), text);

    public static TerminalInputEvent FromResize(TerminalSize size) =>
        new(TerminalInputEventKind.Resize, Size: size);

    public static TerminalInputEvent Stop { get; } = new(TerminalInputEventKind.Stop);
}

public enum TerminalInputEventKind
{
    Unknown = 0,
    Key = 1,
    Paste = 2,
    Resize = 3,
    Mouse = 4,
    Focus = 5,
    Blur = 6,
    Stop = 7
}
