using HPD.TUI.Terminal;

namespace HPD.TUI.Rendering;

internal readonly record struct TuiLoopEvent(
    TuiLoopEventKind Kind,
    TerminalInputEvent Input = default);

internal enum TuiLoopEventKind
{
    Input,
    RenderRequested,
    Tick,
    Stop
}
