using HPD.TUI.Terminal;

namespace HPD.TUI.Rendering;

internal readonly record struct TuiLoopEvent(
    TuiLoopEventKind Kind,
    TerminalInputEvent Input = default,
    Func<ValueTask>? Callback = null);

internal enum TuiLoopEventKind
{
    Input,
    RenderRequested,
    Tick,
    Stop,
    Callback
}
