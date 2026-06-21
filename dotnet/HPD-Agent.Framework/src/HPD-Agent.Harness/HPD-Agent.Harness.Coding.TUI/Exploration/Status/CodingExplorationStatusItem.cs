using HPD.Agent.TUI.Composition;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.Exploration.Status;

internal sealed class CodingExplorationStatusItem : IAgentTuiStatusItem
{
    public IComponent Create(AgentTuiStatusContext context)
        => new CodingExplorationStatusComponent(context.State);
}

internal sealed class CodingExplorationStatusComponent : IComponent
{
    private readonly AgentTuiStateBag _state;

    public CodingExplorationStatusComponent(AgentTuiStateBag state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

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

    private string BuildText()
        => _state.TryGet<CodingExplorationStore>(CodingExplorationStore.StateKey, out var store)
            ? CodingExplorationDisplayFormatter.StatusText(store)
            : "";

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
