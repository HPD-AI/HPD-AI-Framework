using HPD.Agent.TUI.Composition;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.LanguageServers;

internal static class LanguageServerStatusPage
{
    public const string PageId = "hpd.coding.language-servers";

    public static HpdAgentTuiPageDescriptor Create(CodingHarnessTuiTheme theme)
        => new(PageId, context => new Component(context.State, theme))
        {
            Title = "Language Servers",
            Description = "Inspect activated language servers.",
            Hidden = true
        };

    private sealed class Component(AgentTuiStateBag state, CodingHarnessTuiTheme theme) : HPD.TUI.Core.Component
    {
        public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
            => new(Math.Min(20, constraints.MaxWidth), Math.Min(120, constraints.MaxWidth), 1);

        public override void Render(in RenderContext context, ref DisplayListBuilder output)
        {
            var maxWidth = output.MaxWidth;
            output.Write("Language servers".AsSpan(), theme.ResolveLabel(context.Theme));
            if (!state.TryGet<CodingLanguageServerTuiState>(CodingLanguageServerTuiState.StateKey, out var snapshot) ||
                snapshot.Servers.Count == 0)
            {
                output.WriteLineBreak();
                output.Write("No language servers are active in this runtime.".AsSpan(), theme.ResolveMuted(context.Theme));
                return;
            }

            foreach (var server in snapshot.Servers)
            {
                output.WriteLineBreak();
                var line = $"• {server.ServerId}  {server.Status.ToString().ToLowerInvariant()}  {server.Root}";
                output.Write(line.AsSpan(0, Math.Min(line.Length, maxWidth)), theme.ResolveText(context.Theme));
                if (!string.IsNullOrWhiteSpace(server.Message))
                {
                    output.WriteLineBreak();
                    var detail = $"  {server.Message}";
                    output.Write(detail.AsSpan(0, Math.Min(detail.Length, maxWidth)), theme.ResolveMuted(context.Theme));
                }
            }
        }

        public override bool HandleInput(in TuiInputEvent input) => false;
    }
}
