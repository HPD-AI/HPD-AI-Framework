using HPD.Agent.TUI.Composition;
using HPD.Agent.ToolHarness.Coding.TUI.Exploration.Views;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.Exploration;

internal sealed class CodingExplorationCellRenderer : IAgentTuiTranscriptRenderer<CodingExplorationCell>
{
    private readonly CodingHarnessTuiTheme _theme;

    public CodingExplorationCellRenderer(CodingHarnessTuiTheme theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    public IComponent Create(AgentTuiTranscriptRenderContext<CodingExplorationCell> context)
        => new CodingTranscriptLabeledComponent(
            context.Cell.IsActive ? "• Exploring" : "• Explored",
            context.DepthIndent,
            new CodingExplorationCellView(context.Cell, _theme),
            context.Services,
            _theme);
}
