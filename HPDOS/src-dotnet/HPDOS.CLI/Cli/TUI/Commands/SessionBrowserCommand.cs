using HPD.Agent.Hosting.Data;
using Spectre.Console;
using System.Net.Http.Json;
using System.Text.Json;

namespace HPDOS.Shell.Cli.TUI.Commands;

/// <summary>
/// Rich session browser for /sessions. Two-phase interaction:
///   Phase 1 — searchable, grouped session picker with branch counts loaded in parallel.
///   Phase 2 — action prompt (Open/Rename/Delete), then branch picker, then history tail.
///
/// When pinnedSessionId is provided, Phase 1 is skipped and the browser opens
/// directly into Phase 2 for that session (used by /branch list).
/// </summary>
public static class SessionBrowserCommand
{
    /// <summary>
    /// Runs the session browser. Returns the selected session and branch, or null if cancelled.
    /// </summary>
    public static async Task<SessionSwitchResult?> RunAsync(
        HttpClient http,
        AgentUIRenderer renderer,
        string? pinnedSessionId = null,
        string? activeSessionId = null,
        CancellationToken ct = default)
    {
        SessionDto? selected = null;
        Dictionary<string, Task<BranchDto[]?>> branchTasks = new();

        // ── Phase 1: Session picker (skipped when pinnedSessionId is set) ────────

        if (pinnedSessionId != null)
        {
            // Load just the pinned session.
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(new Style(Theme.Text.Accent))
                .StartAsync("Loading session…", async _ =>
                {
                    try
                    {
                        selected = await http.GetFromJsonAsync<SessionDto>(
                            $"/sessions/{pinnedSessionId}", HpdosJsonOptions.Http, ct);
                    }
                    catch { /* handled below */ }
                });

            if (selected == null)
            {
                AnsiConsole.MarkupLine("[dim]Session not found.[/]");
                return null;
            }
        }
        else
        {
            // Full session list.
            SessionDto[]? sessions = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(new Style(Theme.Text.Accent))
                .StartAsync("Loading sessions…", async _ =>
                {
                    sessions = await http.GetFromJsonAsync<SessionDto[]>("/sessions", HpdosJsonOptions.Http, ct);
                });

            var ordered = sessions?.OrderByDescending(s => s.LastActivity).ToArray() ?? [];

            // Kick off branch-count fetches in parallel.
            branchTasks = ordered.ToDictionary(
                s => s.Id,
                s => http.GetFromJsonAsync<BranchDto[]>($"/sessions/{s.Id}/branches", HpdosJsonOptions.Http, ct));

            // Session picker loop — re-enters after rename/delete without returning.
            while (true)
            {
                var pickerResult = await RunSessionPickerAsync(ordered, branchTasks, http, activeSessionId, ct);
                if (pickerResult == null) return null;

                if (pickerResult.Value.ActionResult == SessionPickerAction.New)
                    return new SessionSwitchResult("", "", NewSession: true);

                if (pickerResult.Value.ActionResult == SessionPickerAction.Open)
                {
                    selected = pickerResult.Value.Session;
                    break;
                }

                if (pickerResult.Value.ActionResult == SessionPickerAction.Rename)
                {
                    var s = pickerResult.Value.Session;
                    var currentTitle = GetTitle(s);
                    string newTitle;
                    try
                    {
                        newTitle = await new TextPrompt<string>($"New name [{Markup.Escape(currentTitle)}]:")
                            .DefaultValue(currentTitle)
                            .ShowAsync(AnsiConsole.Console, ct);
                    }
                    catch (OperationCanceledException) { return null; }

                    if (!string.IsNullOrWhiteSpace(newTitle) && newTitle != currentTitle)
                    {
                        try
                        {
                            await http.PatchAsJsonAsync($"/sessions/{s.Id}",
                                new UpdateSessionRequest(new Dictionary<string, object?> { ["title"] = newTitle }),
                                HpdosJsonOptions.Http, ct);
                            // Patch the local copy so the picker re-renders with the new title.
                            var idx = Array.IndexOf(ordered, s);
                            if (idx >= 0)
                                ordered[idx] = s with { Metadata = MergeTitle(s.Metadata, newTitle) };
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[red]Rename failed:[/] {Markup.Escape(ex.Message)}");
                        }
                    }
                    continue; // back to picker
                }

                if (pickerResult.Value.ActionResult == SessionPickerAction.Delete)
                {
                    var s = pickerResult.Value.Session;
                    var title = GetTitle(s);
                    var branchCount = branchTasks.TryGetValue(s.Id, out var bt) && bt.IsCompletedSuccessfully
                        ? bt.Result?.Length ?? 1 : 1;
                    var warning = branchCount == 1
                        ? $"Delete \"{Markup.Escape(title)}\"? This will delete its history."
                        : $"Delete \"{Markup.Escape(title)}\"? This will delete all {branchCount} branches and their history.";

                    bool confirmed;
                    try
                    {
                        confirmed = await new ConfirmationPrompt(warning) { DefaultValue = false }
                            .ShowAsync(AnsiConsole.Console, ct);
                    }
                    catch (OperationCanceledException) { return null; }

                    if (confirmed)
                    {
                        try
                        {
                            var resp = await http.DeleteAsync($"/sessions/{s.Id}", ct);
                            resp.EnsureSuccessStatusCode();
                            AnsiConsole.Write(new Rule("[dim]Session deleted.[/]").LeftJustified().RuleStyle(new Style(Theme.Text.Muted)));

                            // If the deleted session was the active one, signal ChatCommand to create a new one.
                            if (s.Id == activeSessionId)
                                return new SessionSwitchResult(s.Id, "main", DeletedActiveSession: true);

                            // Re-build ordered without the deleted session.
                            ordered = ordered.Where(x => x.Id != s.Id).ToArray();
                            branchTasks.Remove(s.Id);
                            if (ordered.Length == 0)
                            {
                                AnsiConsole.MarkupLine("[dim]No sessions remaining.[/]");
                                return null;
                            }
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[red]Delete failed:[/] {Markup.Escape(ex.Message)}");
                        }
                    }
                    continue; // back to picker
                }
            }
        }

        // ── Phase 2: Resolve branches ────────────────────────────────────────

        BranchDto[]? branches = null;
        if (branchTasks.TryGetValue(selected!.Id, out var branchTask))
        {
            try { branches = await branchTask; }
            catch { /* will re-fetch below */ }
        }

        if (branches == null || branches.Length == 0)
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(new Style(Theme.Text.Accent))
                .StartAsync("Loading branches…", async _ =>
                {
                    branches = await http.GetFromJsonAsync<BranchDto[]>(
                        $"/sessions/{selected.Id}/branches", HpdosJsonOptions.Http, ct);
                });
        }

        branches ??= [];

        // Branch picker (or auto-select if only one).
        BranchDto? activeBranch;
        if (branches.Length > 1)
        {
            activeBranch = await PickBranchAsync(http, selected.Id, branches, ct);
            if (activeBranch == null) return null;
        }
        else
        {
            activeBranch = branches.Length == 1 ? branches[0] : null;
        }

        var branchId = activeBranch?.Id ?? "main";

        // ── Phase 2: Session header ──────────────────────────────────────────

        AnsiConsole.WriteLine();
        AnsiConsole.Write(
            new Rule($"[{Theme.Markup(Theme.Text.Accent)}]Session [bold]{selected.Id[..8]}[/]…[/]  " +
                     $"[dim]{Markup.Escape(activeBranch?.Name ?? "main")}  ·  created {UIHelpers.RelativeTime(selected.CreatedAt)}[/]")
                .LeftJustified()
                .RuleStyle(new Style(Theme.Text.Accent)));

        // ── Phase 2: Message history tail ────────────────────────────────────

        if (activeBranch != null && activeBranch.MessageCount > 0)
        {
            MessageDto[]? messages = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(new Style(Theme.Text.Accent))
                .StartAsync("Loading history…", async _ =>
                {
                    var resp = await http.GetAsync(
                        $"/sessions/{selected.Id}/branches/{branchId}/messages", ct);
                    if (!resp.IsSuccessStatusCode)
                    {
                        var body = await resp.Content.ReadAsStringAsync(ct);
                        AnsiConsole.MarkupLine(
                            $"[dim red]History unavailable ({(int)resp.StatusCode}): {Markup.Escape(body)}[/]");
                        return;
                    }
                    messages = await resp.Content.ReadFromJsonAsync<MessageDto[]>(
                        HpdosJsonOptions.Http, ct);
                });

            if (messages != null && messages.Length > 0)
            {
                const int tailTurns = 3;
                var tail = messages.TakeLast(tailTurns * 2).ToArray();

                AnsiConsole.Write(
                    new Rule($"[dim]Last {tail.Length} messages in [cyan]{Markup.Escape(activeBranch.Name)}[/][/]")
                        .LeftJustified()
                        .RuleStyle(new Style(Theme.Text.Muted)));
                AnsiConsole.WriteLine();

                renderer.RenderHistoryTail(tail);
            }
        }

        return new SessionSwitchResult(selected.Id, branchId);
    }

    // ── Session picker loop ──────────────────────────────────────────────────

    private static readonly PickerEntry _newSessionEntry = new(null, "__new__");

    private static async Task<(SessionDto Session, SessionPickerAction ActionResult)?> RunSessionPickerAsync(
        SessionDto[] ordered,
        Dictionary<string, Task<BranchDto[]?>> branchTasks,
        HttpClient http,
        string? activeSessionId,
        CancellationToken ct)
    {
        var prompt = new SelectionPrompt<PickerEntry>() { SearchEnabled = true };
        prompt
            .Title($"[{Theme.Markup(Theme.Text.Accent)}]Switch to session[/] [dim](type to search)[/]")
            .PageSize(12)
            .WrapAround(true)
            .UseConverter(e => e == _newSessionEntry
                ? "[cyan]+ New Session[/]"
                : e.IsHeader
                    ? $"[dim]{e.Label}[/]"
                    : FormatSessionChoice(e.Session!, branchTasks, activeSessionId))
            .MoreChoicesText("[dim]↑↓ more[/]");

        static PickerEntry Header(string label) => new(null, label);
        static PickerEntry Item(SessionDto s)   => new(s, null);

        prompt.AddChoiceGroup(Header("Actions"), [_newSessionEntry]);

        var today    = ordered.Where(s => UIHelpers.IsToday(s.LastActivity)).ToArray();
        var thisWeek = ordered.Where(s => !UIHelpers.IsToday(s.LastActivity) && UIHelpers.IsThisWeek(s.LastActivity)).ToArray();
        var older    = ordered.Where(s => !UIHelpers.IsThisWeek(s.LastActivity)).ToArray();

        if (today.Length > 0)    prompt.AddChoiceGroup(Header("Today"),     today.Select(Item));
        if (thisWeek.Length > 0) prompt.AddChoiceGroup(Header("This Week"), thisWeek.Select(Item));
        if (older.Length > 0)    prompt.AddChoiceGroup(Header("Older"),     older.Select(Item));

        PickerEntry pickedEntry;
        try { pickedEntry = AnsiConsole.Prompt(prompt); }
        catch (OperationCanceledException) { return null; }

        if (pickedEntry == _newSessionEntry)
            return (default!, SessionPickerAction.New);

        if (pickedEntry.IsHeader) return null;
        var selected = pickedEntry.Session!;

        // Action prompt.
        var actionPrompt = new SelectionPrompt<string>()
            .Title($"[white]{Markup.Escape(GetTitle(selected))}[/]")
            .AddChoices("Open", "Rename", "Delete", "← Back");

        string action;
        try { action = await actionPrompt.ShowAsync(AnsiConsole.Console, ct); }
        catch (OperationCanceledException) { return null; }

        return action switch
        {
            "Open"   => (selected, SessionPickerAction.Open),
            "Rename" => (selected, SessionPickerAction.Rename),
            "Delete" => (selected, SessionPickerAction.Delete),
            _        => ((SessionDto, SessionPickerAction)?) null
        };
    }

    private enum SessionPickerAction { Open, Rename, Delete, New }

    // ── Branch picker ────────────────────────────────────────────────────────

    private static async Task<BranchDto?> PickBranchAsync(
        HttpClient http,
        string sessionId,
        BranchDto[] branches,
        CancellationToken ct)
    {
        var mostRecentId = branches.OrderByDescending(b => b.LastActivity).First().Id;

        while (true)
        {
            var prompt = new SelectionPrompt<BranchPickerEntry>()
                .Title($"[{Theme.Markup(Theme.Text.Accent)}]Which branch?[/]")
                .PageSize(8)
                .WrapAround(true)
                .UseConverter(e => e.IsDelete
                    ? "[red dim]Delete a branch…[/]"
                    : FormatBranchChoice(e.Branch!, mostRecentId));

            var original = branches.Where(b => b.IsOriginal);
            var forks    = branches.Where(b => !b.IsOriginal).OrderByDescending(b => b.LastActivity);
            prompt.AddChoices(original.Concat(forks).Select(b => new BranchPickerEntry(b)));
            prompt.AddChoices(new BranchPickerEntry(null)); // delete sentinel

            BranchPickerEntry picked;
            try { picked = await prompt.ShowAsync(AnsiConsole.Console, ct); }
            catch (OperationCanceledException) { return null; }

            if (!picked.IsDelete)
                return picked.Branch;

            // Delete flow.
            var deletePrompt = new SelectionPrompt<BranchDto>()
                .Title($"[red]Delete which branch?[/]")
                .PageSize(8)
                .WrapAround(true)
                .UseConverter(b => FormatBranchChoice(b, mostRecentId));

            // Cannot delete the original branch (would orphan the session).
            var deletable = branches.Where(b => !b.IsOriginal).OrderByDescending(b => b.LastActivity).ToArray();
            if (deletable.Length == 0)
            {
                AnsiConsole.MarkupLine("[dim]No deletable branches (cannot delete the original branch).[/]");
                continue;
            }
            deletePrompt.AddChoices(deletable);

            BranchDto toDelete;
            try { toDelete = await deletePrompt.ShowAsync(AnsiConsole.Console, ct); }
            catch (OperationCanceledException) { continue; }

            var childCount = toDelete.TotalForks;
            var confirmMsg = childCount > 0
                ? $"Delete \"{Markup.Escape(toDelete.Name)}\" and its {childCount} child branch(es)?"
                : $"Delete \"{Markup.Escape(toDelete.Name)}\"?";

            bool confirmed;
            try
            {
                confirmed = await new ConfirmationPrompt(confirmMsg) { DefaultValue = false }
                    .ShowAsync(AnsiConsole.Console, ct);
            }
            catch (OperationCanceledException) { continue; }

            if (!confirmed) continue;

            try
            {
                var url = childCount > 0
                    ? $"/sessions/{sessionId}/branches/{toDelete.Id}?recursive=true"
                    : $"/sessions/{sessionId}/branches/{toDelete.Id}";
                var resp = await http.DeleteAsync(url, ct);
                resp.EnsureSuccessStatusCode();
                AnsiConsole.Write(new Rule($"[dim]Branch \"{Markup.Escape(toDelete.Name)}\" deleted.[/]")
                    .LeftJustified().RuleStyle(new Style(Theme.Text.Muted)));
                branches = branches.Where(b => b.Id != toDelete.Id).ToArray();
                mostRecentId = branches.Length > 0
                    ? branches.OrderByDescending(b => b.LastActivity).First().Id
                    : mostRecentId;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Delete failed:[/] {Markup.Escape(ex.Message)}");
            }
        }
    }

    // ── Display string helpers ───────────────────────────────────────────────

    internal static string GetTitle(SessionDto s)
    {
        if (s.Metadata?.TryGetValue("title", out var t) == true && t is not null)
        {
            return t is JsonElement el ? el.GetString() ?? s.Id[..8] + "…" : t.ToString() ?? s.Id[..8] + "…";
        }
        return s.Id[..8] + "…";
    }

    private static Dictionary<string, object> MergeTitle(Dictionary<string, object>? existing, string title)
    {
        var d = existing != null ? new Dictionary<string, object>(existing) : new Dictionary<string, object>();
        d["title"] = title;
        return d;
    }

    private static string FormatSessionChoice(
        SessionDto s,
        Dictionary<string, Task<BranchDto[]?>> branchTasks,
        string? activeSessionId)
    {
        var title = GetTitle(s);
        var branchCount = "";
        if (branchTasks.TryGetValue(s.Id, out var task) && task.IsCompletedSuccessfully)
        {
            var count = task.Result?.Length ?? 0;
            branchCount = count == 1 ? "  [dim]1 branch[/]" : $"  [dim]{count} branches[/]";
        }
        var active = s.Id == activeSessionId ? " [dim cyan]← active[/]" : "";
        return $"[white]{Markup.Escape(title)}[/]{branchCount}  [dim]{UIHelpers.RelativeTime(s.LastActivity)}[/]{active}";
    }

    private static string FormatBranchChoice(BranchDto b, string mostRecentId)
    {
        var prefix = b.ForkedFrom != null ? "  └─ " : "";
        var recent = b.Id == mostRecentId ? " [dim cyan]← most recent[/]" : "";
        var tags   = b.Tags is { Count: > 0 }
            ? $"  [dim grey]{string.Join(", ", b.Tags)}[/]"
            : "";
        return $"{prefix}[white]{Markup.Escape(b.Name)}[/]  " +
               $"[dim]{b.MessageCount} messages · {UIHelpers.RelativeTime(b.LastActivity)}[/]{tags}{recent}";
    }
}

/// <summary>Wraps a SessionDto or a group header label for use in SelectionPrompt&lt;PickerEntry&gt;.</summary>
internal record PickerEntry(SessionDto? Session, string? Label)
{
    public bool IsHeader => Session is null;
}

/// <summary>Wraps a BranchDto or the delete sentinel for use in the branch picker.</summary>
internal record BranchPickerEntry(BranchDto? Branch)
{
    public bool IsDelete => Branch is null;
}

/// <summary>Result of a session browser selection.</summary>
public record SessionSwitchResult(string SessionId, string BranchId, bool DeletedActiveSession = false, bool NewSession = false);
