using HPD.Agent.TUI.Composition;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands.Pages;

internal static class CodingCommandPages
{
    public static HpdAgentTuiPageDescriptor CommandsPage()
        => new("hpd.coding.commands", context => new CodingCommandsPageComponent(context.State))
        {
            Title = "Coding Commands",
            Description = "Show coding command execution state.",
            Hidden = true
        };

    public static HpdAgentTuiPageDescriptor BackgroundPage()
        => new("hpd.coding.background", context => new CodingBackgroundCommandsPageComponent(context.State))
        {
            Title = "Background Commands",
            Description = "Show active background command state.",
            Hidden = true
        };
}

internal abstract class CodingCommandPageComponentBase : IComponent
{
    protected CodingCommandPageComponentBase(AgentTuiStateBag state)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    protected AgentTuiStateBag State { get; }

    public Measurement Measure(in RenderContext context, int maxWidth)
        => new(Math.Min(20, maxWidth), Math.Min(120, maxWidth), 1);

    public abstract void Render(in RenderContext context, int maxWidth, ref SegmentWriter output);

    public bool HandleInput(in TuiInputEvent input)
    {
        return false;
    }

    protected bool TryGetStore(out CodingCommandExecutionStore store)
        => State.TryGet(CodingCommandExecutionStore.StateKey, out store!);

    protected static void WriteCommandBlock(
        CodingCommandExecutionState command,
        int maxWidth,
        int tailRows,
        in RenderContext context,
        ref SegmentWriter output)
    {
        var title = $"{CodingCommandRenderText.VerbFor(command)} {command.DisplayCommand}";
        CodingCommandPanelText.WriteClipped(title, maxWidth, context.Theme.Accent, ref output);

        WriteMetadataLine(command, maxWidth, in context, ref output);
        WriteOutputTail(command, maxWidth, tailRows, in context, ref output);
    }

    protected static void WriteBackgroundCommandBlock(
        CodingCommandExecutionState command,
        int maxWidth,
        int tailRows,
        in RenderContext context,
        ref SegmentWriter output)
    {
        CodingCommandPanelText.WriteClipped($"• {command.DisplayCommand}", maxWidth, context.Theme.Accent, ref output);
        WriteBackgroundDetail("task", command.BackgroundTaskId ?? command.CommandId, maxWidth, in context, ref output);
        if (!string.IsNullOrWhiteSpace(command.WorkingDirectory))
        {
            WriteBackgroundDetail("cwd", command.WorkingDirectory, maxWidth, in context, ref output);
        }

        WriteBackgroundDetail("state", BuildBackgroundStateText(command), maxWidth, in context, ref output);
        WriteOutputTail(command, maxWidth, tailRows, in context, ref output);
    }

    private static void WriteBackgroundDetail(
        string label,
        string value,
        int maxWidth,
        in RenderContext context,
        ref SegmentWriter output)
    {
        output.WriteLineBreak();
        output.Write($"  {label} ".AsSpan(), context.Theme.Border);
        CodingCommandPanelText.WriteClipped(value, Math.Max(0, maxWidth - label.Length - 3), context.Theme.Border, ref output);
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

    private static void WriteMetadataLine(
        CodingCommandExecutionState command,
        int maxWidth,
        in RenderContext context,
        ref SegmentWriter output)
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
        output.Write("  ".AsSpan(), context.Theme.Border);
        CodingCommandPanelText.WriteClipped(
            string.Join("  ", parts.Where(static part => !string.IsNullOrWhiteSpace(part))),
            Math.Max(0, maxWidth - 2),
            context.Theme.Border,
            ref output);
    }

    private static void WriteOutputTail(
        CodingCommandExecutionState command,
        int maxWidth,
        int tailRows,
        in RenderContext context,
        ref SegmentWriter output)
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
            output.Write("  no output observed".AsSpan(), context.Theme.Border);
            return;
        }

        foreach (var line in snapshot.Lines)
        {
            output.WriteLineBreak();
            output.Write("  ".AsSpan(), context.Theme.Border);
            var style = line.Stream == ExecuteCommandStreamKind.Stderr
                ? context.Theme.Warning
                : context.Theme.Border;
            CodingCommandPanelText.WriteClipped(line.Text, outputWidth, style, ref output);
        }

        if (snapshot.OmittedLineCount > 0)
        {
            output.WriteLineBreak();
            output.Write($"  ... +{snapshot.OmittedLineCount} lines".AsSpan(), context.Theme.Border);
        }
    }
}

internal sealed class CodingCommandsPageComponent : CodingCommandPageComponentBase
{
    public CodingCommandsPageComponent(AgentTuiStateBag state)
        : base(state)
    {
    }

    public override void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        output.Write("Coding commands".AsSpan(), context.Theme.Accent);
        if (!TryGetStore(out var store))
        {
            output.WriteLineBreak();
            output.Write("No command state observed.".AsSpan(), context.Theme.Border);
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
            output.Write("No command state observed.".AsSpan(), context.Theme.Border);
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
    public CodingBackgroundCommandsPageComponent(AgentTuiStateBag state)
        : base(state)
    {
    }

    public override void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        output.Write("Background commands".AsSpan(), context.Theme.Accent);
        if (!TryGetStore(out var store) || store.ActiveBackground.Count == 0)
        {
            output.WriteLineBreak();
            output.Write("No active background commands.".AsSpan(), context.Theme.Border);
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
