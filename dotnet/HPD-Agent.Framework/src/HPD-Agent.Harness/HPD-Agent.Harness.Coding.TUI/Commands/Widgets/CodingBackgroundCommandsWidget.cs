using HPD.Agent.TUI.Composition;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands.Widgets;

internal sealed class CodingBackgroundCommandsWidget : IAgentTuiWidget
{
    public IComponent Create(AgentTuiWidgetContext context)
        => new CodingBackgroundCommandsComponent(context.State);
}

internal sealed class CodingBackgroundCommandsComponent : IComponent
{
    private const int MaxCommands = 3;
    private const int TailRowsPerCommand = 2;
    private readonly AgentTuiStateBag _state;

    public CodingBackgroundCommandsComponent(AgentTuiStateBag state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var commands = GetBackgroundCommands();
        if (commands.Count == 0)
        {
            return new Measurement(0, 0, 0);
        }

        var rows = 1;
        foreach (var command in commands.Take(MaxCommands))
        {
            var snapshot = command.Output.CreateSnapshot(headRows: 0, tailRows: TailRowsPerCommand, maxVisibleRows: TailRowsPerCommand);
            rows += 1 + Math.Max(1, snapshot.Lines.Count);
        }

        if (commands.Count > MaxCommands)
        {
            rows++;
        }

        return new Measurement(Math.Min(8, maxWidth), Math.Min(maxWidth, 96), rows);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        var commands = GetBackgroundCommands();
        if (commands.Count == 0 || maxWidth <= 0)
        {
            return;
        }

        output.Write($"bg {commands.Count}".AsSpan(), context.Theme.Accent);
        var shown = 0;
        foreach (var command in commands.Take(MaxCommands))
        {
            shown++;
            output.WriteLineBreak();
            output.Write("  ".AsSpan(), context.Theme.Border);
            CodingCommandWidgetText.WriteClipped(
                CodingCommandWidgetText.BuildMetadata(command, includeWorkingDirectory: true),
                Math.Max(0, maxWidth - 2),
                context.Theme.Accent,
                ref output);

            var outputWidth = Math.Max(0, maxWidth - 4);
            var snapshot = command.Output.CreateSnapshot(
                headRows: 0,
                tailRows: TailRowsPerCommand,
                maxVisibleRows: TailRowsPerCommand,
                wrapWidth: outputWidth);
            if (snapshot.Lines.Count == 0)
            {
                output.WriteLineBreak();
                output.Write("    no output observed".AsSpan(), context.Theme.Border);
                continue;
            }

            foreach (var line in snapshot.Lines)
            {
                output.WriteLineBreak();
                output.Write("    ".AsSpan(), context.Theme.Border);
                var style = line.Stream == ExecuteCommandStreamKind.Stderr
                    ? context.Theme.Warning
                    : context.Theme.Border;
                CodingCommandWidgetText.WriteClipped(line.Text, outputWidth, style, ref output);
            }
        }

        if (commands.Count > shown)
        {
            output.WriteLineBreak();
            output.Write($"  ... +{commands.Count - shown} background commands".AsSpan(), context.Theme.Border);
        }
    }

    public void HandleInput(in KeyEvent key)
    {
    }

    public void Invalidate()
    {
    }

    private IReadOnlyList<CodingCommandExecutionState> GetBackgroundCommands()
    {
        if (!_state.TryGet(CodingCommandExecutionStore.StateKey, out CodingCommandExecutionStore store))
        {
            return [];
        }

        return store.ActiveBackground
            .OrderByDescending(static command => command.BackgroundedAt ?? command.StartedAt)
            .ToArray();
    }
}
