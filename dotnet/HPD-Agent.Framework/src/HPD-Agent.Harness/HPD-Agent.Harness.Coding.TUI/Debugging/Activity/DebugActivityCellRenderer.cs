using HPD.Agent.TUI.Composition;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

internal sealed class DebugActivityCellRenderer(CodingHarnessTuiTheme theme)
    : IAgentTuiTranscriptRenderer<DebugActivityCell>
{
    public IComponent Create(AgentTuiTranscriptRenderContext<DebugActivityCell> context)
        => new CodingTranscriptLabeledComponent(
            context.Cell.Label,
            context.DepthIndent,
            new DebugTextRowsView(context.Cell.Lines, theme),
            context.Services,
            theme);
}
