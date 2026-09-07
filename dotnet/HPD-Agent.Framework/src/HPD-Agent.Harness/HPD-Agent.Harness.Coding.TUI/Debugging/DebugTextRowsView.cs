using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

internal sealed class DebugTextRowsView(
    IReadOnlyList<string> rows,
    CodingHarnessTuiTheme theme) : HPD.TUI.Core.Component
{
    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
    {
        var maxWidth = constraints.MaxWidth;
        var width = rows.Count == 0
            ? 0
            : rows.Max(row => Math.Min(row.Length, maxWidth));
        return new Measurement(width, width, Math.Max(1, rows.Count));
    }

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
    {
        var maxWidth = output.MaxWidth;
        var style = theme.ResolveMuted(context.Theme);
        for (var index = 0; index < rows.Count; index++)
        {
            if (index > 0) output.WriteLineBreak();
            var row = rows[index];
            output.Write(row.AsSpan(0, Math.Min(row.Length, maxWidth)), style);
        }
    }

    public override bool HandleInput(in TuiInputEvent input) => false;
}
