using HPD.Agent.TUI.Console.Demo;

namespace HPD.Agent.TUI.Console.Modes;

internal static class DemoMode
{
    public static async Task RunAsync(string[] args)
    {
        await using var runtime = new SampleAgentTuiRuntime();
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            configure: tui => tui
                .AddAgentTuiDefaults()
                .AddConsoleAgentChat()
                .AddSampleContributions());
        await app.RunAsync();
    }
}
