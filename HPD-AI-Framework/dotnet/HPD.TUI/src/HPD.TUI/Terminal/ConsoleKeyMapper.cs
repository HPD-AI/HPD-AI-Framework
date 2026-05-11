using HPD.TUI.Core;

namespace HPD.TUI.Terminal;

internal static class ConsoleKeyMapper
{
    public static KeyEvent Map(ConsoleKeyInfo key)
    {
        var modifiers = KeyModifiers.None;

        if ((key.Modifiers & ConsoleModifiers.Control) != 0)
        {
            modifiers |= KeyModifiers.Ctrl;
        }

        if ((key.Modifiers & ConsoleModifiers.Shift) != 0)
        {
            modifiers |= KeyModifiers.Shift;
        }

        if ((key.Modifiers & ConsoleModifiers.Alt) != 0)
        {
            modifiers |= KeyModifiers.Alt;
        }

        return key.Key switch
        {
            ConsoleKey.Enter => new KeyEvent(KeyCode.Enter, default, modifiers),
            ConsoleKey.Backspace => new KeyEvent(KeyCode.Backspace, default, modifiers),
            ConsoleKey.Delete => new KeyEvent(KeyCode.Delete, default, modifiers),
            ConsoleKey.Escape => new KeyEvent(KeyCode.Escape, default, modifiers),
            ConsoleKey.Tab => new KeyEvent(KeyCode.Tab, default, modifiers),
            ConsoleKey.Home => new KeyEvent(KeyCode.Home, default, modifiers),
            ConsoleKey.End => new KeyEvent(KeyCode.End, default, modifiers),
            ConsoleKey.PageUp => new KeyEvent(KeyCode.PageUp, default, modifiers),
            ConsoleKey.PageDown => new KeyEvent(KeyCode.PageDown, default, modifiers),
            ConsoleKey.UpArrow => new KeyEvent(KeyCode.UpArrow, default, modifiers),
            ConsoleKey.DownArrow => new KeyEvent(KeyCode.DownArrow, default, modifiers),
            ConsoleKey.LeftArrow => new KeyEvent(KeyCode.LeftArrow, default, modifiers),
            ConsoleKey.RightArrow => new KeyEvent(KeyCode.RightArrow, default, modifiers),
            _ when key.KeyChar != '\0' => new KeyEvent(KeyCode.Character, new Rune(key.KeyChar), modifiers),
            _ => new KeyEvent(KeyCode.Unknown, default, modifiers)
        };
    }
}
