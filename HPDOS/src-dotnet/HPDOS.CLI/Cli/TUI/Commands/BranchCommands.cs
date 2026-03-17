using HPD.Agent.Hosting.Data;
using Spectre.Console;
using System.Net.Http.Json;

namespace HPDOS.Shell.Cli.TUI.Commands;

/// <summary>
/// Implements the /branch command — an interactive branch manager.
/// Opens a top-level action menu (Fork / Switch / New / Delete / Tree) and runs
/// the selected flow inline, same pattern as /sessions.
/// </summary>
public static class BranchCommands
{
    public static void RegisterAll(CommandRegistry registry)
    {
        registry.Register(CreateBranchCommand());
    }

    private static SlashCommand CreateBranchCommand() => new()
    {
        Name = "branch",
        AltNames = ["branches", "br"],
        Description = "Manage branches for this session",
        AutoExecute = true,
        Action = async ctx =>
        {
            var (http, sessionId, activeBranchId) = GetContext(ctx);
            if (http == null) return CommandResult.Error("HTTP client not available");

            // Load all branches for this session upfront — used by most flows.
            BranchDto[]? branches = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(new Style(Theme.Text.Accent))
                .StartAsync("Loading branches…", async _ =>
                {
                    branches = await http.GetFromJsonAsync<BranchDto[]>(
                        $"/sessions/{sessionId}/branches", HpdosJsonOptions.Http, ctx.CancellationToken);
                });

            branches ??= [];

            // Top-level action menu.
            var actionPrompt = new SelectionPrompt<string>()
                .Title($"[{Theme.Markup(Theme.Text.Accent)}]Branch manager[/]  " +
                       $"[dim]current: [cyan]{Markup.Escape(activeBranchId)}[/][/]")
                .WrapAround(true)
                .AddChoices("Fork", "Switch", "New", "Delete", "Tree", "Cancel");

            string action;
            try { action = await actionPrompt.ShowAsync(AnsiConsole.Console, ctx.CancellationToken); }
            catch (OperationCanceledException) { return CommandResult.Ok(); }

            switch (action)
            {
                case "Fork":   return await RunForkAsync(http, ctx, sessionId, activeBranchId, branches);
                case "Switch": return await RunSwitchAsync(ctx, sessionId, activeBranchId, branches);
                case "New":    return await RunNewAsync(http, ctx, sessionId);
                case "Delete": return await RunDeleteAsync(http, ctx, sessionId, activeBranchId, branches);
                case "Tree":   return RunTree(branches, activeBranchId);
                default:       return CommandResult.Ok();
            }
        }
    };

    // ── Fork ─────────────────────────────────────────────────────────────────

    private static async Task<CommandResult> RunForkAsync(
        HttpClient http, CommandContext ctx,
        string sessionId, string branchId, BranchDto[] branches)
    {
        // Fetch messages for the current branch.
        MessageDto[]? messages = null;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Loading messages…", async _ =>
            {
                var resp = await http.GetAsync(
                    $"/sessions/{sessionId}/branches/{branchId}/messages", ctx.CancellationToken);
                if (resp.IsSuccessStatusCode)
                    messages = await resp.Content.ReadFromJsonAsync<MessageDto[]>(
                        HpdosJsonOptions.Http, ctx.CancellationToken);
            });

        if (messages == null || messages.Length == 0)
            return CommandResult.Error("No messages in current branch to fork from.");

        var userMessages = messages
            .Select((m, i) => (Message: m, Index: i))
            .Where(x => x.Message.Role == "user")
            .ToArray();

        if (userMessages.Length == 0)
            return CommandResult.Error("No user messages to fork from.");

        var pickPrompt = new SelectionPrompt<(MessageDto Message, int Index)>()
            .Title($"[{Theme.Markup(Theme.Text.Accent)}]Fork from which message?[/]")
            .PageSize(8)
            .WrapAround(true)
            .UseConverter(x =>
            {
                var text = x.Message.GetText();
                var preview = text.Length > 70 ? text[..70] + "…" : text;
                return $"[dim][{x.Index}][/] {Markup.Escape(preview)}";
            });
        pickPrompt.AddChoices(userMessages.Reverse());

        (MessageDto Message, int Index) chosen;
        try { chosen = await pickPrompt.ShowAsync(AnsiConsole.Console, ctx.CancellationToken); }
        catch (OperationCanceledException) { return CommandResult.Ok(); }

        string? branchName = null;
        try
        {
            branchName = await new TextPrompt<string>("Branch name [dim](optional, Enter to skip)[/]:")
                .AllowEmpty()
                .ShowAsync(AnsiConsole.Console, ctx.CancellationToken);
            if (string.IsNullOrWhiteSpace(branchName)) branchName = null;
        }
        catch (OperationCanceledException) { return CommandResult.Ok(); }

        var newBranchId = Guid.NewGuid().ToString("N")[..12];
        ForkBranchRequest forkReq = new(
            NewBranchId: newBranchId,
            FromMessageIndex: chosen.Index,
            Name: branchName,
            Description: null,
            Tags: null);

        BranchDto? newBranch = null;
        try
        {
            var resp = await http.PostAsJsonAsync(
                $"/sessions/{sessionId}/branches/{branchId}/fork",
                forkReq, HpdosJsonOptions.Http, ctx.CancellationToken);
            resp.EnsureSuccessStatusCode();
            newBranch = await resp.Content.ReadFromJsonAsync<BranchDto>(
                HpdosJsonOptions.Http, ctx.CancellationToken);
        }
        catch (Exception ex) { return CommandResult.Error($"Fork failed: {ex.Message}"); }

        var activeName = newBranch?.Name ?? branchName ?? newBranchId;
        var activeId   = newBranch?.Id   ?? newBranchId;

        ctx.Data["BranchId"] = activeId;
        ctx.Data["PrefillInput"] = chosen.Message.GetText();

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule(
            $"[{Theme.Markup(Theme.Text.Accent)}]Switched to branch: [bold]{Markup.Escape(activeName)}[/][/]")
            .LeftJustified().RuleStyle(new Style(Theme.Text.Accent)));

        return CommandResult.Ok();
    }

    // ── Switch ───────────────────────────────────────────────────────────────

    private static async Task<CommandResult> RunSwitchAsync(
        CommandContext ctx, string sessionId, string activeBranchId, BranchDto[] branches)
    {
        if (branches.Length == 0)
            return CommandResult.Error("No branches found.");

        var mostRecentId = branches.OrderByDescending(b => b.LastActivity).First().Id;

        var prompt = new SelectionPrompt<BranchDto>()
            .Title($"[{Theme.Markup(Theme.Text.Accent)}]Switch to branch:[/]")
            .PageSize(8)
            .WrapAround(true)
            .UseConverter(b => FormatBranchChoice(b, mostRecentId, activeBranchId));

        var original = branches.Where(b => b.IsOriginal);
        var forks    = branches.Where(b => !b.IsOriginal).OrderByDescending(b => b.LastActivity);
        prompt.AddChoices(original.Concat(forks));

        BranchDto chosen;
        try { chosen = await prompt.ShowAsync(AnsiConsole.Console, ctx.CancellationToken); }
        catch (OperationCanceledException) { return CommandResult.Ok(); }

        if (chosen.Id == activeBranchId) return CommandResult.Ok();

        ctx.Data["BranchId"] = chosen.Id;

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule(
            $"[{Theme.Markup(Theme.Text.Accent)}]Switched to branch: [bold]{Markup.Escape(chosen.Name)}[/][/]")
            .LeftJustified().RuleStyle(new Style(Theme.Text.Accent)));

        return CommandResult.Ok();
    }

    // ── New ──────────────────────────────────────────────────────────────────

    private static async Task<CommandResult> RunNewAsync(
        HttpClient http, CommandContext ctx, string sessionId)
    {
        string branchName;
        try
        {
            branchName = await new TextPrompt<string>("Branch name:")
                .ShowAsync(AnsiConsole.Console, ctx.CancellationToken);
        }
        catch (OperationCanceledException) { return CommandResult.Ok(); }

        if (string.IsNullOrWhiteSpace(branchName)) return CommandResult.Ok();

        var newBranchId = Guid.NewGuid().ToString("N")[..12];
        CreateBranchRequest req = new(
            BranchId: newBranchId,
            Name: branchName.Trim(),
            Description: null,
            Tags: null);

        BranchDto? newBranch = null;
        try
        {
            var resp = await http.PostAsJsonAsync(
                $"/sessions/{sessionId}/branches",
                req, HpdosJsonOptions.Http, ctx.CancellationToken);
            resp.EnsureSuccessStatusCode();
            newBranch = await resp.Content.ReadFromJsonAsync<BranchDto>(
                HpdosJsonOptions.Http, ctx.CancellationToken);
        }
        catch (Exception ex) { return CommandResult.Error($"Branch creation failed: {ex.Message}"); }

        var activeId   = newBranch?.Id   ?? newBranchId;
        var activeName = newBranch?.Name ?? branchName.Trim();

        ctx.Data["BranchId"] = activeId;

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule(
            $"[{Theme.Markup(Theme.Text.Accent)}]Switched to branch: [bold]{Markup.Escape(activeName)}[/][/]")
            .LeftJustified().RuleStyle(new Style(Theme.Text.Accent)));

        return CommandResult.Ok();
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    private static async Task<CommandResult> RunDeleteAsync(
        HttpClient http, CommandContext ctx,
        string sessionId, string activeBranchId, BranchDto[] branches)
    {
        var deletable = branches.Where(b => !b.IsOriginal).OrderByDescending(b => b.LastActivity).ToArray();
        if (deletable.Length == 0)
            return CommandResult.Error("No deletable branches (cannot delete the original branch).");

        var mostRecentId = branches.OrderByDescending(b => b.LastActivity).First().Id;

        var prompt = new SelectionPrompt<BranchDto>()
            .Title("[red]Delete which branch?[/]")
            .PageSize(8)
            .WrapAround(true)
            .UseConverter(b => FormatBranchChoice(b, mostRecentId, activeBranchId));
        prompt.AddChoices(deletable);

        BranchDto toDelete;
        try { toDelete = await prompt.ShowAsync(AnsiConsole.Console, ctx.CancellationToken); }
        catch (OperationCanceledException) { return CommandResult.Ok(); }

        var childCount = toDelete.TotalForks;
        var confirmMsg = childCount > 0
            ? $"Delete \"{Markup.Escape(toDelete.Name)}\" and its {childCount} child branch(es)?"
            : $"Delete \"{Markup.Escape(toDelete.Name)}\"?";

        bool confirmed;
        try
        {
            confirmed = await new ConfirmationPrompt(confirmMsg) { DefaultValue = false }
                .ShowAsync(AnsiConsole.Console, ctx.CancellationToken);
        }
        catch (OperationCanceledException) { return CommandResult.Ok(); }

        if (!confirmed) return CommandResult.Ok();

        try
        {
            var url = childCount > 0
                ? $"/sessions/{sessionId}/branches/{toDelete.Id}?recursive=true"
                : $"/sessions/{sessionId}/branches/{toDelete.Id}";
            (await http.DeleteAsync(url, ctx.CancellationToken)).EnsureSuccessStatusCode();
        }
        catch (Exception ex) { return CommandResult.Error($"Delete failed: {ex.Message}"); }

        // If we deleted the active branch, switch to parent or main.
        if (toDelete.Id == activeBranchId)
        {
            var parentId   = toDelete.ForkedFrom ?? "main";
            var parentName = branches.FirstOrDefault(b => b.Id == parentId)?.Name ?? parentId;
            ctx.Data["BranchId"] = parentId;

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule(
                $"[dim]Branch deleted. Switched to: [cyan]{Markup.Escape(parentName)}[/][/]")
                .LeftJustified().RuleStyle(new Style(Theme.Text.Muted)));
        }
        else
        {
            AnsiConsole.Write(new Rule(
                $"[dim]Branch \"{Markup.Escape(toDelete.Name)}\" deleted.[/]")
                .LeftJustified().RuleStyle(new Style(Theme.Text.Muted)));
        }

        return CommandResult.Ok();
    }

    // ── Tree ─────────────────────────────────────────────────────────────────

    private static CommandResult RunTree(BranchDto[] branches, string activeBranchId)
    {
        if (branches.Length == 0)
            return CommandResult.Error("No branches found.");

        AnsiConsole.WriteLine();
        AnsiConsole.Write(BuildTree(branches, activeBranchId));
        AnsiConsole.WriteLine();
        return CommandResult.Ok();
    }

    private static Tree BuildTree(BranchDto[] branches, string? activeBranchId)
    {
        var root = branches.FirstOrDefault(b => b.IsOriginal && b.ForkedFrom == null)
            ?? branches.First();

        var tree = new Tree(FormatTreeNode(root, activeBranchId));

        var byParent = branches
            .Where(b => b.ForkedFrom != null)
            .GroupBy(b => b.ForkedFrom!)
            .ToDictionary(g => g.Key, g => g.OrderBy(b => b.CreatedAt).ToList());

        AddChildren(tree, root.Id, byParent, activeBranchId);
        return tree;
    }

    private static void AddChildren(
        IHasTreeNodes parent, string parentId,
        Dictionary<string, List<BranchDto>> byParent, string? activeBranchId)
    {
        if (!byParent.TryGetValue(parentId, out var children)) return;
        foreach (var child in children)
        {
            var node = parent.AddNode(FormatTreeNode(child, activeBranchId));
            AddChildren(node, child.Id, byParent, activeBranchId);
        }
    }

    private static string FormatTreeNode(BranchDto b, string? activeBranchId)
    {
        var active   = b.Id == activeBranchId ? " [cyan]← active[/]" : "";
        var forkInfo = b.ForkedAtMessageIndex.HasValue
            ? $" [dim]forked @ msg {b.ForkedAtMessageIndex}[/]" : "";
        return $"[white]{Markup.Escape(b.Name)}[/] [dim]{b.MessageCount} msgs[/]{forkInfo}{active}";
    }

    private static string FormatBranchChoice(BranchDto b, string mostRecentId, string activeBranchId)
    {
        var prefix  = b.ForkedFrom != null ? "  └─ " : "";
        var recent  = b.Id == mostRecentId    ? " [dim cyan]← most recent[/]" : "";
        var current = b.Id == activeBranchId  ? " [dim cyan]← active[/]"      : "";
        var tags    = b.Tags is { Count: > 0 }
            ? $"  [dim grey]{string.Join(", ", b.Tags)}[/]" : "";
        return $"{prefix}[white]{Markup.Escape(b.Name)}[/]  " +
               $"[dim]{b.MessageCount} messages · {UIHelpers.RelativeTime(b.LastActivity)}[/]{tags}{recent}{current}";
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (HttpClient? Http, string SessionId, string BranchId) GetContext(CommandContext ctx)
    {
        if (!ctx.Data.TryGetValue("HttpClient", out var hcObj) || hcObj is not HttpClient http)
            return (null, "", "");
        var sessionId = ctx.Data.TryGetValue("SessionId", out var sid) ? sid?.ToString() ?? "" : "";
        var branchId  = ctx.Data.TryGetValue("BranchId",  out var bid) ? bid?.ToString() ?? "main" : "main";
        return (http, sessionId, branchId);
    }
}
