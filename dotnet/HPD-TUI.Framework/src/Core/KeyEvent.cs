namespace HPD.TUI.Core;

public readonly record struct KeyEvent(
    KeyCode Key,
    Rune Character = default,
    KeyModifiers Modifiers = KeyModifiers.None,
    string? Text = null);

public enum KeyCode
{
    Unknown = 0,
    Character = 1,
    Enter = 2,
    Backspace = 3,
    Delete = 4,
    Escape = 5,
    Tab = 6,
    Home = 7,
    End = 8,
    PageUp = 9,
    PageDown = 10,
    UpArrow = 11,
    DownArrow = 12,
    LeftArrow = 13,
    RightArrow = 14,
    Paste = 15
}

[Flags]
public enum KeyModifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4
}
