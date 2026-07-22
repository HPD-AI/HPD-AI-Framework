using HPD.Agent.TUI.Composition;
using HPD.TUI.Core;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.LanguageServers;

internal sealed class LanguageServerStatusFooterItem(CodingHarnessTuiTheme theme) : IAgentTuiFooterItem
{
    public IComponent Create(AgentTuiFooterContext context)
        => new LanguageServerStatusComponent(context.State, theme);
}

internal sealed class LanguageServerStatusComponent(
    AgentTuiStateBag state,
    CodingHarnessTuiTheme theme) : IComponent
{
    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var text = BuildText();
        return new(Math.Min(text.Length, maxWidth), Math.Min(text.Length, maxWidth), text.Length == 0 ? 0 : 1);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        var text = BuildText();
        if (text.Length > 0 && maxWidth > 0)
            output.Write(text.AsSpan(0, Math.Min(text.Length, maxWidth)), theme.ResolveMuted(context.Theme));
    }

    public bool HandleInput(in TuiInputEvent input) => false;

    private string BuildText()
    {
        if (!state.TryGet<CodingLanguageServerTuiState>(CodingLanguageServerTuiState.StateKey, out var snapshot))
            return "";

        var running = snapshot.Servers.Count(static server => server.Status == LanguageServerStatusKind.Running);
        var failed = snapshot.Servers.Count(static server => server.Status == LanguageServerStatusKind.Unavailable);
        var starting = snapshot.Servers.Count(static server => server.Status == LanguageServerStatusKind.Starting);
        var parts = new List<string>();
        if (running > 0) parts.Add($"● {running} LSP");
        if (starting > 0) parts.Add($"◌ {starting} starting");
        if (failed > 0) parts.Add($"◉ {failed} unavailable");
        return string.Join("  ", parts);
    }
}
