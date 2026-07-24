using HPD.Agent.TUI.Composition;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

internal sealed class DebugBreakpointCellRenderer(CodingHarnessTuiTheme theme)
    : IAgentTuiTranscriptRenderer<DebugBreakpointCell>
{
    public IComponent Create(AgentTuiTranscriptRenderContext<DebugBreakpointCell> context)
        => new CodingTranscriptLabeledComponent(
            context.Cell.Label,
            context.DepthIndent,
            new DebugBreakpointCellView(context.Cell, theme),
            context.Services,
            theme);
}
