using HPD.Agent.Sandbox.Local;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Runtime;
using Microsoft.Extensions.AI;

namespace HPD.Agent.TUI.Console.Modes;

internal static class CodingMode
{
    public static async Task RunAsync(string[] args)
    {
        var options = ModeOptions.Parse(args);
        var agentId = options.Get("agent", "tui-coding-agent");
        var sessionId = options.Get("session", "local-session");
        var threadId = options.Get("thread", "main");
        var model = options.Get("model", "qwen/qwen3.7-plus");
        var workspacePath = Path.GetFullPath(options.Get("workspace", Directory.GetCurrentDirectory()));
        if (!Directory.Exists(workspacePath))
        {
            global::System.Console.Error.WriteLine($"Workspace does not exist: {workspacePath}");
            System.Environment.ExitCode = 2;
            return;
        }

        var workspace = new AgentWorkspace(
            "default",
            workspacePath,
            [new AgentWorkspaceRoot("default", workspacePath, Path.GetFileName(workspacePath))]);
        var providers = ConsoleProviderContext.Create();

        var agent = await providers.CreateAgentBuilder()
            .WithAgentId(agentId)
            .WithName("TUI Coding Agent")
            .WithInstructions("You are a coding agent. Be concise, inspect before editing, and explain code changes clearly.")
            .WithProvider("openrouter", model)
            .WithLocalSandbox()
            .WithHarnessCollapsing()
            .WithToolHarness<CodingToolHarness>()
            .BuildAsync();

        if (options.Has("list-tools"))
        {
            var tools = agent.DefaultOptions?.Tools?
                .OfType<AIFunction>()
                .Select(tool => tool.Name)
                .Order(StringComparer.Ordinal)
                .ToArray() ?? [];

            global::System.Console.WriteLine($"Agent: {agent.Name}");
            global::System.Console.WriteLine("Coding tools:");
            foreach (var tool in tools)
                global::System.Console.WriteLine($"- {tool}");

            return;
        }

        var scope = new AgentTuiRuntimeScope(agentId, sessionId, threadId);
        await using var runtime = new InMemoryAgentTuiRuntime(agent, scope);
        var store = new AgentTuiContributionStore();
        var packages = ConsolePackageContext.Create(store);
        new HpdAgentTuiBuilder(store, HpdContributionOwner.App)
            .AddAgentTuiDefaults()
            .AddConsoleBranding("coding")
            .AddPackageManagement(packages.TuiPackages)
            .AddConsoleModelsDevModelSelection(providers)
            .AddRunConfigContributor("console.coding", (_, runConfig) =>
            {
                if (providers.ModelSelection.Current is { } selected)
                {
                    runConfig.SetProviderModel(selected.ProviderKey, selected.ModelId);
                }

                runConfig.AddContextOverride(AgentWorkspace.ContextKey, workspace);
            })
            .AddConsoleProviderCommands(providers)
            .AddConsoleCodingAgent();
        var registries = new HpdAgentTuiRegistryProvider(store);
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            scope,
            registries);
        await app.RunAsync();
    }
}
