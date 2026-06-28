using HPD.Agent.TUI.Console.Demo;
using HPD.Agent.TUI.Composition;

namespace HPD.Agent.TUI.Console.Modes;

internal static class DemoMode
{
    public static async Task RunAsync(string[] args)
    {
        await using var runtime = new SampleAgentTuiRuntime();
        var store = new AgentTuiContributionStore();
        var packages = ConsolePackageContext.Create(store);
        new HpdAgentTuiBuilder(store, HpdContributionOwner.App)
            .AddAgentTuiDefaults()
            .AddPackageManagement(packages.TuiPackages)
            .AddConsoleAgentChat()
            .AddSampleContributions();
        var registries = new HpdAgentTuiRegistryProvider(store);
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            null,
            registries);
        await app.RunAsync();
    }
}
