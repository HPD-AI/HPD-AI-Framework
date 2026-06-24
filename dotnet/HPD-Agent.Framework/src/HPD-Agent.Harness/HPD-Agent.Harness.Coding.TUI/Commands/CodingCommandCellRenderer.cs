using HPD.Agent.TUI.Composition;
using HPD.Agent.ToolHarness.Coding.TUI.Commands.Views;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands;

internal sealed class CodingCommandCellRenderer : IAgentTuiTranscriptRenderer<CodingCommandCell>
{
    public IComponent Create(AgentTuiTranscriptRenderContext<CodingCommandCell> context)
        => new CodingTranscriptLabeledComponent(
            $"• {CodingCommandTranscriptEntryFactory.LabelFor(context.Cell)} {context.Cell.DisplayCommand}",
            context.DepthIndent,
            new CodingCommandCellView(context.Cell),
            context.Services);
}
