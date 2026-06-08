using HPD.Agent.ToolHarness.Coding.TUI;

namespace HPD.Agent.TUI.Console;

internal static class ConsoleCodingTuiContributions
{
    public static HpdAgentTuiBuilder AddConsoleCodingAgent(this HpdAgentTuiBuilder tui)
        => tui
            .AddConsoleAgentChat(
                includeReasoning: false,
                includeToolLifecycle: false)
            .AddCodingHarnessTui();
}
