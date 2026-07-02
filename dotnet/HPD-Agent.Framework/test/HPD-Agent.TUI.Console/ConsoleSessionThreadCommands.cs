using System.Text;
using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Console;

internal static class ConsoleSessionThreadCommands
{
    private const string SessionsPageId = "console.sessions";
    private const string SessionDetailPageId = "console.session-detail";
    private const string ThreadsPageId = "console.threads";
    private const string ThreadDetailPageId = "console.thread-detail";
    private static readonly ConsoleSessionThreadPageState PageState = new();

    public static HpdAgentTuiBuilder AddConsoleSessionThreadCommands(this HpdAgentTuiBuilder tui)
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
            .TryAddPage(new HpdAgentTuiPageDescriptor(ThreadsPageId, RenderThreadsPage)
            {
                Title = "Threads",
                Description = "Browse threads in a session.",
                Hidden = true,
                HandleInput = HandleThreadsPageInput
            })
            .TryAddPage(new HpdAgentTuiPageDescriptor(ThreadDetailPageId, RenderThreadDetailPage)
            {
                Title = "Thread",
                Description = "Inspect the selected thread.",
                Hidden = true,
                HandleInput = HandleThreadDetailPageInput
            })
            .TryAddSlashCommand(new HpdAgentTuiCommandDescriptor("sessions", ExecuteSessionsAsync)
            {
                Title = "/sessions",
                Description = "List, create, switch, rename, or delete sessions."
            })
            .TryAddSlashCommand(new HpdAgentTuiCommandDescriptor("thread", ExecuteThreadAsync)
            {
                Title = "/thread",
                Description = "List, create, switch, or delete threads in the current session."
            });

    private static async ValueTask ExecuteSessionsAsync(AgentTuiCommandContext context)
    {
        if (context.Runtime is not IAgentTuiSessionThreadRuntime runtime)
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

    private static async ValueTask ExecuteThreadAsync(AgentTuiCommandContext context)
    {
        if (context.Runtime is not IAgentTuiSessionThreadRuntime runtime)
        {
            AppendNotice(context, "Threads are not supported by this runtime.", TranscriptSeverity.Warning);
            return;
        }

        var args = SplitArgs(context.Arguments);
        var verb = args.Count == 0 ? "list" : args[0];
        switch (verb)
        {
            case "list":
                await ShowThreadsAsync(context, runtime).ConfigureAwait(false);
                break;
            case "switch":
                await SwitchThreadAsync(context, runtime, args).ConfigureAwait(false);
                break;
            case "new":
            case "create":
                await CreateThreadAsync(context, runtime, args).ConfigureAwait(false);
                break;
            case "delete":
            case "rm":
                await DeleteThreadAsync(context, runtime, args).ConfigureAwait(false);
                break;
            case "tree":
                await ShowThreadTreeAsync(context, runtime).ConfigureAwait(false);
                break;
            default:
                ConsoleCommandSurface.Show(context, "Thread commands", ThreadUsage(), TranscriptSeverity.Warning);
                break;
        }
    }

    private static async Task ShowSessionsAsync(
        AgentTuiCommandContext context,
        IAgentTuiSessionThreadRuntime runtime)
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
        if (!selected.IsSubmitted || selected.Value is not { } choice)
        {
            return;
        }

        if (choice.Kind == SessionDialogChoiceKind.Create)
        {
            await CreateUntitledSessionAsync(context, runtime).ConfigureAwait(false);
            return;
        }

        if (choice.Session is { } session)
        {
            PageState.SelectSession(session.Id);
            context.Navigation.GoToPage(SessionDetailPageId);
        }
    }

    private static async Task SwitchSessionAsync(
        AgentTuiCommandContext context,
        IAgentTuiSessionThreadRuntime runtime,
        IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            ConsoleCommandSurface.Show(
                context,
                "Session commands",
                "`/sessions switch <sessionId> [threadId]`",
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

        var threadId = args.Count >= 3 ? args[2] : "main";
        await context.SwitchScopeAsync(
                context.Scope with { SessionId = sessionId, ThreadId = threadId },
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async Task CreateSessionAsync(
        AgentTuiCommandContext context,
        IAgentTuiSessionThreadRuntime runtime,
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
                context.Scope with { SessionId = session.Id, ThreadId = "main" },
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async Task RenameSessionAsync(
        AgentTuiCommandContext context,
        IAgentTuiSessionThreadRuntime runtime,
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
        IAgentTuiSessionThreadRuntime runtime,
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

    private static async Task ShowThreadsAsync(
        AgentTuiCommandContext context,
        IAgentTuiSessionThreadRuntime runtime)
    {
        var threads = await runtime.ListThreadsAsync(context.Scope.SessionId).ConfigureAwait(false);
        PageState.SetThreads(context.Scope.SessionId, threads, context.Scope.ThreadId);
        var selected = await context.Dialogs.SelectAsync(
                "Select thread",
                threads,
                thread => FormatThreadChoice(thread, context.Scope),
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!selected.IsSubmitted || selected.Value is not { } thread)
        {
            return;
        }

        PageState.SelectThread(thread.Id);
        context.Navigation.GoToPage(ThreadDetailPageId);
    }

    private static async Task SwitchThreadAsync(
        AgentTuiCommandContext context,
        IAgentTuiSessionThreadRuntime runtime,
        IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            ConsoleCommandSurface.Show(
                context,
                "Thread commands",
                "`/thread switch <threadId>`",
                TranscriptSeverity.Warning);
            return;
        }

        var threads = await runtime.ListThreadsAsync(context.Scope.SessionId).ConfigureAwait(false);
        if (!threads.Any(thread => string.Equals(thread.Id, args[1], StringComparison.Ordinal)))
        {
            AppendNotice(context, $"Thread `{args[1]}` was not found.", TranscriptSeverity.Warning);
            return;
        }

        await context.SwitchScopeAsync(
                context.Scope with { ThreadId = args[1] },
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async Task CreateThreadAsync(
        AgentTuiCommandContext context,
        IAgentTuiSessionThreadRuntime runtime,
        IReadOnlyList<string> args)
    {
        var threadId = args.Count >= 2 ? args[1] : null;
        var name = args.Count >= 3 ? string.Join(' ', args.Skip(2)) : null;
        var thread = await runtime.CreateThreadAsync(
                context.Scope.AgentId,
                context.Scope.SessionId,
                threadId,
                name)
            .ConfigureAwait(false);
        AppendNotice(context, $"Created thread `{thread.Id}`.");
    }

    private static async Task DeleteThreadAsync(
        AgentTuiCommandContext context,
        IAgentTuiSessionThreadRuntime runtime,
        IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            ConsoleCommandSurface.Show(
                context,
                "Thread commands",
                "`/thread delete <threadId> [recursive]`",
                TranscriptSeverity.Warning);
            return;
        }

        var recursive = args.Count >= 3 &&
            string.Equals(args[2], "recursive", StringComparison.OrdinalIgnoreCase);
        await runtime.DeleteThreadAsync(context.Scope.SessionId, args[1], recursive).ConfigureAwait(false);
        AppendNotice(context, $"Deleted thread `{args[1]}`.");
    }

    private static async Task ShowThreadTreeAsync(
        AgentTuiCommandContext context,
        IAgentTuiSessionThreadRuntime runtime)
    {
        var threads = await runtime.ListThreadsAsync(context.Scope.SessionId).ConfigureAwait(false);
        if (threads.Count == 0)
        {
            ConsoleCommandSurface.Show(context, "Threads", "No threads found.");
            return;
        }

        var markdown = new StringBuilder();
        markdown.AppendLine("**Thread tree**");
        markdown.AppendLine();
        foreach (var thread in threads.OrderBy(static thread => thread.ForkedFrom is null ? "" : thread.ForkedFrom)
                     .ThenBy(static thread => thread.Id, StringComparer.Ordinal))
        {
            var marker = thread.Id == context.Scope.ThreadId ? "*" : "-";
            var parent = thread.ForkedFrom is null ? "root" : $"from `{thread.ForkedFrom}`";
            markdown.Append(marker)
                .Append(" `").Append(EscapeMarkdown(thread.Id)).Append("` ")
                .Append(EscapeMarkdown(thread.Name))
                .Append(" (").Append(parent).Append(')')
                .AppendLine();
        }

        ConsoleCommandSurface.Show(context, "Threads", markdown.ToString());
    }

    private static IComponent RenderSessionsPage(AgentTuiPageContext context)
    {
        var snapshot = PageState.Snapshot();
        var markdown = new StringBuilder();
        markdown.AppendLine("**Sessions**");
        markdown.AppendLine();
        markdown.AppendLine("Use Up/Down to move, Enter to inspect, `s` to switch, `b` for threads, Esc to go back.");
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
                _ = ShowThreadsForSelectedSessionAsync(context);
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
        markdown.AppendLine("Actions: `s` switch to session, `b` browse threads, Esc back.");
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
                _ = ShowThreadsForSelectedSessionAsync(context);
                return true;
            default:
                return false;
        }
    }

    private static IComponent RenderThreadsPage(AgentTuiPageContext context)
    {
        var snapshot = PageState.Snapshot();
        var markdown = new StringBuilder();
        markdown.Append("**Threads");
        if (!string.IsNullOrWhiteSpace(snapshot.ThreadSessionId))
        {
            markdown.Append(" for `").Append(EscapeMarkdown(snapshot.ThreadSessionId)).Append('`');
        }

        markdown.AppendLine("**");
        markdown.AppendLine();
        markdown.AppendLine("Use Up/Down to move, Enter to inspect, `s` to switch, Esc to go back.");
        markdown.AppendLine();

        if (snapshot.Threads.Count == 0)
        {
            markdown.AppendLine("No threads found.");
            return new Markdown(markdown.ToString());
        }

        for (var i = 0; i < snapshot.Threads.Count; i++)
        {
            var thread = snapshot.Threads[i];
            markdown.Append(i == snapshot.SelectedThreadIndex ? "=> " : "   ")
                .Append('`').Append(EscapeMarkdown(thread.Id)).Append("` - ")
                .Append(EscapeMarkdown(thread.Name));
            if (thread.Id == context.Scope.ThreadId && thread.SessionId == context.Scope.SessionId)
            {
                markdown.Append(" current");
            }

            markdown.AppendLine();
            markdown.Append("    messages: ")
                .Append(thread.MessageCount)
                .Append("  forks: ")
                .Append(thread.TotalForks);
            if (!string.IsNullOrWhiteSpace(thread.ForkedFrom))
            {
                markdown.Append("  forked from: `").Append(EscapeMarkdown(thread.ForkedFrom)).Append('`');
            }

            markdown.AppendLine();
        }

        return new Markdown(markdown.ToString());
    }

    private static bool HandleThreadsPageInput(AgentTuiPageContext context, KeyEvent key)
    {
        switch (key.Key)
        {
            case KeyCode.UpArrow:
                PageState.MoveThread(-1);
                return true;
            case KeyCode.DownArrow:
                PageState.MoveThread(1);
                return true;
            case KeyCode.Home:
                PageState.MoveThreadToStart();
                return true;
            case KeyCode.End:
                PageState.MoveThreadToEnd();
                return true;
            case KeyCode.Enter:
                if (PageState.SelectCurrentThread() is not null)
                {
                    context.Navigation.GoToPage(ThreadDetailPageId);
                }

                return true;
            case KeyCode.Character when IsCharacter(in key, 's'):
                SwitchToSelectedThread(context);
                return true;
            default:
                return false;
        }
    }

    private static IComponent RenderThreadDetailPage(AgentTuiPageContext context)
    {
        var snapshot = PageState.Snapshot();
        var thread = snapshot.SelectedThread;
        if (thread is null)
        {
            return new Markdown("**Thread**\n\nNo thread selected.");
        }

        var markdown = new StringBuilder();
        markdown.AppendLine("**Thread**");
        markdown.AppendLine();
        markdown.Append("- session: `").Append(EscapeMarkdown(thread.SessionId)).AppendLine("`");
        markdown.Append("- id: `").Append(EscapeMarkdown(thread.Id)).AppendLine("`");
        markdown.Append("- name: ").AppendLine(EscapeMarkdown(thread.Name));
        markdown.Append("- description: ").AppendLine(EscapeMarkdown(thread.Description ?? ""));
        markdown.Append("- messages: ").AppendLine(thread.MessageCount.ToString());
        markdown.Append("- forks: ").AppendLine(thread.TotalForks.ToString());
        markdown.Append("- forked from: ").AppendLine(thread.ForkedFrom is null ? "" : $"`{EscapeMarkdown(thread.ForkedFrom)}`");
        markdown.Append("- forked at message: ").AppendLine(thread.ForkedAtMessageId is null ? "" : $"`{EscapeMarkdown(thread.ForkedAtMessageId)}`");
        markdown.Append("- current: ").AppendLine(thread.Id == context.Scope.ThreadId && thread.SessionId == context.Scope.SessionId ? "yes" : "no");
        markdown.AppendLine();
        markdown.AppendLine("Actions: `s` switch, Esc back.");
        return new Markdown(markdown.ToString());
    }

    private static bool HandleThreadDetailPageInput(AgentTuiPageContext context, KeyEvent key)
    {
        switch (key.Key)
        {
            case KeyCode.Character when IsCharacter(in key, 's'):
                SwitchToSelectedThread(context);
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
            context.Scope with { SessionId = session.Id, ThreadId = "main" },
            CancellationToken.None);
    }

    private static void SwitchToSelectedThread(AgentTuiPageContext context)
    {
        if (PageState.Snapshot().SelectedThread is not { } thread ||
            context.Shell.SwitchScopeAsync is null)
        {
            return;
        }

        _ = context.Shell.SwitchScopeAsync(
            context.Scope with { SessionId = thread.SessionId, ThreadId = thread.Id },
            CancellationToken.None);
    }

    private static async Task ShowThreadsForSelectedSessionAsync(AgentTuiPageContext context)
    {
        var snapshot = PageState.Snapshot();
        var session = snapshot.SelectedSession;
        if (session is null || context.Shell.Runtime is not IAgentTuiSessionThreadRuntime runtime)
        {
            return;
        }

        var threads = await runtime.ListThreadsAsync(session.Id).ConfigureAwait(false);
        PageState.SetThreads(session.Id, threads, context.Scope.ThreadId);
        context.Navigation.GoToPage(ThreadsPageId);
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
            context.Shell.Transcript.AddFinal(entry);
            return;
        }

        context.Shell.Transcript.FinalizeLive(entry.EntryKey!, entry.AsFinal());
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
        IAgentTuiSessionThreadRuntime runtime)
    {
        var session = await runtime.CreateSessionAsync(sessionId: null, title: null)
            .ConfigureAwait(false);
        await context.SwitchScopeAsync(
                context.Scope with { SessionId = session.Id, ThreadId = "main" },
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static string FormatThreadChoice(AgentTuiThreadInfo thread, AgentTuiRuntimeScope scope)
    {
        var current = string.Equals(thread.SessionId, scope.SessionId, StringComparison.Ordinal) &&
            string.Equals(thread.Id, scope.ThreadId, StringComparison.Ordinal)
                ? " current"
                : "";
        return $"{thread.Id} - {thread.Name}{current}";
    }

    private static bool IsCharacter(in KeyEvent key, char value)
        => key.Character.Value == value;

    private static string SessionUsage()
        => """
        Usage:

        - `/sessions`
        - `/sessions switch <sessionId> [threadId]`
        - `/sessions new [title]`
        - `/sessions rename <sessionId> <title>`
        - `/sessions delete <sessionId>`
        """;

    private static string ThreadUsage()
        => """
        Usage:

        - `/thread`
        - `/thread switch <threadId>`
        - `/thread new [threadId] [name]`
        - `/thread delete <threadId> [recursive]`
        - `/thread tree`
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

    private sealed class ConsoleSessionThreadPageState
    {
        private readonly object _gate = new();
        private IReadOnlyList<AgentTuiSessionInfo> _sessions = [];
        private IReadOnlyList<AgentTuiThreadInfo> _threads = [];
        private AgentTuiSessionInfo? _selectedSession;
        private AgentTuiThreadInfo? _selectedThread;
        private string? _threadSessionId;
        private int _selectedSessionIndex;
        private int _selectedThreadIndex;

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

        public void SetThreads(
            string sessionId,
            IReadOnlyList<AgentTuiThreadInfo> threads,
            string currentThreadId)
        {
            lock (_gate)
            {
                _threadSessionId = sessionId;
                _threads = threads;
                _selectedThreadIndex = Math.Max(0, threads.ToList().FindIndex(
                    thread => string.Equals(thread.Id, currentThreadId, StringComparison.Ordinal)));
                if (_selectedThreadIndex >= threads.Count)
                {
                    _selectedThreadIndex = Math.Max(0, threads.Count - 1);
                }

                _selectedThread = threads.Count == 0 ? null : threads[_selectedThreadIndex];
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

        public void MoveThread(int delta)
        {
            lock (_gate)
            {
                if (_threads.Count == 0)
                {
                    return;
                }

                _selectedThreadIndex = Math.Clamp(_selectedThreadIndex + delta, 0, _threads.Count - 1);
                _selectedThread = _threads[_selectedThreadIndex];
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

        public void MoveThreadToStart()
        {
            lock (_gate)
            {
                _selectedThreadIndex = 0;
                _selectedThread = _threads.Count == 0 ? null : _threads[0];
            }
        }

        public void MoveThreadToEnd()
        {
            lock (_gate)
            {
                _selectedThreadIndex = Math.Max(0, _threads.Count - 1);
                _selectedThread = _threads.Count == 0 ? null : _threads[_selectedThreadIndex];
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

        public AgentTuiThreadInfo? SelectCurrentThread()
        {
            lock (_gate)
            {
                _selectedThread = _threads.Count == 0 ? null : _threads[_selectedThreadIndex];
                return _selectedThread;
            }
        }

        public AgentTuiThreadInfo? SelectThread(string threadId)
        {
            lock (_gate)
            {
                for (var i = 0; i < _threads.Count; i++)
                {
                    if (!string.Equals(_threads[i].Id, threadId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    _selectedThreadIndex = i;
                    _selectedThread = _threads[i];
                    return _selectedThread;
                }

                return null;
            }
        }

        public PageSnapshot Snapshot()
        {
            lock (_gate)
            {
                return new PageSnapshot(
                    _sessions.ToArray(),
                    _threads.ToArray(),
                    _selectedSession,
                    _selectedThread,
                    _threadSessionId,
                    _selectedSessionIndex,
                    _selectedThreadIndex);
            }
        }

        private void SelectThreadById(string threadId)
        {
            for (var i = 0; i < _threads.Count; i++)
            {
                if (!string.Equals(_threads[i].Id, threadId, StringComparison.Ordinal))
                {
                    continue;
                }

                _selectedThreadIndex = i;
                _selectedThread = _threads[i];
                return;
            }
        }

        public sealed record PageSnapshot(
            IReadOnlyList<AgentTuiSessionInfo> Sessions,
            IReadOnlyList<AgentTuiThreadInfo> Threads,
            AgentTuiSessionInfo? SelectedSession,
            AgentTuiThreadInfo? SelectedThread,
            string? ThreadSessionId,
            int SelectedSessionIndex,
            int SelectedThreadIndex);
    }
}
