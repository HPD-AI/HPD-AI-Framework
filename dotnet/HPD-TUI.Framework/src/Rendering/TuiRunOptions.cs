using HPD.Events.Signals;
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
