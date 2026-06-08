using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands.Views;

internal sealed class CodingCommandExecutionView : IComponent
{
    private readonly CodingCommandExecutionState _state;

    public CodingCommandExecutionView(CodingCommandExecutionState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var snapshot = _state.Output.CreateSnapshot();
        var rows = Math.Max(1, snapshot.Lines.Count);
        if (snapshot.OmittedLineCount > 0)
        {
            rows++;
        }

        if (CodingCommandRenderText.ShouldRenderSummary(_state))
        {
            rows++;
        }

        return new Measurement(1, Math.Min(maxWidth, 80), rows);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        var outputWidth = Math.Max(0, maxWidth - 2);
        var snapshot = _state.Output.CreateSnapshot(wrapWidth: outputWidth);
        var wroteLine = false;

        for (var i = 0; i < snapshot.Lines.Count; i++)
        {
            if (snapshot.OmittedLineCount > 0 && i == snapshot.HeadLineCount)
            {
                if (wroteLine)
                {
                    output.WriteLineBreak();
                }

                WriteOutputPrefix(wroteLine, context.Theme.Border, ref output);
                output.Write($"... +{snapshot.OmittedLineCount} lines".AsSpan(), context.Theme.Border);
                wroteLine = true;
            }

            var line = snapshot.Lines[i];
            if (wroteLine)
            {
                output.WriteLineBreak();
            }

            WriteOutputPrefix(wroteLine, context.Theme.Border, ref output);
            var style = line.Stream == ExecuteCommandStreamKind.Stderr
                ? context.Theme.Warning
                : context.Theme.Border;
            WriteClipped(line.Text, outputWidth, style, ref output);
            wroteLine = true;
        }

        if (!wroteLine)
        {
            output.Write("└ ".AsSpan(), context.Theme.Border);
            output.Write("no output observed".AsSpan(), context.Theme.Border);
            wroteLine = true;
        }

        if (CodingCommandRenderText.ShouldRenderSummary(_state))
        {
            output.WriteLineBreak();
            output.Write("  ".AsSpan(), context.Theme.Border);
            output.Write(CodingCommandRenderText.BuildSummary(_state).AsSpan(), StyleForState(context));
        }
    }

    private static void WriteOutputPrefix(bool continuation, Style style, ref SegmentWriter output)
    {
        output.Write((continuation ? "  " : "└ ").AsSpan(), style);
    }

    public void HandleInput(in KeyEvent key)
    {
    }

    public void Invalidate()
    {
    }

    private Style StyleForState(in RenderContext context)
        => _state.DisplayState switch
        {
            CodingCommandDisplayState.Completed => context.Theme.Success,
            CodingCommandDisplayState.Failed or CodingCommandDisplayState.TimedOut => context.Theme.Error,
            CodingCommandDisplayState.Cancelled => context.Theme.Warning,
            CodingCommandDisplayState.Backgrounded => context.Theme.Accent,
            _ => context.Theme.Border
        };

    private static void WriteClipped(string text, int width, Style style, ref SegmentWriter output)
    {
        if (width <= 0)
        {
            return;
        }

        var normalized = text.Replace('\t', ' ');
        if (normalized.Length <= width)
        {
            output.Write(normalized.AsSpan(), style);
            return;
        }

        if (width <= 1)
        {
            output.Write(".".AsSpan(), style);
            return;
        }

        var marker = width >= 3 ? "..." : new string('.', width);
        output.Write(normalized.AsSpan(0, width - marker.Length), style);
        output.Write(marker.AsSpan(), style);
    }
}
