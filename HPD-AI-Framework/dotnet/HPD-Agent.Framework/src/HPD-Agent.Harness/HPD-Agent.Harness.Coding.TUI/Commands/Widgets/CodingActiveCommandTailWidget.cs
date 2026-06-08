using HPD.Agent.TUI.Composition;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands.Widgets;

internal sealed class CodingActiveCommandTailWidget : IAgentTuiWidget
{
    public IComponent Create(AgentTuiWidgetContext context)
        => new CodingActiveCommandTailComponent(context.State);
}

internal sealed class CodingActiveCommandTailComponent : IComponent
{
    private readonly AgentTuiStateBag _state;

    public CodingActiveCommandTailComponent(AgentTuiStateBag state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        if (!TryGetActiveCommand(out var command))
        {
            return new Measurement(0, 0, 0);
        }

        var snapshot = command.Output.CreateSnapshot(headRows: 0, tailRows: 3, maxVisibleRows: 3);
        var rows = 1 + Math.Max(1, snapshot.Lines.Count);
        return new Measurement(Math.Min(12, maxWidth), Math.Min(maxWidth, 96), rows);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        if (!TryGetActiveCommand(out var command) || maxWidth <= 0)
        {
            return;
        }

        var header = CodingCommandWidgetText.BuildMetadata(command, includeWorkingDirectory: false);
        CodingCommandWidgetText.WriteClipped(header, maxWidth, context.Theme.Accent, ref output);

        var outputWidth = Math.Max(0, maxWidth - 2);
        var snapshot = command.Output.CreateSnapshot(headRows: 0, tailRows: 3, maxVisibleRows: 3, wrapWidth: outputWidth);
        if (snapshot.Lines.Count == 0)
        {
            output.WriteLineBreak();
            output.Write("  no output observed".AsSpan(), context.Theme.Border);
            return;
        }

        for (var i = 0; i < snapshot.Lines.Count; i++)
        {
            output.WriteLineBreak();
            var line = snapshot.Lines[i];
            output.Write("  ".AsSpan(), context.Theme.Border);
            var style = line.Stream == ExecuteCommandStreamKind.Stderr
                ? context.Theme.Warning
                : context.Theme.Border;
            CodingCommandWidgetText.WriteClipped(line.Text, outputWidth, style, ref output);
        }
    }

    public void HandleInput(in KeyEvent key)
    {
    }

    public void Invalidate()
    {
    }

    private bool TryGetActiveCommand(out CodingCommandExecutionState command)
    {
        if (!_state.TryGet(CodingCommandExecutionStore.StateKey, out CodingCommandExecutionStore store))
        {
            command = null!;
            return false;
        }

        command = store.ActiveForeground
            .OrderByDescending(static candidate => candidate.StartedAt)
            .FirstOrDefault()!;
        return command is not null;
    }

}
