using HPD.Agent.TUI.Composition;
using HPD.Agent.ToolHarness.Coding.TUI.FileMutations.Views;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.FileMutations;

internal sealed class FileMutationCellRenderer : IAgentTuiTranscriptRenderer<FileMutationCell>
{
    private readonly CodingHarnessTuiTheme _theme;

    public FileMutationCellRenderer(CodingHarnessTuiTheme theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    public IComponent Create(AgentTuiTranscriptRenderContext<FileMutationCell> context)
        => new CodingTranscriptLabeledComponent(
            context.Cell.Label,
            context.DepthIndent,
            new FileMutationCellView(context.Cell, _theme),
            context.Services,
            _theme);
}

internal sealed class CodingDiagnosticsCellRenderer : IAgentTuiTranscriptRenderer<CodingDiagnosticsCell>
{
    private readonly CodingHarnessTuiTheme _theme;

    public CodingDiagnosticsCellRenderer(CodingHarnessTuiTheme theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    public IComponent Create(AgentTuiTranscriptRenderContext<CodingDiagnosticsCell> context)
        => new CodingTranscriptLabeledComponent(
            $"• Diagnostics {context.Cell.Path}",
            context.DepthIndent,
            new CodingDiagnosticsCellView(context.Cell, _theme),
            context.Services,
            _theme);
}
