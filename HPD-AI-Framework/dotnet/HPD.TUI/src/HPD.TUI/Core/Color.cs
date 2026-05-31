namespace HPD.TUI.Core;

public readonly record struct Color(byte R, byte G, byte B, bool IsDefault = false)
{
    /// <summary>
    /// Represents the terminal's default color. When used as a background,
    /// no background ANSI code is emitted, allowing the terminal's theme
    /// (light or dark) to show through naturally.
    /// </summary>
    public static Color Default { get; } = new(0, 0, 0, IsDefault: true);

    public static Color Black { get; } = new(0, 0, 0);

    public static Color White { get; } = new(255, 255, 255);

    public static Color Gray { get; } = new(128, 128, 128);

    public static Color Cyan { get; } = new(0, 255, 255);

    public static Color Blue { get; } = new(0, 0, 255);

    public static Color Red { get; } = new(255, 0, 0);

    public static Color Green { get; } = new(0, 255, 0);

    public static Color Yellow { get; } = new(255, 255, 0);
}
