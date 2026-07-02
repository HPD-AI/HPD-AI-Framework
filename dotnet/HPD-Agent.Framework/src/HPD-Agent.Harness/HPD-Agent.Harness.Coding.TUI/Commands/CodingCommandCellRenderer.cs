using HPD.Agent.TUI.Composition;
using HPD.Agent.ToolHarness.Coding.TUI.Commands.Views;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands;

internal sealed class CodingCommandCellRenderer : IAgentTuiTranscriptRenderer<CodingCommandCell>
{
    private readonly CodingHarnessTuiTheme _theme;

    public CodingCommandCellRenderer(CodingHarnessTuiTheme theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    public IComponent Create(AgentTuiTranscriptRenderContext<CodingCommandCell> context)
        => new CodingTranscriptLabeledComponent(
            $"• {CodingCommandTranscriptEntryFactory.LabelFor(context.Cell)} {context.Cell.DisplayCommand}",
            context.DepthIndent,
            new CodingCommandCellView(context.Cell, _theme),
            context.Services,
            _theme);
}
