using HPD.Agent.TUI.Composition;
using HPD.Agent.ToolHarness.Coding.TUI.SubAgents.Views;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.SubAgents;

internal sealed class CodingSubAgentCellRenderer(CodingHarnessTuiTheme theme)
    : IAgentTuiTranscriptRenderer<CodingSubAgentCell>
{
    private readonly CodingHarnessTuiTheme _theme = theme ?? throw new ArgumentNullException(nameof(theme));

    public IComponent Create(AgentTuiTranscriptRenderContext<CodingSubAgentCell> context)
    {
        var marker = context.Cell.State is CodingSubAgentState.Failed or CodingSubAgentState.Cancelled ? "■" : "•";
        var task = string.IsNullOrWhiteSpace(context.Cell.TaskName) ? "" : $" · {context.Cell.TaskName}";
        return new CodingTranscriptLabeledComponent(
            $"{marker} {context.Cell.RoleName}{task}",
            context.DepthIndent,
            new CodingSubAgentCellView(context.Cell, _theme),
            context.Services,
            _theme);
    }
}
