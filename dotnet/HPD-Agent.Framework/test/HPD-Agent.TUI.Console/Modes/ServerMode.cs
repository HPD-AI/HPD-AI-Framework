using HPD.Agent.AspNetCore;
using HPD.Agent;
using HPD.Agent.Providers.Generated;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;
using HPD.Agent.TUI.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HPD.Agent.TUI.Console.Modes;

internal static class ServerMode
{
    public static async Task RunAsync(string[] args)
    {
        var options = ModeOptions.Parse(args);
        var url = options.Get("url", "http://127.0.0.1:5057");
        var agentId = options.Get("agent", "tui-console-agent");
        var sessionId = options.Get("session", "local-session");
        var threadId = options.Get("thread", "main");
        var model = options.Get("model", "qwen/qwen3.7-plus");
        var dataRoot = options.Get("data", Path.Combine(Directory.GetCurrentDirectory(), ".hpd-tui-console"));
        var providers = ConsoleProviderContext.Create();

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls(url);
        builder.Configuration.AddConfiguration(providers.Configuration);
        builder.Logging.ClearProviders();
        builder.Services.AddRouting();
        builder.Services.AddHPDAgent(agentId, config =>
        {
            config.SessionStorePath = Path.Combine(dataRoot, "sessions");
            config.AgentStore = new JsonAgentStore(Path.Combine(dataRoot, "agents"));
            config.PersistAfterTurn = true;
            config.DefaultAgent = new AgentConfig
            {
                Name = "TUI Console Agent",
                SystemInstructions = "You are a server-hosted HPD Agent. Be concise and helpful.",
                Clients = new AgentClientsConfig
                {
                    Chat = new ChatClientConfig
                    {
                        Provider = new ProviderReference { Key = "openrouter" },
                        ModelName = model
                    }
                }
            };
        });

        await using var server = builder.Build();
        server.MapGet("/", () => "HPD Agent TUI console server is running.");
        server.MapGroup("/hpd").MapHPDAgentApi(agentId, api =>
        {
            api.MapEvals = false;
        });

        await server.StartAsync();

        try
        {
            var scope = new AgentTuiRuntimeScope(agentId, sessionId, threadId);
            await using var runtime = new HostedAgentTuiRuntime(new HostedAgentTuiRuntimeOptions
            {
                BaseAddress = new Uri($"{url.TrimEnd('/')}/hpd/"),
                DefaultScope = scope,
                EventComposition = AgentEventComposition.Create([CoreAgentEventModule.Fragment]),
                ProviderComposition = GeneratedProviderComposition.Composition
            });
            await using var app = HpdAgentTuiApp.Create(
                runtime,
                new DirectAgentTuiExecutionTarget(scope),
                tui => tui
                    .AddAgentTuiDefaults()
                    .AddConsoleBranding("server")
                    .AddConsoleModelsDevModelSelection(providers)
                    .AddConsoleProviderCommands(providers)
                    .AddConsoleAgentChat());
            await app.RunAsync();
        }
        finally
        {
            await server.StopAsync();
        }
    }
}
