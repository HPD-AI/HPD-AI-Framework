using HPD.TUI.Terminal;

namespace HPD.TUI.Core;

public readonly record struct TuiInputEvent(TerminalInputEvent Terminal)
{
    public TerminalInputEventKind Kind => Terminal.Kind;

    public KeyEvent KeyEvent => Terminal.Key;

    public KeyCode Key => Terminal.Key.Key;

    public Rune Character => Terminal.Key.Character;

    public KeyModifiers Modifiers => Terminal.Key.Modifiers;

    public string? Text => Terminal.Text ?? Terminal.Key.Text;

    public TerminalSize Size => Terminal.Size;

    public static TuiInputEvent FromKey(KeyEvent key) =>
        new(TerminalInputEvent.FromKey(key));

    public static implicit operator TuiInputEvent(KeyEvent key) => FromKey(key);
}
