using HPD.Agent.TUI.Composition;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands.Status;

internal sealed class CodingCommandStatusItem : IAgentTuiStatusItem
{
    public IComponent Create(AgentTuiStatusContext context)
        => new CodingCommandStatusComponent(context.State);
}

internal sealed class CodingBackgroundTerminalStatusItem : IAgentTuiStatusItem
{
    public IComponent Create(AgentTuiStatusContext context)
        => new CodingBackgroundTerminalStatusComponent(context.State);
}

internal sealed class CodingCommandOutputStatusItem : IAgentTuiStatusItem
{
    public IComponent Create(AgentTuiStatusContext context)
        => new CodingCommandOutputStatusComponent(context.State);
}

internal abstract class CodingCommandStatusComponentBase : IComponent
{
    protected CodingCommandStatusComponentBase(AgentTuiStateBag state)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    protected AgentTuiStateBag State { get; }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var text = BuildText();
        return string.IsNullOrEmpty(text)
            ? new Measurement(0, 0, 0)
            : new Measurement(Math.Min(text.Length, maxWidth), Math.Min(text.Length, maxWidth), 1);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        var text = BuildText();
        if (string.IsNullOrEmpty(text) || maxWidth <= 0)
        {
            return;
        }

        output.Write(Clip(text, maxWidth).AsSpan(), context.Theme.Border);
    }

    public void HandleInput(in KeyEvent key)
    {
    }

    public void Invalidate()
    {
    }

    protected bool TryGetStore(out CodingCommandExecutionStore store)
        => State.TryGet(CodingCommandExecutionStore.StateKey, out store);

    protected abstract string BuildText();

    private static string Clip(string text, int maxWidth)
    {
        if (text.Length <= maxWidth)
        {
            return text;
        }

        if (maxWidth <= 3)
        {
            return new string('.', maxWidth);
        }

        return string.Concat(text.AsSpan(0, maxWidth - 3), "...");
    }
}

internal sealed class CodingCommandStatusComponent : CodingCommandStatusComponentBase
{
    public CodingCommandStatusComponent(AgentTuiStateBag state)
        : base(state)
    {
    }

    protected override string BuildText()
    {
        if (!TryGetStore(out var store))
        {
            return "";
        }

        var active = store.ActiveForeground;
        if (active.Count > 0)
        {
            var command = active[0];
            return active.Count == 1
                ? $"cmd running {command.DisplayCommand}"
                : $"cmd {active.Count} running";
        }

        var latest = store.RecentCompleted.FirstOrDefault();
        if (latest is null)
        {
            return "";
        }

        return latest.DisplayState switch
        {
            CodingCommandDisplayState.Completed => $"cmd ok {latest.DisplayCommand}",
            CodingCommandDisplayState.Failed => $"cmd failed {latest.DisplayCommand}",
            CodingCommandDisplayState.Cancelled => $"cmd cancelled {latest.DisplayCommand}",
            CodingCommandDisplayState.TimedOut => $"cmd timed out {latest.DisplayCommand}",
            _ => ""
        };
    }
}

internal sealed class CodingBackgroundTerminalStatusComponent : CodingCommandStatusComponentBase
{
    public CodingBackgroundTerminalStatusComponent(AgentTuiStateBag state)
        : base(state)
    {
    }

    protected override string BuildText()
    {
        if (!TryGetStore(out var store))
        {
            return "";
        }

        var active = store.ActiveBackground;
        return active.Count switch
        {
            0 => "",
            1 => $"bg 1 {active[0].DisplayCommand}",
            _ => $"bg {active.Count}"
        };
    }
}

internal sealed class CodingCommandOutputStatusComponent : CodingCommandStatusComponentBase
{
    public CodingCommandOutputStatusComponent(AgentTuiStateBag state)
        : base(state)
    {
    }

    protected override string BuildText()
    {
        if (!TryGetStore(out var store))
        {
            return "";
        }

        var flags = new List<string>();
        foreach (var command in store.ActiveForeground
                     .Concat(store.ActiveBackground)
                     .Concat(store.RecentCompleted.Take(1)))
        {
            if (command.OutputTruncated || command.CombinedBytesDiscarded > 0)
            {
                flags.Add("truncated");
            }

            if (command.OutputEventsSuppressed)
            {
                flags.Add("suppressed");
            }

            if (command.BinaryOutputObserved)
            {
                flags.Add("binary");
            }

            if (command.Artifacts.HasAny)
            {
                flags.Add("artifacts");
            }
        }

        if (flags.Count == 0)
        {
            return "";
        }

        return $"output {string.Join("/", flags.Distinct(StringComparer.Ordinal))}";
    }
}
