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

public sealed record NormalTerminalRunOptions : TuiRunOptions
{
    public NormalTerminalRenderBounds Bounds { get; init; } =
        NormalTerminalRenderBounds.ViewportAnchored();
}

public readonly record struct NormalTerminalRenderBounds(
    int MaxRows,
    NormalTerminalAnchor Anchor)
{
    public static NormalTerminalRenderBounds ViewportAnchored(
        int maxRows = 0,
        NormalTerminalAnchor anchor = NormalTerminalAnchor.Bottom) =>
        new(maxRows, anchor);

    public int ResolveRows(TerminalSize size)
    {
        var rows = MaxRows <= 0 ? size.Height : MaxRows;
        return Math.Max(size.Height, rows);
    }
}

public enum NormalTerminalAnchor
{
    Top,
    Bottom
}
