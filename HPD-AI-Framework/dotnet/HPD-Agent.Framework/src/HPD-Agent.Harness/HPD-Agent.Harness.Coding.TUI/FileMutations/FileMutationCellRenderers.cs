using HPD.Agent.TUI.Composition;
using HPD.Agent.ToolHarness.Coding.TUI.FileMutations.Views;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.FileMutations;

internal sealed class FileMutationCellRenderer : IAgentTuiTranscriptRenderer<FileMutationCell>
{
    public IComponent Create(AgentTuiTranscriptRenderContext<FileMutationCell> context)
        => new CodingTranscriptLabeledComponent(
            context.Cell.Label,
            context.DepthIndent,
            new FileMutationCellView(context.Cell),
            context.Services);
}

internal sealed class CodingDiagnosticsCellRenderer : IAgentTuiTranscriptRenderer<CodingDiagnosticsCell>
{
    public IComponent Create(AgentTuiTranscriptRenderContext<CodingDiagnosticsCell> context)
        => new CodingTranscriptLabeledComponent(
            $"• Diagnostics {context.Cell.Path}",
            context.DepthIndent,
            new CodingDiagnosticsCellView(context.Cell),
            context.Services);
}
