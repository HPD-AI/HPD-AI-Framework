using HPD.Agent.TUI.Runtime;

namespace HPD.Agent.TUI.Console.Modes;

internal static class DirectMode
{
    public static async Task RunAsync(string[] args)
    {
        var options = ModeOptions.Parse(args);
        var agentId = options.Get("agent", "tui-direct-agent");
        var sessionId = options.Get("session", "local-session");
        var branchId = options.Get("branch", "main");
        var model = options.Get("model", "deepseek/deepseek-v4-flash");
        var providers = ConsoleProviderContext.Create();

        var agent = await providers.CreateAgentBuilder()
            .WithAgentId(agentId)
            .WithInstructions("You are a direct in-process HPD Agent. Be concise and helpful.")
            .WithProvider("openrouter", model)
            .BuildAsync();

        var scope = new AgentTuiRuntimeScope(agentId, sessionId, branchId);
        await using var runtime = new InMemoryAgentTuiRuntime(agent, scope);
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            scope,
            tui => tui
                .AddAgentTuiDefaults()
                .AddConsoleBranding("direct")
                .AddConsoleModelsDevModelSelection(providers)
                .AddConsoleProviderCommands(providers)
                .AddConsoleAgentChat());
        await app.RunAsync();
    }
}
