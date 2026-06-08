using System.Text;
using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Console;

internal static class ConsoleSessionBranchCommands
{
    private const string SessionsPageId = "console.sessions";
    private const string SessionDetailPageId = "console.session-detail";
    private const string BranchesPageId = "console.branches";
    private const string BranchDetailPageId = "console.branch-detail";
    private static readonly ConsoleSessionBranchPageState PageState = new();

    public static HpdAgentTuiBuilder AddConsoleSessionBranchCommands(this HpdAgentTuiBuilder tui)
        => tui.AddConsoleCommandSurface()
            .TryAddPage(new HpdAgentTuiPageDescriptor(SessionsPageId, RenderSessionsPage)
            {
                Title = "Sessions",
                Description = "Browse stored sessions.",
                Hidden = true,
                HandleInput = HandleSessionsPageInput
            })
            .TryAddPage(new HpdAgentTuiPageDescriptor(SessionDetailPageId, RenderSessionDetailPage)
            {
                Title = "Session",
                Description = "Inspect the selected session.",
                Hidden = true,
                HandleInput = HandleSessionDetailPageInput
            })
            .TryAddPage(new HpdAgentTuiPageDescriptor(BranchesPageId, RenderBranchesPage)
            {
                Title = "Branches",
                Description = "Browse branches in a session.",
                Hidden = true,
                HandleInput = HandleBranchesPageInput
            })
            .TryAddPage(new HpdAgentTuiPageDescriptor(BranchDetailPageId, RenderBranchDetailPage)
            {
                Title = "Branch",
                Description = "Inspect the selected branch.",
                Hidden = true,
                HandleInput = HandleBranchDetailPageInput
            })
            .TryAddSlashCommand(new HpdAgentTuiCommandDescriptor("sessions", ExecuteSessionsAsync)
            {
                Title = "/sessions",
                Description = "List, create, switch, rename, or delete sessions."
            })
            .TryAddSlashCommand(new HpdAgentTuiCommandDescriptor("branch", ExecuteBranchAsync)
            {
                Title = "/branch",
                Description = "List, create, switch, or delete branches in the current session."
            });

    private static async ValueTask ExecuteSessionsAsync(AgentTuiCommandContext context)
    {
        if (context.Runtime is not IAgentTuiSessionBranchRuntime runtime)
        {
            AppendNotice(context, "Sessions are not supported by this runtime.", TranscriptSeverity.Warning);
            return;
        }

        var args = SplitArgs(context.Arguments);
        var verb = args.Count == 0 ? "list" : args[0];
        switch (verb)
        {
            case "list":
                await ShowSessionsAsync(context, runtime).ConfigureAwait(false);
                break;
            case "switch":
                await SwitchSessionAsync(context, runtime, args).ConfigureAwait(false);
                break;
            case "new":
            case "create":
                await CreateSessionAsync(context, runtime, args).ConfigureAwait(false);
                break;
            case "rename":
                await RenameSessionAsync(context, runtime, args).ConfigureAwait(false);
                break;
            case "delete":
            case "rm":
                await DeleteSessionAsync(context, runtime, args).ConfigureAwait(false);
                break;
            default:
                ConsoleCommandSurface.Show(context, "Session commands", SessionUsage(), TranscriptSeverity.Warning);
                break;
        }
    }

    private static async ValueTask ExecuteBranchAsync(AgentTuiCommandContext context)
    {
        if (context.Runtime is not IAgentTuiSessionBranchRuntime runtime)
        {
            AppendNotice(context, "Branches are not supported by this runtime.", TranscriptSeverity.Warning);
            return;
        }

        var args = SplitArgs(context.Arguments);
        var verb = args.Count == 0 ? "list" : args[0];
        switch (verb)
        {
            case "list":
                await ShowBranchesAsync(context, runtime).ConfigureAwait(false);
                break;
            case "switch":
                await SwitchBranchAsync(context, runtime, args).ConfigureAwait(false);
                break;
            case "new":
            case "create":
                await CreateBranchAsync(context, runtime, args).ConfigureAwait(false);
                break;
            case "delete":
            case "rm":
                await DeleteBranchAsync(context, runtime, args).ConfigureAwait(false);
                break;
            case "tree":
                await ShowBranchTreeAsync(context, runtime).ConfigureAwait(false);
                break;
            default:
                ConsoleCommandSurface.Show(context, "Branch commands", BranchUsage(), TranscriptSeverity.Warning);
                break;
        }
    }

    private static async Task ShowSessionsAsync(
        AgentTuiCommandContext context,
        IAgentTuiSessionBranchRuntime runtime)
    {
        var sessions = await runtime.ListSessionsAsync().ConfigureAwait(false);
        PageState.SetSessions(sessions, context.Scope.SessionId);
        var choices = new List<SessionDialogChoice>
        {
            SessionDialogChoice.Create()
        };
        choices.AddRange(sessions.Select(SessionDialogChoice.ForSession));

        var selected = await context.Dialogs.SelectAsync(
                "Select session",
                choices,
                choice => FormatSessionChoice(choice, context.Scope.SessionId),
                CancellationToken.None)
            .ConfigureAwait(false);
        if (selected is null)
        {
            return;
        }

        if (selected.Kind == SessionDialogChoiceKind.Create)
        {
            await CreateUntitledSessionAsync(context, runtime).ConfigureAwait(false);
            return;
        }

        if (selected.Session is { } session)
        {
            PageState.SelectSession(session.Id);
            context.Navigation.GoToPage(SessionDetailPageId);
        }
    }

    private static async Task SwitchSessionAsync(
        AgentTuiCommandContext context,
        IAgentTuiSessionBranchRuntime runtime,
        IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            ConsoleCommandSurface.Show(
                context,
                "Session commands",
                "`/sessions switch <sessionId> [branchId]`",
                TranscriptSeverity.Warning);
            return;
        }

        var sessionId = args[1];
        var session = await runtime.GetSessionAsync(sessionId).ConfigureAwait(false);
        if (session is null)
        {
            AppendNotice(context, $"Session `{sessionId}` was not found.", TranscriptSeverity.Warning);
            return;
        }

        var branchId = args.Count >= 3 ? args[2] : "main";
        await context.SwitchScopeAsync(
                context.Scope with { SessionId = sessionId, BranchId = branchId },
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async Task CreateSessionAsync(
        AgentTuiCommandContext context,
        IAgentTuiSessionBranchRuntime runtime,
        IReadOnlyList<string> args)
    {
        if (args.Count == 1)
        {
            await CreateUntitledSessionAsync(context, runtime).ConfigureAwait(false);
            return;
        }

        var title = string.Join(' ', args.Skip(1));
        var session = await runtime.CreateSessionAsync(sessionId: null, title).ConfigureAwait(false);
        await context.SwitchScopeAsync(
                context.Scope with { SessionId = session.Id, BranchId = "main" },
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async Task RenameSessionAsync(
        AgentTuiCommandContext context,
        IAgentTuiSessionBranchRuntime runtime,
        IReadOnlyList<string> args)
    {
        if (args.Count < 3)
        {
            ConsoleCommandSurface.Show(
                context,
                "Session commands",
                "`/sessions rename <sessionId> <title>`",
                TranscriptSeverity.Warning);
            return;
        }

        var title = string.Join(' ', args.Skip(2));
        await runtime.RenameSessionAsync(args[1], title).ConfigureAwait(false);
        AppendNotice(context, $"Renamed session `{args[1]}`.");
    }

    private static async Task DeleteSessionAsync(
        AgentTuiCommandContext context,
        IAgentTuiSessionBranchRuntime runtime,
        IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            ConsoleCommandSurface.Show(
                context,
                "Session commands",
                "`/sessions delete <sessionId>`",
                TranscriptSeverity.Warning);
            return;
        }

        await runtime.DeleteSessionAsync(args[1]).ConfigureAwait(false);
        AppendNotice(context, $"Deleted session `{args[1]}`.");
    }

    private static async Task ShowBranchesAsync(
        AgentTuiCommandContext context,
        IAgentTuiSessionBranchRuntime runtime)
    {
        var branches = await runtime.ListBranchesAsync(context.Scope.SessionId).ConfigureAwait(false);
        PageState.SetBranches(context.Scope.SessionId, branches, context.Scope.BranchId);
        var selected = await context.Dialogs.SelectAsync(
                "Select branch",
                branches,
                branch => FormatBranchChoice(branch, context.Scope),
                CancellationToken.None)
            .ConfigureAwait(false);
        if (selected is null)
        {
            return;
        }

        PageState.SelectBranch(selected.Id);
        context.Navigation.GoToPage(BranchDetailPageId);
    }

    private static async Task SwitchBranchAsync(
        AgentTuiCommandContext context,
        IAgentTuiSessionBranchRuntime runtime,
        IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            ConsoleCommandSurface.Show(
                context,
                "Branch commands",
                "`/branch switch <branchId>`",
                TranscriptSeverity.Warning);
            return;
        }

        var branches = await runtime.ListBranchesAsync(context.Scope.SessionId).ConfigureAwait(false);
        if (!branches.Any(branch => string.Equals(branch.Id, args[1], StringComparison.Ordinal)))
        {
            AppendNotice(context, $"Branch `{args[1]}` was not found.", TranscriptSeverity.Warning);
            return;
        }

        await context.SwitchScopeAsync(
                context.Scope with { BranchId = args[1] },
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async Task CreateBranchAsync(
        AgentTuiCommandContext context,
        IAgentTuiSessionBranchRuntime runtime,
        IReadOnlyList<string> args)
    {
        var branchId = args.Count >= 2 ? args[1] : null;
        var name = args.Count >= 3 ? string.Join(' ', args.Skip(2)) : null;
        var branch = await runtime.CreateBranchAsync(
                context.Scope.AgentId,
                context.Scope.SessionId,
                branchId,
                name)
            .ConfigureAwait(false);
        AppendNotice(context, $"Created branch `{branch.Id}`.");
    }

    private static async Task DeleteBranchAsync(
        AgentTuiCommandContext context,
        IAgentTuiSessionBranchRuntime runtime,
        IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            ConsoleCommandSurface.Show(
                context,
                "Branch commands",
                "`/branch delete <branchId> [recursive]`",
                TranscriptSeverity.Warning);
            return;
        }

        var recursive = args.Count >= 3 &&
            string.Equals(args[2], "recursive", StringComparison.OrdinalIgnoreCase);
        await runtime.DeleteBranchAsync(context.Scope.SessionId, args[1], recursive).ConfigureAwait(false);
        AppendNotice(context, $"Deleted branch `{args[1]}`.");
    }

    private static async Task ShowBranchTreeAsync(
        AgentTuiCommandContext context,
        IAgentTuiSessionBranchRuntime runtime)
    {
        var branches = await runtime.ListBranchesAsync(context.Scope.SessionId).ConfigureAwait(false);
        if (branches.Count == 0)
        {
            ConsoleCommandSurface.Show(context, "Branches", "No branches found.");
            return;
        }

        var markdown = new StringBuilder();
        markdown.AppendLine("**Branch tree**");
        markdown.AppendLine();
        foreach (var branch in branches.OrderBy(static branch => branch.ForkedFrom is null ? "" : branch.ForkedFrom)
                     .ThenBy(static branch => branch.Id, StringComparer.Ordinal))
        {
            var marker = branch.Id == context.Scope.BranchId ? "*" : "-";
            var parent = branch.ForkedFrom is null ? "root" : $"from `{branch.ForkedFrom}`";
            markdown.Append(marker)
                .Append(" `").Append(EscapeMarkdown(branch.Id)).Append("` ")
                .Append(EscapeMarkdown(branch.Name))
                .Append(" (").Append(parent).Append(')')
                .AppendLine();
        }

        ConsoleCommandSurface.Show(context, "Branches", markdown.ToString());
    }

    private static IComponent RenderSessionsPage(AgentTuiPageContext context)
    {
        var snapshot = PageState.Snapshot();
        var markdown = new StringBuilder();
        markdown.AppendLine("**Sessions**");
        markdown.AppendLine();
        markdown.AppendLine("Use Up/Down to move, Enter to inspect, `s` to switch, `b` for branches, Esc to go back.");
        markdown.AppendLine();

        if (snapshot.Sessions.Count == 0)
        {
            markdown.AppendLine("No sessions found.");
            return new Markdown(markdown.ToString());
        }

        for (var i = 0; i < snapshot.Sessions.Count; i++)
        {
            var session = snapshot.Sessions[i];
            markdown.Append(i == snapshot.SelectedSessionIndex ? "=> " : "   ")
                .Append('`').Append(EscapeMarkdown(session.Id)).Append('`');
            if (!string.IsNullOrWhiteSpace(session.Title))
            {
                markdown.Append(" - ").Append(EscapeMarkdown(session.Title));
            }

            if (session.Id == context.Scope.SessionId)
            {
                markdown.Append(" current");
            }

            markdown.AppendLine();
            markdown.Append("    last activity: ")
                .AppendLine(EscapeMarkdown(FormatTime(session.LastActivity)));
        }

        return new Markdown(markdown.ToString());
    }

    private static bool HandleSessionsPageInput(AgentTuiPageContext context, KeyEvent key)
    {
        switch (key.Key)
        {
            case KeyCode.UpArrow:
                PageState.MoveSession(-1);
                return true;
            case KeyCode.DownArrow:
                PageState.MoveSession(1);
                return true;
            case KeyCode.Home:
                PageState.MoveSessionToStart();
                return true;
            case KeyCode.End:
                PageState.MoveSessionToEnd();
                return true;
            case KeyCode.Enter:
                if (PageState.SelectCurrentSession() is not null)
                {
                    context.Navigation.GoToPage(SessionDetailPageId);
                }

                return true;
            case KeyCode.Character when IsCharacter(in key, 's'):
                SwitchToSelectedSession(context);
                return true;
            case KeyCode.Character when IsCharacter(in key, 'b'):
                _ = ShowBranchesForSelectedSessionAsync(context);
                return true;
            default:
                return false;
        }
    }

    private static IComponent RenderSessionDetailPage(AgentTuiPageContext context)
    {
        var snapshot = PageState.Snapshot();
        var session = snapshot.SelectedSession;
        if (session is null)
        {
            return new Markdown("**Session**\n\nNo session selected.");
        }

        var markdown = new StringBuilder();
        markdown.AppendLine("**Session**");
        markdown.AppendLine();
        markdown.Append("- id: `").Append(EscapeMarkdown(session.Id)).AppendLine("`");
        markdown.Append("- title: ").AppendLine(EscapeMarkdown(session.Title ?? ""));
        markdown.Append("- created: ").AppendLine(EscapeMarkdown(FormatTime(session.CreatedAt)));
        markdown.Append("- last activity: ").AppendLine(EscapeMarkdown(FormatTime(session.LastActivity)));
        markdown.Append("- current: ").AppendLine(session.Id == context.Scope.SessionId ? "yes" : "no");
        markdown.AppendLine();
        markdown.AppendLine("Actions: `s` switch to session, `b` browse branches, Esc back.");
        return new Markdown(markdown.ToString());
    }

    private static bool HandleSessionDetailPageInput(AgentTuiPageContext context, KeyEvent key)
    {
        switch (key.Key)
        {
            case KeyCode.Character when IsCharacter(in key, 's'):
                SwitchToSelectedSession(context);
                return true;
            case KeyCode.Character when IsCharacter(in key, 'b'):
                _ = ShowBranchesForSelectedSessionAsync(context);
                return true;
            default:
                return false;
        }
    }

    private static IComponent RenderBranchesPage(AgentTuiPageContext context)
    {
        var snapshot = PageState.Snapshot();
        var markdown = new StringBuilder();
        markdown.Append("**Branches");
        if (!string.IsNullOrWhiteSpace(snapshot.BranchSessionId))
        {
            markdown.Append(" for `").Append(EscapeMarkdown(snapshot.BranchSessionId)).Append('`');
        }

        markdown.AppendLine("**");
        markdown.AppendLine();
        markdown.AppendLine("Use Up/Down to move, Enter to inspect, `s` to switch, Esc to go back.");
        markdown.AppendLine();

        if (snapshot.Branches.Count == 0)
        {
            markdown.AppendLine("No branches found.");
            return new Markdown(markdown.ToString());
        }

        for (var i = 0; i < snapshot.Branches.Count; i++)
        {
            var branch = snapshot.Branches[i];
            markdown.Append(i == snapshot.SelectedBranchIndex ? "=> " : "   ")
                .Append('`').Append(EscapeMarkdown(branch.Id)).Append("` - ")
                .Append(EscapeMarkdown(branch.Name));
            if (branch.Id == context.Scope.BranchId && branch.SessionId == context.Scope.SessionId)
            {
                markdown.Append(" current");
            }

            markdown.AppendLine();
            markdown.Append("    messages: ")
                .Append(branch.MessageCount)
                .Append("  forks: ")
                .Append(branch.TotalForks);
            if (!string.IsNullOrWhiteSpace(branch.ForkedFrom))
            {
                markdown.Append("  forked from: `").Append(EscapeMarkdown(branch.ForkedFrom)).Append('`');
            }

            markdown.AppendLine();
        }

        return new Markdown(markdown.ToString());
    }

    private static bool HandleBranchesPageInput(AgentTuiPageContext context, KeyEvent key)
    {
        switch (key.Key)
        {
            case KeyCode.UpArrow:
                PageState.MoveBranch(-1);
                return true;
            case KeyCode.DownArrow:
                PageState.MoveBranch(1);
                return true;
            case KeyCode.Home:
                PageState.MoveBranchToStart();
                return true;
            case KeyCode.End:
                PageState.MoveBranchToEnd();
                return true;
            case KeyCode.Enter:
                if (PageState.SelectCurrentBranch() is not null)
                {
                    context.Navigation.GoToPage(BranchDetailPageId);
                }

                return true;
            case KeyCode.Character when IsCharacter(in key, 's'):
                SwitchToSelectedBranch(context);
                return true;
            default:
                return false;
        }
    }

    private static IComponent RenderBranchDetailPage(AgentTuiPageContext context)
    {
        var snapshot = PageState.Snapshot();
        var branch = snapshot.SelectedBranch;
        if (branch is null)
        {
            return new Markdown("**Branch**\n\nNo branch selected.");
        }

        var markdown = new StringBuilder();
        markdown.AppendLine("**Branch**");
        markdown.AppendLine();
        markdown.Append("- session: `").Append(EscapeMarkdown(branch.SessionId)).AppendLine("`");
        markdown.Append("- id: `").Append(EscapeMarkdown(branch.Id)).AppendLine("`");
        markdown.Append("- name: ").AppendLine(EscapeMarkdown(branch.Name));
        markdown.Append("- description: ").AppendLine(EscapeMarkdown(branch.Description ?? ""));
        markdown.Append("- messages: ").AppendLine(branch.MessageCount.ToString());
        markdown.Append("- forks: ").AppendLine(branch.TotalForks.ToString());
        markdown.Append("- forked from: ").AppendLine(branch.ForkedFrom is null ? "" : $"`{EscapeMarkdown(branch.ForkedFrom)}`");
        markdown.Append("- previous sibling: ").AppendLine(branch.PreviousSiblingId is null ? "" : $"`{EscapeMarkdown(branch.PreviousSiblingId)}`");
        markdown.Append("- next sibling: ").AppendLine(branch.NextSiblingId is null ? "" : $"`{EscapeMarkdown(branch.NextSiblingId)}`");
        markdown.Append("- current: ").AppendLine(branch.Id == context.Scope.BranchId && branch.SessionId == context.Scope.SessionId ? "yes" : "no");
        markdown.AppendLine();
        markdown.AppendLine("Actions: `s` switch, `p` previous sibling, `n` next sibling, Esc back.");
        return new Markdown(markdown.ToString());
    }

    private static bool HandleBranchDetailPageInput(AgentTuiPageContext context, KeyEvent key)
    {
        switch (key.Key)
        {
            case KeyCode.Character when IsCharacter(in key, 's'):
                SwitchToSelectedBranch(context);
                return true;
            case KeyCode.Character when IsCharacter(in key, 'p'):
                PageState.SelectPreviousSiblingBranch();
                return true;
            case KeyCode.Character when IsCharacter(in key, 'n'):
                PageState.SelectNextSiblingBranch();
                return true;
            default:
                return false;
        }
    }

    private static void SwitchToSelectedSession(AgentTuiPageContext context)
    {
        if (PageState.Snapshot().SelectedSession is not { } session ||
            context.Shell.SwitchScopeAsync is null)
        {
            return;
        }

        _ = context.Shell.SwitchScopeAsync(
            context.Scope with { SessionId = session.Id, BranchId = "main" },
            CancellationToken.None);
    }

    private static void SwitchToSelectedBranch(AgentTuiPageContext context)
    {
        if (PageState.Snapshot().SelectedBranch is not { } branch ||
            context.Shell.SwitchScopeAsync is null)
        {
            return;
        }

        _ = context.Shell.SwitchScopeAsync(
            context.Scope with { SessionId = branch.SessionId, BranchId = branch.Id },
            CancellationToken.None);
    }

    private static async Task ShowBranchesForSelectedSessionAsync(AgentTuiPageContext context)
    {
        var snapshot = PageState.Snapshot();
        var session = snapshot.SelectedSession;
        if (session is null || context.Shell.Runtime is not IAgentTuiSessionBranchRuntime runtime)
        {
            return;
        }

        var branches = await runtime.ListBranchesAsync(session.Id).ConfigureAwait(false);
        PageState.SetBranches(session.Id, branches, context.Scope.BranchId);
        context.Navigation.GoToPage(BranchesPageId);
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
                Cell: new NoticeCell(title, new Markdown(markdown)),
                Metadata: Metadata(context)));

    private static void AppendOrUpdate(AgentTuiCommandContext context, TranscriptEntry entry)
    {
        if (entry.EntryKey is null)
        {
            context.Shell.Transcript.Append(entry);
            return;
        }

        context.Shell.Transcript.Update(entry);
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

    private static string FormatSessionChoice(SessionDialogChoice choice, string currentSessionId)
    {
        if (choice.Kind == SessionDialogChoiceKind.Create)
        {
            return "Create new session";
        }

        return choice.Session is null
            ? ""
            : FormatSessionChoice(choice.Session, currentSessionId);
    }

    private static string FormatSessionChoice(AgentTuiSessionInfo session, string currentSessionId)
    {
        var title = string.IsNullOrWhiteSpace(session.Title) ? "" : $" - {session.Title}";
        var current = string.Equals(session.Id, currentSessionId, StringComparison.Ordinal) ? " current" : "";
        return $"{session.Id}{title}{current}";
    }

    private static async Task CreateUntitledSessionAsync(
        AgentTuiCommandContext context,
        IAgentTuiSessionBranchRuntime runtime)
    {
        var session = await runtime.CreateSessionAsync(sessionId: null, title: null)
            .ConfigureAwait(false);
        await context.SwitchScopeAsync(
                context.Scope with { SessionId = session.Id, BranchId = "main" },
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static string FormatBranchChoice(AgentTuiBranchInfo branch, AgentTuiRuntimeScope scope)
    {
        var current = string.Equals(branch.SessionId, scope.SessionId, StringComparison.Ordinal) &&
            string.Equals(branch.Id, scope.BranchId, StringComparison.Ordinal)
                ? " current"
                : "";
        return $"{branch.Id} - {branch.Name}{current}";
    }

    private static bool IsCharacter(in KeyEvent key, char value)
        => key.Character.Value == value;

    private static string SessionUsage()
        => """
        Usage:

        - `/sessions`
        - `/sessions switch <sessionId> [branchId]`
        - `/sessions new [title]`
        - `/sessions rename <sessionId> <title>`
        - `/sessions delete <sessionId>`
        """;

    private static string BranchUsage()
        => """
        Usage:

        - `/branch`
        - `/branch switch <branchId>`
        - `/branch new [branchId] [name]`
        - `/branch delete <branchId> [recursive]`
        - `/branch tree`
        """;

    private sealed record SessionDialogChoice(
        SessionDialogChoiceKind Kind,
        AgentTuiSessionInfo? Session)
    {
        public static SessionDialogChoice Create()
            => new(SessionDialogChoiceKind.Create, null);

        public static SessionDialogChoice ForSession(AgentTuiSessionInfo session)
            => new(SessionDialogChoiceKind.Session, session);
    }

    private enum SessionDialogChoiceKind
    {
        Create,
        Session
    }

    private sealed class ConsoleSessionBranchPageState
    {
        private readonly object _gate = new();
        private IReadOnlyList<AgentTuiSessionInfo> _sessions = [];
        private IReadOnlyList<AgentTuiBranchInfo> _branches = [];
        private AgentTuiSessionInfo? _selectedSession;
        private AgentTuiBranchInfo? _selectedBranch;
        private string? _branchSessionId;
        private int _selectedSessionIndex;
        private int _selectedBranchIndex;

        public void SetSessions(IReadOnlyList<AgentTuiSessionInfo> sessions, string currentSessionId)
        {
            lock (_gate)
            {
                _sessions = sessions;
                _selectedSessionIndex = Math.Max(0, sessions.ToList().FindIndex(
                    session => string.Equals(session.Id, currentSessionId, StringComparison.Ordinal)));
                if (_selectedSessionIndex >= sessions.Count)
                {
                    _selectedSessionIndex = Math.Max(0, sessions.Count - 1);
                }

                _selectedSession = sessions.Count == 0 ? null : sessions[_selectedSessionIndex];
            }
        }

        public void SetBranches(
            string sessionId,
            IReadOnlyList<AgentTuiBranchInfo> branches,
            string currentBranchId)
        {
            lock (_gate)
            {
                _branchSessionId = sessionId;
                _branches = branches;
                _selectedBranchIndex = Math.Max(0, branches.ToList().FindIndex(
                    branch => string.Equals(branch.Id, currentBranchId, StringComparison.Ordinal)));
                if (_selectedBranchIndex >= branches.Count)
                {
                    _selectedBranchIndex = Math.Max(0, branches.Count - 1);
                }

                _selectedBranch = branches.Count == 0 ? null : branches[_selectedBranchIndex];
            }
        }

        public void MoveSession(int delta)
        {
            lock (_gate)
            {
                if (_sessions.Count == 0)
                {
                    return;
                }

                _selectedSessionIndex = Math.Clamp(_selectedSessionIndex + delta, 0, _sessions.Count - 1);
                _selectedSession = _sessions[_selectedSessionIndex];
            }
        }

        public void MoveBranch(int delta)
        {
            lock (_gate)
            {
                if (_branches.Count == 0)
                {
                    return;
                }

                _selectedBranchIndex = Math.Clamp(_selectedBranchIndex + delta, 0, _branches.Count - 1);
                _selectedBranch = _branches[_selectedBranchIndex];
            }
        }

        public void MoveSessionToStart()
        {
            lock (_gate)
            {
                _selectedSessionIndex = 0;
                _selectedSession = _sessions.Count == 0 ? null : _sessions[0];
            }
        }

        public void MoveSessionToEnd()
        {
            lock (_gate)
            {
                _selectedSessionIndex = Math.Max(0, _sessions.Count - 1);
                _selectedSession = _sessions.Count == 0 ? null : _sessions[_selectedSessionIndex];
            }
        }

        public void MoveBranchToStart()
        {
            lock (_gate)
            {
                _selectedBranchIndex = 0;
                _selectedBranch = _branches.Count == 0 ? null : _branches[0];
            }
        }

        public void MoveBranchToEnd()
        {
            lock (_gate)
            {
                _selectedBranchIndex = Math.Max(0, _branches.Count - 1);
                _selectedBranch = _branches.Count == 0 ? null : _branches[_selectedBranchIndex];
            }
        }

        public AgentTuiSessionInfo? SelectCurrentSession()
        {
            lock (_gate)
            {
                _selectedSession = _sessions.Count == 0 ? null : _sessions[_selectedSessionIndex];
                return _selectedSession;
            }
        }

        public AgentTuiSessionInfo? SelectSession(string sessionId)
        {
            lock (_gate)
            {
                for (var i = 0; i < _sessions.Count; i++)
                {
                    if (!string.Equals(_sessions[i].Id, sessionId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    _selectedSessionIndex = i;
                    _selectedSession = _sessions[i];
                    return _selectedSession;
                }

                return null;
            }
        }

        public AgentTuiBranchInfo? SelectCurrentBranch()
        {
            lock (_gate)
            {
                _selectedBranch = _branches.Count == 0 ? null : _branches[_selectedBranchIndex];
                return _selectedBranch;
            }
        }

        public AgentTuiBranchInfo? SelectBranch(string branchId)
        {
            lock (_gate)
            {
                for (var i = 0; i < _branches.Count; i++)
                {
                    if (!string.Equals(_branches[i].Id, branchId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    _selectedBranchIndex = i;
                    _selectedBranch = _branches[i];
                    return _selectedBranch;
                }

                return null;
            }
        }

        public void SelectPreviousSiblingBranch()
        {
            lock (_gate)
            {
                if (_selectedBranch?.PreviousSiblingId is { } previous)
                {
                    SelectBranchById(previous);
                }
            }
        }

        public void SelectNextSiblingBranch()
        {
            lock (_gate)
            {
                if (_selectedBranch?.NextSiblingId is { } next)
                {
                    SelectBranchById(next);
                }
            }
        }

        public PageSnapshot Snapshot()
        {
            lock (_gate)
            {
                return new PageSnapshot(
                    _sessions.ToArray(),
                    _branches.ToArray(),
                    _selectedSession,
                    _selectedBranch,
                    _branchSessionId,
                    _selectedSessionIndex,
                    _selectedBranchIndex);
            }
        }

        private void SelectBranchById(string branchId)
        {
            for (var i = 0; i < _branches.Count; i++)
            {
                if (!string.Equals(_branches[i].Id, branchId, StringComparison.Ordinal))
                {
                    continue;
                }

                _selectedBranchIndex = i;
                _selectedBranch = _branches[i];
                return;
            }
        }

        public sealed record PageSnapshot(
            IReadOnlyList<AgentTuiSessionInfo> Sessions,
            IReadOnlyList<AgentTuiBranchInfo> Branches,
            AgentTuiSessionInfo? SelectedSession,
            AgentTuiBranchInfo? SelectedBranch,
            string? BranchSessionId,
            int SelectedSessionIndex,
            int SelectedBranchIndex);
    }
}
