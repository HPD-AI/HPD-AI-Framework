using System.Text;
using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Console;

internal static class ConsoleAgentCommands
{
    private const string AgentsPageId = "console.agents";
    private const string AgentDetailPageId = "console.agent-detail";
    private static readonly ConsoleAgentPageState PageState = new();

    public static HpdAgentTuiBuilder AddConsoleAgentCommands(this HpdAgentTuiBuilder tui)
        => tui.AddConsoleCommandSurface()
            .TryAddPage(new HpdAgentTuiPageDescriptor(AgentsPageId, RenderAgentsPage)
            {
                Title = "Agents",
                Description = "Browse stored agent definitions.",
                Hidden = true,
                HandleInput = HandleAgentsPageInput
            })
            .TryAddPage(new HpdAgentTuiPageDescriptor(AgentDetailPageId, RenderAgentDetailPage)
            {
                Title = "Agent",
                Description = "Inspect a stored agent definition.",
                Hidden = true,
                HandleInput = HandleAgentDetailPageInput
            })
            .TryAddSlashCommand(new HpdAgentTuiCommandDescriptor("agents", ExecuteAgentsAsync)
        {
            Title = "/agents",
            Description = "List, switch, or delete stored agent definitions."
        });

    private static async ValueTask ExecuteAgentsAsync(AgentTuiCommandContext context)
    {
        if (context.Runtime is not IAgentTuiAgentRuntime runtime)
        {
            AppendNotice(context, "Agents are not supported by this runtime.", TranscriptSeverity.Warning);
            return;
        }

        var args = SplitArgs(context.Arguments);
        var verb = args.Count == 0 ? "list" : args[0];
        switch (verb)
        {
            case "list":
                await ShowAgentsAsync(context, runtime).ConfigureAwait(false);
                break;
            case "switch":
                await SwitchAgentAsync(context, runtime, args).ConfigureAwait(false);
                break;
            case "delete":
            case "rm":
                await DeleteAgentAsync(context, runtime, args).ConfigureAwait(false);
                break;
            default:
                ConsoleCommandSurface.Show(context, "Agent commands", Usage(), TranscriptSeverity.Warning);
                break;
        }
    }

    private static async Task ShowAgentsAsync(
        AgentTuiCommandContext context,
        IAgentTuiAgentRuntime runtime)
    {
        var agents = await runtime.ListAgentsAsync().ConfigureAwait(false);
        PageState.SetAgents(agents, context.Scope.AgentId);
        var selected = await context.Dialogs.SelectAsync(
                "Select agent",
                agents,
                agent => FormatAgentChoice(agent, context.Scope.AgentId),
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!selected.IsSubmitted || selected.Value is not { } agent)
        {
            return;
        }

        PageState.SelectAgent(agent.Id);
        context.Navigation.GoToPage(AgentDetailPageId);
    }

    private static async Task SwitchAgentAsync(
        AgentTuiCommandContext context,
        IAgentTuiAgentRuntime runtime,
        IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            ConsoleCommandSurface.Show(
                context,
                "Agent commands",
                "`/agents switch <agentId> [sessionId] [threadId]`",
                TranscriptSeverity.Warning);
            return;
        }

        if (!runtime.CanSwitchAgents)
        {
            AppendNotice(
                context,
                "This runtime cannot switch agents in place. Start the direct/coding mode with a different `--agent` value, or use server mode.",
                TranscriptSeverity.Warning);
            return;
        }

        var agentId = args[1];
        var agent = await runtime.GetAgentAsync(agentId).ConfigureAwait(false);
        if (agent is null)
        {
            AppendNotice(context, $"Agent `{agentId}` was not found.", TranscriptSeverity.Warning);
            return;
        }

        await context.SwitchScopeAsync(
                context.Scope with
                {
                    AgentId = agentId,
                    SessionId = args.Count >= 3 ? args[2] : context.Scope.SessionId,
                    ThreadId = args.Count >= 4 ? args[3] : context.Scope.ThreadId
                },
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async Task DeleteAgentAsync(
        AgentTuiCommandContext context,
        IAgentTuiAgentRuntime runtime,
        IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            ConsoleCommandSurface.Show(
                context,
                "Agent commands",
                "`/agents delete <agentId>`",
                TranscriptSeverity.Warning);
            return;
        }

        if (string.Equals(args[1], context.Scope.AgentId, StringComparison.Ordinal))
        {
            AppendNotice(context, "Cannot delete the currently active agent.", TranscriptSeverity.Warning);
            return;
        }

        await runtime.DeleteAgentAsync(args[1]).ConfigureAwait(false);
        AppendNotice(context, $"Deleted agent `{args[1]}`.");
    }

    private static void AppendNotice(
        AgentTuiCommandContext context,
        string message,
        TranscriptSeverity severity = TranscriptSeverity.Info,
        string? entryKey = null)
        => AppendOrUpdate(context, new TranscriptEntry(
                Id: $"command-{Guid.NewGuid():N}",
                EntryKey: entryKey,
                Cell: new NoticeCell(message, Severity: severity),
                Metadata: Metadata(context)));

    private static void AppendNotice(
        AgentTuiCommandContext context,
        string title,
        string markdown,
        string? entryKey = null)
        => AppendOrUpdate(context, new TranscriptEntry(
                Id: $"command-{Guid.NewGuid():N}",
                EntryKey: entryKey,
                Cell: new NoticeCell(title, HPD.TUI.Content.TextBlock.Create(markdown)),
                Metadata: Metadata(context)));

    private static void AppendOrUpdate(AgentTuiCommandContext context, TranscriptEntry entry)
    {
        if (entry.EntryKey is null)
        {
            context.Shell.Transcript.AddFinal(entry);
            return;
        }

        context.Shell.Transcript.FinalizeLive(entry.EntryKey!, entry.AsFinal(), CommittedHistoryMutationPolicy.Reject);
    }

    private static TranscriptEntryMetadata Metadata(AgentTuiCommandContext context)
        => new(
            AgentId: context.Scope.AgentId,
            AgentName: "tui",
            AgentChain: ["tui"]);

    private static IReadOnlyList<string> SplitArgs(string arguments)
        => arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string EscapeMarkdown(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal);

    private static string FormatTime(DateTimeOffset value)
        => value == DateTimeOffset.MinValue ? "" : value.LocalDateTime.ToString("g");

    private static string FormatAgentChoice(AgentTuiAgentInfo agent, string currentAgentId)
    {
        var current = string.Equals(agent.Id, currentAgentId, StringComparison.Ordinal) ? " current" : "";
        return $"{agent.Id} - {agent.Name}{current}";
    }

    private static IComponent RenderAgentsPage(AgentTuiPageContext context)
    {
        var snapshot = PageState.Snapshot();
        var markdown = new StringBuilder();
        markdown.AppendLine("**Agents**");
        markdown.AppendLine();
        markdown.AppendLine("Use Up/Down to move, Enter to inspect, `s` to switch, Esc to go back.");
        markdown.AppendLine();

        if (snapshot.Agents.Count == 0)
        {
            markdown.AppendLine("No stored agents found.");
            return HPD.TUI.Content.TextBlock.Create(markdown.ToString());
        }

        for (var i = 0; i < snapshot.Agents.Count; i++)
        {
            var agent = snapshot.Agents[i];
            markdown.Append(i == snapshot.SelectedIndex ? "=> " : "   ")
                .Append('`').Append(EscapeMarkdown(agent.Id)).Append("` - ")
                .Append(EscapeMarkdown(agent.Name));
            if (agent.Id == context.Scope.AgentId)
            {
                markdown.Append(" current");
            }

            markdown.AppendLine();
            markdown.Append("    updated: ")
                .AppendLine(EscapeMarkdown(FormatTime(agent.UpdatedAt)));
        }

        return HPD.TUI.Content.TextBlock.Create(markdown.ToString());
    }

    private static bool HandleAgentsPageInput(AgentTuiPageContext context, KeyEvent key)
    {
        switch (key.Key)
        {
            case KeyCode.UpArrow:
                PageState.Move(-1);
                return true;
            case KeyCode.DownArrow:
                PageState.Move(1);
                return true;
            case KeyCode.Home:
                PageState.MoveToStart();
                return true;
            case KeyCode.End:
                PageState.MoveToEnd();
                return true;
            case KeyCode.Enter:
                if (PageState.SelectCurrent() is not null)
                {
                    context.Navigation.GoToPage(AgentDetailPageId);
                }

                return true;
            case KeyCode.Character when IsCharacter(in key, 's'):
                SwitchToSelectedAgent(context);
                return true;
            default:
                return false;
        }
    }

    private static IComponent RenderAgentDetailPage(AgentTuiPageContext context)
    {
        var agent = PageState.Snapshot().SelectedAgent;
        if (agent is null)
        {
            return HPD.TUI.Content.TextBlock.Create("**Agent**\n\nNo agent selected.");
        }

        var markdown = new StringBuilder();
        markdown.AppendLine("**Agent**");
        markdown.AppendLine();
        markdown.Append("- id: `").Append(EscapeMarkdown(agent.Id)).AppendLine("`");
        markdown.Append("- name: ").AppendLine(EscapeMarkdown(agent.Name));
        markdown.Append("- updated: ").AppendLine(EscapeMarkdown(FormatTime(agent.UpdatedAt)));
        markdown.Append("- current: ").AppendLine(agent.Id == context.Scope.AgentId ? "yes" : "no");
        markdown.AppendLine();
        markdown.AppendLine("Actions: `s` switch, Esc back.");
        return HPD.TUI.Content.TextBlock.Create(markdown.ToString());
    }

    private static bool HandleAgentDetailPageInput(AgentTuiPageContext context, KeyEvent key)
    {
        if (key.Key == KeyCode.Character && IsCharacter(in key, 's'))
        {
            SwitchToSelectedAgent(context);
            return true;
        }

        return false;
    }

    private static void SwitchToSelectedAgent(AgentTuiPageContext context)
    {
        if (PageState.Snapshot().SelectedAgent is not { } agent ||
            context.Shell.Runtime is not IAgentTuiAgentRuntime { CanSwitchAgents: true } ||
            context.Shell.SwitchScopeAsync is null)
        {
            return;
        }

        _ = context.Shell.SwitchScopeAsync(
            context.Scope with { AgentId = agent.Id },
            CancellationToken.None);
    }

    private static bool IsCharacter(in KeyEvent key, char value)
        => key.Character.Value == value;

    private static string Usage()
        => """
        Usage:

        - `/agents`
        - `/agents switch <agentId> [sessionId] [threadId]`
        - `/agents delete <agentId>`
        """;

    private sealed class ConsoleAgentPageState
    {
        private readonly object _gate = new();
        private IReadOnlyList<AgentTuiAgentInfo> _agents = [];
        private AgentTuiAgentInfo? _selectedAgent;
        private int _selectedIndex;

        public void SetAgents(IReadOnlyList<AgentTuiAgentInfo> agents, string currentAgentId)
        {
            lock (_gate)
            {
                _agents = agents;
                _selectedIndex = Math.Max(0, agents.ToList().FindIndex(
                    agent => string.Equals(agent.Id, currentAgentId, StringComparison.Ordinal)));
                if (_selectedIndex >= agents.Count)
                {
                    _selectedIndex = Math.Max(0, agents.Count - 1);
                }

                _selectedAgent = agents.Count == 0 ? null : agents[_selectedIndex];
            }
        }

        public void Move(int delta)
        {
            lock (_gate)
            {
                if (_agents.Count == 0)
                {
                    return;
                }

                _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, _agents.Count - 1);
                _selectedAgent = _agents[_selectedIndex];
            }
        }

        public void MoveToStart()
        {
            lock (_gate)
            {
                _selectedIndex = 0;
                _selectedAgent = _agents.Count == 0 ? null : _agents[0];
            }
        }

        public void MoveToEnd()
        {
            lock (_gate)
            {
                _selectedIndex = Math.Max(0, _agents.Count - 1);
                _selectedAgent = _agents.Count == 0 ? null : _agents[_selectedIndex];
            }
        }

        public AgentTuiAgentInfo? SelectCurrent()
        {
            lock (_gate)
            {
                _selectedAgent = _agents.Count == 0 ? null : _agents[_selectedIndex];
                return _selectedAgent;
            }
        }

        public AgentTuiAgentInfo? SelectAgent(string agentId)
        {
            lock (_gate)
            {
                for (var i = 0; i < _agents.Count; i++)
                {
                    if (!string.Equals(_agents[i].Id, agentId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    _selectedIndex = i;
                    _selectedAgent = _agents[i];
                    return _selectedAgent;
                }

                return null;
            }
        }

        public PageSnapshot Snapshot()
        {
            lock (_gate)
            {
                return new PageSnapshot(_agents.ToArray(), _selectedAgent, _selectedIndex);
            }
        }

        public sealed record PageSnapshot(
            IReadOnlyList<AgentTuiAgentInfo> Agents,
            AgentTuiAgentInfo? SelectedAgent,
            int SelectedIndex);
    }
}
