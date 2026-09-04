namespace HPD.TUI.Core;

/// <summary>Provides immutable terminal and frame state to component measurement and painting.</summary>
public readonly record struct RenderContext
{
    /// <summary>Creates a render context for one admitted frame.</summary>
    /// <param name="width">Physical screen width in cells.</param>
    /// <param name="height">Physical screen height in cells.</param>
    /// <param name="theme">Theme used to resolve semantic styles.</param>
    /// <param name="colorSystem">Terminal color encoding level.</param>
    /// <param name="elapsed">Admitted animation time.</param>
    /// <param name="capabilities">Optional terminal capabilities.</param>
    public RenderContext(
        int width,
        int height,
        Theme theme,
        ColorSystem colorSystem = ColorSystem.TrueColor,
        TimeSpan elapsed = default,
        TerminalCapabilities capabilities = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
        Theme = theme ?? throw new ArgumentNullException(nameof(theme));
        ColorSystem = colorSystem;
        Elapsed = elapsed;
        Capabilities = capabilities;
    }

    /// <summary>Gets the physical screen width in cells.</summary>
    public int Width { get; }

    /// <summary>Gets the physical screen height in cells.</summary>
    public int Height { get; }

    /// <summary>Gets the active theme.</summary>
    public Theme Theme { get; }

    /// <summary>Gets the terminal color encoding level.</summary>
    public ColorSystem ColorSystem { get; }

    /// <summary>Gets animation time admitted for this frame.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>Gets terminal features that may affect layout or painting.</summary>
    public TerminalCapabilities Capabilities { get; }
}

/// <summary>Describes terminal capabilities observable by components.</summary>
[Flags]
public enum TerminalCapabilities
{
    /// <summary>No optional terminal capability is available.</summary>
    None = 0,
    /// <summary>Absolute cursor positioning is available.</summary>
    AbsoluteCursorAddressing = 1 << 0,
    /// <summary>Synchronized output is available.</summary>
    SynchronizedOutput = 1 << 1,
    /// <summary>OSC 8 hyperlinks are available.</summary>
    Hyperlinks = 1 << 2,
    /// <summary>The terminal supports all capabilities known by this framework version.</summary>
    All = AbsoluteCursorAddressing | SynchronizedOutput | Hyperlinks
}

/// <summary>Identifies a terminal color encoding level.</summary>
public enum ColorSystem
{
    /// <summary>Legacy ANSI colors.</summary>
    Legacy = 0,
    /// <summary>The ANSI 256-color palette.</summary>
    Ansi256 = 1,
    /// <summary>Twenty-four-bit RGB color.</summary>
    TrueColor = 2
}
