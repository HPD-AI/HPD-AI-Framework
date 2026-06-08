using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Models;
using HPD.TUI.Components;

namespace HPD.Agent.TUI.Console;

internal static class ConsoleAgentTuiContributions
{
    public static HpdAgentTuiBuilder AddConsoleAgentChat(
        this HpdAgentTuiBuilder tui,
        bool includeReasoning = true,
        bool includeToolLifecycle = true)
    {
        tui
            .AddConsoleCommandSurface()
            .TryAddEventHandler("console.branch-run-status", new BranchRunStatusHandler())
            .TryAddEventHandler("console.text-stream", new TextMessageStreamHandler());

        if (includeReasoning)
        {
            tui.TryAddEventHandler("console.reasoning-stream", new ReasoningStreamHandler());
        }

        if (includeToolLifecycle)
        {
            tui.TryAddEventHandler("console.tool-lifecycle", new ToolLifecycleHandler());
        }

        return tui
            .AddConsoleAgentCommands()
            .AddConsoleSessionBranchCommands()
            .TryAddSlashCommand(new HpdAgentTuiCommandDescriptor("status", context =>
            {
                ConsoleCommandSurface.Show(context, "Runtime scope", $"""
                    - agent: `{context.Scope.AgentId}`
                    - session: `{context.Scope.SessionId}`
                    - branch: `{context.Scope.BranchId}`
                    """);
            })
            {
                Title = "/status",
                Description = "Show the current agent/session/branch scope."
            });
    }
}
