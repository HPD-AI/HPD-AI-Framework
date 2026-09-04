using HPD.Events.Signals;
namespace HPD.TUI.Rendering;

/// <summary>Configures mailbox capacity, animation ticks, and visual frame admission.</summary>
public record TuiRunOptions
{
    /// <summary>Gets the policy used to admit dirty visual states for rendering.</summary>
    public TuiFramePolicy FramePolicy { get; init; } = new();

    /// <summary>Gets the periodic animation interval, or <see langword="null"/> to disable periodic ticks.</summary>
    public TimeSpan? AnimationTickInterval { get; init; }

    /// <summary>Gets the maximum number of queued input and mutation events.</summary>
    public int InputMailboxCapacity { get; init; } = 4096;

    /// <summary>Gets the behavior used when the mailbox reaches capacity.</summary>
    public EventLoopMailboxOverflowMode InputOverflowMode { get; init; } =
        EventLoopMailboxOverflowMode.Backpressure;

    /// <summary>Gets whether the staged root is rendered when the loop starts.</summary>
    public bool RenderOnStart { get; init; } = true;
}

/// <summary>Controls frame admission under high-frequency invalidation.</summary>
public sealed record TuiFramePolicy
{
    /// <summary>Gets the maximum number of admitted frames per second.</summary>
    public int MaximumFramesPerSecond { get; init; } = 60;

    /// <summary>Gets whether handled input may bypass the next frame deadline.</summary>
    public bool RenderImmediatelyOnInput { get; init; } = true;

    /// <summary>Gets whether multiple pending visual states collapse into the newest state.</summary>
    public bool DropIntermediateVisualStates { get; init; } = true;

    internal TimeSpan MinimumFrameInterval
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumFramesPerSecond);
            return TimeSpan.FromSeconds(1d / MaximumFramesPerSecond);
        }
    }
}
