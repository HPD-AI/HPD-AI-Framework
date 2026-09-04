using HPD.Agent.TUI.Composition;
using HPD.Agent.ToolHarness.Coding.TUI.SourcePresentation;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

internal sealed class DebugStoppedCellRenderer(CodingHarnessTuiTheme theme)
    : IAgentTuiTranscriptRenderer<DebugStoppedCell>
{
    public IComponent Create(AgentTuiTranscriptRenderContext<DebugStoppedCell> context)
        => new CodingTranscriptLabeledComponent(
            Label(context.Cell),
            context.DepthIndent,
            new DebugStoppedCellView(context.Cell, theme),
            context.Services,
            theme);

    private static string Label(DebugStoppedCell cell)
    {
        var location = cell.Summary is { DisplayPath: { } path, Line: { } line }
            ? $" at {path}:{line}"
            : "";
        return $"• Stopped · {cell.Reason}{location}";
    }
}

internal sealed class DebugStoppedCellView : HPD.TUI.Core.Component
{
    private readonly IComponent _content;

    public DebugStoppedCellView(DebugStoppedCell cell, CodingHarnessTuiTheme theme)
    {
        var summary = cell.Summary;
        var preview = summary?.SourcePreview;
        var hunks = preview?.Hunks.Select(hunk => new AnnotatedSourceHunk(
            hunk.Lines.Select((text, offset) =>
            {
                var line = hunk.StartLine + offset;
                var current = line == summary?.Line;
                return new AnnotatedSourceLine(
                    line,
                    text,
                    current ? [new SourceAnnotation("▶", SourceAnnotationTone.Current)] : [],
                    current && summary?.FrameName is { } name ? name : null,
                    current ? SourceLineEmphasis.Current : SourceLineEmphasis.None);
            }).ToArray())).ToArray();
        if (hunks is null or { Length: 0 })
        {
            var detail = summary?.InspectionSucceeded == false
                ? $"Location unavailable ({summary.SafeFailureCode})"
                : summary?.FrameName ?? "Collecting top frame…";
            _content = new DebugTextRowsView([detail], theme);
            return;
        }
        _content = new AnnotatedSourceView(new(
            preview?.DisplayPath ?? summary?.DisplayPath,
            preview?.Language,
            hunks,
            preview?.Truncated == true), theme);
    }

    public override Measurement Measure(in RenderContext context, int maxWidth)
        => _content.Measure(context, maxWidth);
    public override void Render(in RenderContext context, int maxWidth, ref DisplayListBuilder output)
        => _content.Render(context, maxWidth, ref output);
    public override bool HandleInput(in TuiInputEvent input) => false;
}
