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
    private DisplayPayload(string? text, TuiSurface? surface)
    {
        Text = text;
        Surface = surface;
    }

    /// <summary>Gets text owned by the display-list generation, when applicable.</summary>
    public string? Text { get; }

    /// <summary>Gets the explicitly retained surface, when applicable.</summary>
    public TuiSurface? Surface { get; }

    /// <summary>Creates an immutable text payload.</summary>
    public static DisplayPayload FromText(string text) => new(text ?? throw new ArgumentNullException(nameof(text)), null);

    /// <summary>Creates a retained-surface payload.</summary>
    public static DisplayPayload FromSurface(TuiSurface surface) => new(null, surface ?? throw new ArgumentNullException(nameof(surface)));
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
