using HPD.Agent.TUI.Composition;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.Exploration.Status;

internal sealed class CodingExplorationStatusItem : IAgentTuiStatusItem
{
    private readonly CodingHarnessTuiTheme _theme;

    public CodingExplorationStatusItem(CodingHarnessTuiTheme theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    public IComponent Create(AgentTuiStatusContext context)
        => new CodingExplorationStatusComponent(context.State, _theme);
}

internal sealed class CodingExplorationStatusComponent : IComponent
{
    private readonly AgentTuiStateBag _state;
    private readonly CodingHarnessTuiTheme _theme;

    public CodingExplorationStatusComponent(AgentTuiStateBag state, CodingHarnessTuiTheme theme)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
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

        output.Write(Clip(text, maxWidth).AsSpan(), _theme.ResolveMuted(context.Theme));
    }

    public bool HandleInput(in TuiInputEvent input)
    {
        return false;
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
