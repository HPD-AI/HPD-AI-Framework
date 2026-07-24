using HPD.Agent.TUI.Composition;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

internal sealed class DebugStatusFooterItem(CodingHarnessTuiTheme theme) : IAgentTuiFooterItem
{
    public IComponent Create(AgentTuiFooterContext context) => new Component(context.State, theme);

    private sealed class Component(AgentTuiStateBag state, CodingHarnessTuiTheme theme) : IComponent
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
            if (!state.TryGet<DebugTuiState>(DebugTuiState.StateKey, out var debug) ||
                debug.ActiveTreeId is not { } id ||
                !debug.Trees.TryGetValue(id, out var tree))
                return "";
            var marker = tree.Status switch
            {
                "Stopped" => "◆",
                "Running" => "▶",
                "Faulted" => "!",
                "Terminated" => "■",
                _ => "◌"
            };
            return $"{marker} debug {tree.Status.ToLowerInvariant()} · " +
                $"{tree.Breakpoints.Verified}/{tree.Breakpoints.Requested} bp";
        }
    }
}
