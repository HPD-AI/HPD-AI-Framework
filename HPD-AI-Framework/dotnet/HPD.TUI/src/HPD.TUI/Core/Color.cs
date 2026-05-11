namespace HPD.TUI.Core;

public readonly record struct Color(byte R, byte G, byte B)
{
    public static Color Black { get; } = new(0, 0, 0);

    public static Color White { get; } = new(255, 255, 255);

    public static Color Gray { get; } = new(128, 128, 128);

    public static Color Cyan { get; } = new(0, 255, 255);

    public static Color Blue { get; } = new(0, 0, 255);

    public static Color Red { get; } = new(255, 0, 0);

    public static Color Green { get; } = new(0, 255, 0);

    public static Color Yellow { get; } = new(255, 255, 0);
}
