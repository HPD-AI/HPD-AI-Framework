using HPD.Events.Signals;
using HPD.TUI.Terminal;

namespace HPD.TUI.Rendering;

public record TuiRunOptions
{
    public TimeSpan? MaxFrameInterval { get; init; }

    public TimeSpan? AnimationTickInterval { get; init; }

    public int InputMailboxCapacity { get; init; } = 4096;

    public EventLoopMailboxOverflowMode InputOverflowMode { get; init; } =
        EventLoopMailboxOverflowMode.Backpressure;

    public bool RenderOnStart { get; init; } = true;
}

public sealed record ManagedTerminalRunOptions : TuiRunOptions
{
    public ManagedTerminalRenderBounds Bounds { get; init; } =
        ManagedTerminalRenderBounds.ViewportAnchored();
}

public readonly record struct ManagedTerminalRenderBounds(
    int MaxRows,
    ManagedTerminalAnchor Anchor)
{
    public static ManagedTerminalRenderBounds ViewportAnchored(
        int maxRows = 0,
        ManagedTerminalAnchor anchor = ManagedTerminalAnchor.Bottom) =>
        new(maxRows, anchor);

    public int ResolveRows(TerminalSize size)
    {
        var rows = MaxRows <= 0 ? size.Height : MaxRows;
        return Math.Max(size.Height, rows);
    }
}

public enum ManagedTerminalAnchor
{
    Top,
    Bottom
}
