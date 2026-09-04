using HPD.Agent.TUI.Composition;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands.Pages;

internal static class CodingCommandPages
{
    public static HpdAgentTuiPageDescriptor CommandsPage(CodingHarnessTuiTheme theme)
        => new("hpd.coding.commands", context => new CodingCommandsPageComponent(context.State, theme))
        {
            Title = "Coding Commands",
            Description = "Show coding command execution state.",
            Hidden = true
        };

    public static HpdAgentTuiPageDescriptor BackgroundPage(CodingHarnessTuiTheme theme)
        => new("hpd.coding.background", context => new CodingBackgroundCommandsPageComponent(context.State, theme))
        {
            Title = "Background Commands",
            Description = "Show active background command state.",
            Hidden = true
        };
}

internal abstract class CodingCommandPageComponentBase : HPD.TUI.Core.Component
{
    protected CodingCommandPageComponentBase(AgentTuiStateBag state, CodingHarnessTuiTheme theme)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        Theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    protected AgentTuiStateBag State { get; }
    protected CodingHarnessTuiTheme Theme { get; }

    public override Measurement Measure(in RenderContext context, int maxWidth)
        => new(Math.Min(20, maxWidth), Math.Min(120, maxWidth), 1);

    public abstract override void Render(in RenderContext context, int maxWidth, ref DisplayListBuilder output);

    public override bool HandleInput(in TuiInputEvent input)
    {
        return false;
    }

    protected bool TryGetStore(out CodingCommandExecutionStore store)
        => State.TryGet(CodingCommandExecutionStore.StateKey, out store!);

    protected void WriteCommandBlock(
        CodingCommandExecutionState command,
        int maxWidth,
        int tailRows,
        in RenderContext context,
        ref DisplayListBuilder output)
    {
        var title = $"{CodingCommandRenderText.VerbFor(command)} {command.DisplayCommand}";
        CodingCommandPanelText.WriteClipped(title, maxWidth, Theme.ResolveCommandState(MapState(command.DisplayState), context.Theme), ref output);

        WriteMetadataLine(command, maxWidth, in context, ref output);
        WriteOutputTail(command, maxWidth, tailRows, in context, ref output);
    }

    protected void WriteBackgroundCommandBlock(
        CodingCommandExecutionState command,
        int maxWidth,
        int tailRows,
        in RenderContext context,
        ref DisplayListBuilder output)
    {
        CodingCommandPanelText.WriteClipped($"• {command.DisplayCommand}", maxWidth, Theme.ResolveCommandState(MapState(command.DisplayState), context.Theme), ref output);
        WriteBackgroundDetail("handle", command.OperationId ?? command.CommandId, maxWidth, in context, ref output);
        if (!string.IsNullOrWhiteSpace(command.WorkingDirectory))
        {
            WriteBackgroundDetail("cwd", command.WorkingDirectory, maxWidth, in context, ref output);
        }

        WriteBackgroundDetail("state", BuildBackgroundStateText(command), maxWidth, in context, ref output);
        WriteOutputTail(command, maxWidth, tailRows, in context, ref output);
    }

    private void WriteBackgroundDetail(
        string label,
        string value,
        int maxWidth,
        in RenderContext context,
        ref DisplayListBuilder output)
    {
        output.WriteLineBreak();
        output.Write($"  {label} ".AsSpan(), Theme.ResolvePrefix(context.Theme));
        CodingCommandPanelText.WriteClipped(value, Math.Max(0, maxWidth - label.Length - 3), Theme.ResolveMuted(context.Theme), ref output);
    }

    private static string BuildBackgroundStateText(CodingCommandExecutionState command)
    {
        if (!command.IsActive)
        {
            return command.ExitCode is { } exitCode
                ? $"{command.DisplayState.ToString().ToLowerInvariant()} exit {exitCode}"
                : command.DisplayState.ToString().ToLowerInvariant();
        }

        var started = command.BackgroundedAt ?? command.StartedAt;
        if (started is null)
        {
            return "running";
        }

        var elapsed = DateTimeOffset.UtcNow - started.Value;
        return $"running {FormatAge(elapsed)}";
    }

    private static string FormatAge(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalSeconds < 60)
        {
            return $"{Math.Max(0, (int)elapsed.TotalSeconds)}s";
        }

        if (elapsed.TotalMinutes < 60)
        {
            return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:D2}s";
        }

        return $"{(int)elapsed.TotalHours}h {elapsed.Minutes:D2}m";
    }

    private void WriteMetadataLine(
        CodingCommandExecutionState command,
        int maxWidth,
        in RenderContext context,
        ref DisplayListBuilder output)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(command.WorkingDirectory))
        {
            parts.Add(command.WorkingDirectory);
        }

        if (command.ProcessId is { } pid)
        {
            parts.Add($"pid {pid}");
        }

        if (command.ExitCode is { } exitCode)
        {
            parts.Add($"exit {exitCode}");
        }

        if (command.CompletionKind is { } completion)
        {
            parts.Add(completion.ToString());
        }

        parts.Add(CodingCommandPanelText.BuildMetadata(command, includeWorkingDirectory: false));

        output.WriteLineBreak();
        output.Write("  ".AsSpan(), Theme.ResolvePrefix(context.Theme));
        CodingCommandPanelText.WriteClipped(
            string.Join("  ", parts.Where(static part => !string.IsNullOrWhiteSpace(part))),
            Math.Max(0, maxWidth - 2),
            Theme.ResolveMuted(context.Theme),
            ref output);
    }

    private void WriteOutputTail(
        CodingCommandExecutionState command,
        int maxWidth,
        int tailRows,
        in RenderContext context,
        ref DisplayListBuilder output)
    {
        var outputWidth = Math.Max(0, maxWidth - 2);
        var snapshot = command.Output.CreateSnapshot(
            headRows: 0,
            tailRows: tailRows,
            maxVisibleRows: tailRows,
            wrapWidth: outputWidth);
        if (snapshot.Lines.Count == 0)
        {
            output.WriteLineBreak();
            output.Write("  no output observed".AsSpan(), Theme.ResolveMuted(context.Theme));
            return;
        }

        foreach (var line in snapshot.Lines)
        {
            output.WriteLineBreak();
            output.Write("  ".AsSpan(), Theme.ResolvePrefix(context.Theme));
            var style = line.Stream == ExecuteCommandStreamKind.Stderr
                ? Theme.ResolveCommandErrorOutput(context.Theme)
                : Theme.ResolveCommandOutput(context.Theme);
            CodingCommandPanelText.WriteClipped(line.Text, outputWidth, style, ref output);
        }

        if (snapshot.OmittedLineCount > 0)
        {
            output.WriteLineBreak();
            output.Write($"  ... +{snapshot.OmittedLineCount} lines".AsSpan(), Theme.ResolveMuted(context.Theme));
        }
    }

    private static CodingCommandTranscriptState MapState(CodingCommandDisplayState state)
        => state switch
        {
            CodingCommandDisplayState.Running => CodingCommandTranscriptState.Running,
            CodingCommandDisplayState.Backgrounded => CodingCommandTranscriptState.Backgrounded,
            CodingCommandDisplayState.Completed => CodingCommandTranscriptState.Completed,
            CodingCommandDisplayState.Failed => CodingCommandTranscriptState.Failed,
            CodingCommandDisplayState.Cancelled => CodingCommandTranscriptState.Cancelled,
            CodingCommandDisplayState.TimedOut => CodingCommandTranscriptState.TimedOut,
            _ => CodingCommandTranscriptState.Exited
        };
}

internal sealed class CodingCommandsPageComponent : CodingCommandPageComponentBase
{
    public CodingCommandsPageComponent(AgentTuiStateBag state, CodingHarnessTuiTheme theme)
        : base(state, theme)
    {
    }

    public override void Render(in RenderContext context, int maxWidth, ref DisplayListBuilder output)
    {
        output.Write("Coding commands".AsSpan(), Theme.ResolveCommandState(CodingCommandTranscriptState.Running, context.Theme));
        if (!TryGetStore(out var store))
        {
            output.WriteLineBreak();
            output.Write("No command state observed.".AsSpan(), Theme.ResolveMuted(context.Theme));
            return;
        }

        var commands = store.ActiveForeground
            .Concat(store.ActiveBackground)
            .Concat(store.RecentCompleted)
            .DistinctBy(static command => command.CommandId)
            .OrderByDescending(static command => command.StartedAt ?? command.CompletedAt)
            .ToArray();

        if (commands.Length == 0)
        {
            output.WriteLineBreak();
            output.Write("No command state observed.".AsSpan(), Theme.ResolveMuted(context.Theme));
            return;
        }

        foreach (var command in commands)
        {
            output.WriteLineBreak();
            output.WriteLineBreak();
            WriteCommandBlock(command, maxWidth, tailRows: 6, in context, ref output);
        }
    }
}

internal sealed class CodingBackgroundCommandsPageComponent : CodingCommandPageComponentBase
{
    public CodingBackgroundCommandsPageComponent(AgentTuiStateBag state, CodingHarnessTuiTheme theme)
        : base(state, theme)
    {
    }

    public override void Render(in RenderContext context, int maxWidth, ref DisplayListBuilder output)
    {
        output.Write("Background commands".AsSpan(), Theme.ResolveCommandState(CodingCommandTranscriptState.Backgrounded, context.Theme));
        if (!TryGetStore(out var store) || store.ActiveBackground.Count == 0)
        {
            output.WriteLineBreak();
            output.Write("No active background commands.".AsSpan(), Theme.ResolveMuted(context.Theme));
            return;
        }

        foreach (var command in store.ActiveBackground.OrderByDescending(static command => command.BackgroundedAt ?? command.StartedAt))
        {
            output.WriteLineBreak();
            output.WriteLineBreak();
            WriteBackgroundCommandBlock(command, maxWidth, tailRows: 8, in context, ref output);
        }
    }
}
