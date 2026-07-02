using HPD.Agent.TUI.Composition;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.Harness;

internal sealed class CodingHarnessToolCellRenderer : IAgentTuiTranscriptRenderer<CodingHarnessToolCell>
{
    private readonly CodingHarnessTuiTheme _theme;

    public CodingHarnessToolCellRenderer(CodingHarnessTuiTheme theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    public IComponent Create(AgentTuiTranscriptRenderContext<CodingHarnessToolCell> context)
        => new CodingTranscriptLabeledComponent(
            context.Cell.IsActive ? "• Coding tools" : "• Coding tools",
            context.DepthIndent,
            new CodingHarnessToolCellView(context.Cell, _theme),
            context.Services,
            _theme);
}

internal sealed class CodingHarnessToolCellView : IComponent
{
    private readonly CodingHarnessToolCell _cell;
    private readonly CodingHarnessTuiTheme _theme;

    public CodingHarnessToolCellView(CodingHarnessToolCell cell, CodingHarnessTuiTheme theme)
    {
        _cell = cell ?? throw new ArgumentNullException(nameof(cell));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
        => new(Math.Min(maxWidth, _cell.Summary.Length + 4), maxWidth, 1);

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        if (maxWidth <= 0)
        {
            return;
        }

        var prefix = "  └ ";
        output.Write(prefix.AsSpan(), _theme.ResolvePrefix(context.Theme));
        var text = _cell.Summary.Length <= Math.Max(0, maxWidth - prefix.Length)
            ? _cell.Summary
            : string.Concat(_cell.Summary.AsSpan(0, Math.Max(0, maxWidth - prefix.Length - 3)), "...");
        output.Write(text.AsSpan(), _theme.ResolveText(context.Theme));
    }

    public bool HandleInput(in TuiInputEvent input) => false;
}
