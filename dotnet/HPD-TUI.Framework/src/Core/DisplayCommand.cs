using HPD.TUI.Layout;
using HPD.TUI.Rendering;

namespace HPD.TUI.Core;

/// <summary>Identifies a retained compositor command.</summary>
public enum DisplayCommandKind
{
    /// <summary>Draws an immutable styled text run.</summary>
    TextRun,
    /// <summary>Fills a rectangular region.</summary>
    Fill,
    /// <summary>Draws a border around a rectangular region.</summary>
    Border,
    /// <summary>Pushes a rectangular clipping region.</summary>
    PushClip,
    /// <summary>Restores the preceding clipping region.</summary>
    PopClip,
    /// <summary>Sets the requested terminal cursor.</summary>
    SetCursor,
    /// <summary>Replays an explicitly retained surface.</summary>
    ReplaySurface
}

/// <summary>Contains the immutable payload referenced by a display command.</summary>
public readonly record struct DisplayPayload
{
    private readonly PooledTextArena? _arena;
    private readonly int _textOffset;
    private readonly int _textLength;
    private readonly TuiSurface.SurfaceLease? _surfaceLease;

    private DisplayPayload(string? text, char? character, TuiSurface? surface,
        PooledTextArena? arena = null, int textOffset = 0, int textLength = 0,
        TuiSurface.SurfaceLease? surfaceLease = null)
    {
        Text = text;
        Character = character;
        Surface = surface;
        _arena = arena;
        _textOffset = textOffset;
        _textLength = textLength;
        _surfaceLease = surfaceLease;
    }

    /// <summary>Gets text owned by the display-list generation, when applicable.</summary>
    public string? Text { get; }

    /// <summary>Gets a single allocation-free character payload, when applicable.</summary>
    public char? Character { get; }

    /// <summary>Gets the explicitly retained surface, when applicable.</summary>
    public TuiSurface? Surface { get; }

    /// <summary>Creates an immutable text payload.</summary>
    public static DisplayPayload FromText(string text) => new(text ?? throw new ArgumentNullException(nameof(text)), null, null);

    /// <summary>Creates an allocation-free single-character payload.</summary>
    public static DisplayPayload FromCharacter(char character) => new(null, character, null);

    /// <summary>Creates a retained-surface payload.</summary>
    public static DisplayPayload FromSurface(TuiSurface surface) => new(null, null, surface ?? throw new ArgumentNullException(nameof(surface)));

    internal static DisplayPayload FromArena(PooledTextArena arena, int offset, int length) =>
        new(null, null, null, arena, offset, length);

    internal static DisplayPayload FromSurfaceLease(TuiSurface surface, TuiSurface.SurfaceLease lease) =>
        new(null, null, surface, surfaceLease: lease);

    internal ReadOnlySpan<char> GetTextSpan() => _arena is null ? Text.AsSpan() : _arena.GetSpan(_textOffset, _textLength);
    internal TuiSurface.SurfaceLease? SurfaceLease => _surfaceLease;
}

/// <summary>Describes one immutable command in a retained display-list generation.</summary>
/// <param name="Kind">The command operation.</param>
/// <param name="Bounds">The physical-screen region affected by the command.</param>
/// <param name="Style">The semantic terminal style.</param>
/// <param name="Metadata">Structural run metadata.</param>
/// <param name="Payload">The generation-owned immutable payload.</param>
public readonly record struct DisplayCommand(
    DisplayCommandKind Kind,
    LayoutRect Bounds,
    Style Style,
    TerminalRunMetadata Metadata,
    DisplayPayload Payload);
