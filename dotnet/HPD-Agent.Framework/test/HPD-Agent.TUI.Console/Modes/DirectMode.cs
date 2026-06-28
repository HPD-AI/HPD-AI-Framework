using HPD.Agent.TUI.Runtime;
using HPD.Agent.TUI.Composition;

namespace HPD.Agent.TUI.Console.Modes;

internal static class DirectMode
{
    public static async Task RunAsync(string[] args)
    {
        var options = ModeOptions.Parse(args);
        var agentId = options.Get("agent", "tui-direct-agent");
        var sessionId = options.Get("session", "local-session");
        var threadId = options.Get("thread", "main");
        var model = options.Get("model", "qwen/qwen3.7-plus");
        var providers = ConsoleProviderContext.Create();

        var agent = await providers.CreateAgentBuilder()
            .WithAgentId(agentId)
            .WithInstructions("You are a direct in-process HPD Agent. Be concise and helpful.")
            .WithProvider("openrouter", model)
            .BuildAsync();

        var scope = new AgentTuiRuntimeScope(agentId, sessionId, threadId);
        await using var runtime = new InMemoryAgentTuiRuntime(agent, scope);
        var store = new AgentTuiContributionStore();
        var packages = ConsolePackageContext.Create(store);
        new HpdAgentTuiBuilder(store, HpdContributionOwner.App)
            .AddAgentTuiDefaults()
            .AddConsoleBranding("direct")
            .AddPackageManagement(packages.TuiPackages)
            .AddConsoleModelsDevModelSelection(providers)
            .AddConsoleProviderCommands(providers)
            .AddConsoleAgentChat();
        var registries = new HpdAgentTuiRegistryProvider(store);
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            scope,
            registries);
        await app.RunAsync();
    }
}
