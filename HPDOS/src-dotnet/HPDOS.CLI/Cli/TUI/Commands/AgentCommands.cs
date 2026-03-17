using HPD.Agent;
using HPD.Agent.Hosting.Data;
using HPDOS.Core.Auth;
using Spectre.Console;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace HPDOS.Shell.Cli.TUI.Commands;

/// <summary>
/// Implements the /agent command — an interactive agent manager.
/// Opens a top-level action menu (Switch / New / Edit / Delete) and runs
/// the selected flow inline, same pattern as /sessions.
/// </summary>
public static class AgentCommands
{
    // Known toolkits exposed in the wizard.
    private static readonly (string Display, string Reference)[] KnownToolkits =
    [
        ("Coding (files, commands, grep, glob)", "CodingToolkit"),
        ("Web Search",                           "WebSearchToolkit"),
        ("Math",                                 "MathToolkit"),
    ];

    // Known middlewares exposed in the wizard.
    private static readonly (string Display, string Reference)[] KnownMiddlewares =
    [
        ("Plan Mode",              "PlanModeMiddleware"),
        ("History Summarisation",  "HistoryReductionMiddleware"),
        ("Permission Guard",       "PermissionMiddleware"),
    ];

    public static void RegisterAll(CommandRegistry registry)
    {
        registry.Register(CreateAgentCommand());
    }

    private static SlashCommand CreateAgentCommand() => new()
    {
        Name = "agent",
        AltNames = ["agents"],
        Description = "Manage agents (switch, create, edit, delete)",
        AutoExecute = true,
        Action = async ctx =>
        {
            var http = GetHttp(ctx);
            if (http == null) return CommandResult.Error("HTTP client not available");

            var currentAgentId = ctx.Data.TryGetValue("AgentId", out var aid)
                ? aid?.ToString() ?? "default" : "default";

            // Load agent list upfront — needed by Switch, Edit, Delete.
            List<AgentSummaryDto>? agents = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(new Style(Theme.Text.Accent))
                .StartAsync("Loading agents…", async _ =>
                {
                    try { agents = await http.GetFromJsonAsync<List<AgentSummaryDto>>(
                        "/agents", HpdosJsonOptions.Http, ctx.CancellationToken); }
                    catch { }
                });

            agents ??= [];

            var actionPrompt = new SelectionPrompt<string>()
                .Title($"[{Theme.Markup(Theme.Text.Accent)}]Agent manager[/]  " +
                       $"[dim]active: [cyan]{Markup.Escape(currentAgentId)}[/][/]")
                .WrapAround(true)
                .AddChoices("Switch", "New", "Edit", "Delete", "Cancel");

            string action;
            try { action = await actionPrompt.ShowAsync(AnsiConsole.Console, ctx.CancellationToken); }
            catch (OperationCanceledException) { return CommandResult.Ok(); }

            return action switch
            {
                "Switch" => await RunSwitchAsync(ctx, agents, currentAgentId),
                "New"    => await RunNewAsync(http, ctx),
                "Edit"   => await RunEditAsync(http, ctx, agents, currentAgentId),
                "Delete" => await RunDeleteAsync(http, ctx, agents, currentAgentId),
                _        => CommandResult.Ok()
            };
        }
    };

    // ── Switch ───────────────────────────────────────────────────────────────

    private static async Task<CommandResult> RunSwitchAsync(
        CommandContext ctx, List<AgentSummaryDto> agents, string currentAgentId)
    {
        if (agents.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No agents found.[/]");
            return CommandResult.Ok();
        }

        var prompt = new SelectionPrompt<AgentSummaryDto>()
            .Title($"[{Theme.Markup(Theme.Text.Accent)}]Switch to agent:[/]")
            .PageSize(10)
            .WrapAround(true)
            .UseConverter(a =>
            {
                var active = a.Id == currentAgentId ? " [dim cyan]← active[/]" : "";
                return $"[white]{Markup.Escape(a.Name)}[/]  [dim]{UIHelpers.RelativeTime(a.UpdatedAt)}[/]{active}";
            });
        prompt.AddChoices(agents.OrderByDescending(a => a.UpdatedAt));

        AgentSummaryDto chosen;
        try { chosen = await prompt.ShowAsync(AnsiConsole.Console, ctx.CancellationToken); }
        catch (OperationCanceledException) { return CommandResult.Ok(); }

        ctx.Data["AgentId"] = chosen.Id;

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule(
            $"[{Theme.Markup(Theme.Text.Accent)}]Switched to agent: [bold]{Markup.Escape(chosen.Name)}[/][/]")
            .LeftJustified().RuleStyle(new Style(Theme.Text.Accent)));

        return CommandResult.Ok();
    }

    // ── New ──────────────────────────────────────────────────────────────────

    private static async Task<CommandResult> RunNewAsync(HttpClient http, CommandContext ctx)
    {
        var config = new AgentConfig();

        // Name
        try
        {
            config.Name = await new TextPrompt<string>("Agent name:")
                .ShowAsync(AnsiConsole.Console, ctx.CancellationToken);
        }
        catch (OperationCanceledException) { return CommandResult.Ok(); }

        if (string.IsNullOrWhiteSpace(config.Name)) return CommandResult.Ok();

        // Provider
        List<AuthSummary>? summaries = null;
        try { summaries = await http.GetFromJsonAsync<List<AuthSummary>>(
            "/api/providers", HpdosJsonOptions.Http, ctx.CancellationToken); }
        catch { /* non-fatal */ }

        var connected = summaries?.Where(s => s.IsAuthenticated && !s.IsExpired).ToList() ?? [];
        string? selectedProvider = null;

        if (connected.Count > 0)
        {
            var provPrompt = new SelectionPrompt<string>()
                .Title("Provider:")
                .UseConverter(id => summaries!.FirstOrDefault(s => s.ProviderId == id)?.DisplayName ?? id)
                .AddChoices(connected.Select(s => s.ProviderId));

            try { selectedProvider = await provPrompt.ShowAsync(AnsiConsole.Console, ctx.CancellationToken); }
            catch (OperationCanceledException) { return CommandResult.Ok(); }
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]No connected providers — skipping. Use /providers to connect one.[/]");
        }

        // Model
        string? selectedModel = null;
        if (selectedProvider != null)
        {
            List<ModelInfo>? models = null;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching models…", async _ =>
                {
                    try { models = await http.GetFromJsonAsync<List<ModelInfo>>(
                        $"/api/providers/{selectedProvider}/models", HpdosJsonOptions.Http, ctx.CancellationToken); }
                    catch { }
                });

            if (models is { Count: > 0 })
            {
                var recommended = models.Where(m => m.IsRecommended).ToList();
                var rest        = models.Where(m => !m.IsRecommended).ToList();

                var modelPrompt = new SelectionPrompt<string>()
                    .Title("Model:").PageSize(12)
                    .UseConverter(id => id == "__custom__"
                        ? "[dim]Enter model ID manually…[/]"
                        : FormatModel(models.FirstOrDefault(x => x.Id == id), id));

                if (recommended.Count > 0) modelPrompt.AddChoiceGroup("Recommended", recommended.Select(m => m.Id));
                if (rest.Count > 0)        modelPrompt.AddChoiceGroup("Models",      rest.Select(m => m.Id));
                modelPrompt.AddChoiceGroup("Other", ["__custom__"]);

                try { selectedModel = await modelPrompt.ShowAsync(AnsiConsole.Console, ctx.CancellationToken); }
                catch (OperationCanceledException) { return CommandResult.Ok(); }

                if (selectedModel == "__custom__")
                {
                    try { selectedModel = await new TextPrompt<string>("Model ID:")
                        .ShowAsync(AnsiConsole.Console, ctx.CancellationToken); }
                    catch (OperationCanceledException) { return CommandResult.Ok(); }
                }
            }
            else
            {
                try { selectedModel = await new TextPrompt<string>("Model ID:")
                    .ShowAsync(AnsiConsole.Console, ctx.CancellationToken); }
                catch (OperationCanceledException) { return CommandResult.Ok(); }
            }
        }

        if (selectedProvider != null || selectedModel != null)
            config.Provider = new ProviderConfig { ProviderKey = selectedProvider ?? "", ModelName = selectedModel ?? "" };

        // Max iterations
        try
        {
            config.MaxAgenticIterations = await new TextPrompt<int>($"Max tool-call turns (default {config.MaxAgenticIterations}):")
                .DefaultValue(config.MaxAgenticIterations)
                .ShowAsync(AnsiConsole.Console, ctx.CancellationToken);
        }
        catch (OperationCanceledException) { return CommandResult.Ok(); }

        // System instructions
        await ConfigureSystemInstructionsAsync(config, ctx.CancellationToken);

        // Behaviour gate
        bool configureBehaviour = false;
        try { configureBehaviour = await new ConfirmationPrompt("Configure behaviour (toolkits, middlewares)?")
            { DefaultValue = false }.ShowAsync(AnsiConsole.Console, ctx.CancellationToken); }
        catch (OperationCanceledException) { return CommandResult.Ok(); }

        if (configureBehaviour) await ConfigureBehaviourAsync(config, ctx.CancellationToken);

        // POST /agents
        StoredAgentDto? created = null;
        try
        {
            var resp = await http.PostAsJsonAsync("/agents",
                new CreateAgentRequest(config.Name, config), HpdosJsonOptions.Http, ctx.CancellationToken);
            resp.EnsureSuccessStatusCode();
            created = await resp.Content.ReadFromJsonAsync<StoredAgentDto>(HpdosJsonOptions.Http, ctx.CancellationToken);
        }
        catch (Exception ex) { return CommandResult.Error($"Agent creation failed: {ex.Message}"); }

        var newId = created?.Id ?? config.Name.ToLowerInvariant().Replace(' ', '-');
        ctx.Data["AgentId"] = newId;

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule(
            $"[{Theme.Markup(Theme.Text.Accent)}]Agent \"{Markup.Escape(config.Name)}\" created. Switched to: [bold]{Markup.Escape(config.Name)}[/][/]")
            .LeftJustified().RuleStyle(new Style(Theme.Text.Accent)));

        return CommandResult.Ok();
    }

    // ── Edit ─────────────────────────────────────────────────────────────────

    private static async Task<CommandResult> RunEditAsync(
        HttpClient http, CommandContext ctx,
        List<AgentSummaryDto> agents, string currentAgentId)
    {
        // Pick which agent to edit (default to active).
        AgentSummaryDto? target = agents.FirstOrDefault(a => a.Id == currentAgentId) ?? agents.FirstOrDefault();
        if (target == null) return CommandResult.Error("No agents available.");

        if (agents.Count > 1)
        {
            var pickPrompt = new SelectionPrompt<AgentSummaryDto>()
                .Title("Edit which agent?")
                .UseConverter(a => a.Id == currentAgentId
                    ? $"[white]{Markup.Escape(a.Name)}[/] [dim cyan]← active[/]"
                    : $"[white]{Markup.Escape(a.Name)}[/]");
            pickPrompt.AddChoices(agents.OrderByDescending(a => a.UpdatedAt));
            try { target = await pickPrompt.ShowAsync(AnsiConsole.Console, ctx.CancellationToken); }
            catch (OperationCanceledException) { return CommandResult.Ok(); }
        }

        StoredAgentDto? stored = null;
        await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
            .StartAsync("Loading agent…", async _ =>
            {
                try { stored = await http.GetFromJsonAsync<StoredAgentDto>(
                    $"/agents/{target.Id}", HpdosJsonOptions.Http, ctx.CancellationToken); }
                catch { }
            });

        if (stored == null) return CommandResult.Error($"Agent \"{target.Id}\" not found.");

        var config = stored.Config;

        var fieldPrompt = new SelectionPrompt<string>()
            .Title($"[white]Edit \"{Markup.Escape(stored.Name)}\"[/]")
            .AddChoices("Name", "Model", "System instructions", "Max iterations",
                        "Toolkits", "Behaviours", "Open full config in editor");

        string field;
        try { field = await fieldPrompt.ShowAsync(AnsiConsole.Console, ctx.CancellationToken); }
        catch (OperationCanceledException) { return CommandResult.Ok(); }

        switch (field)
        {
            case "Name":
                try { config.Name = await new TextPrompt<string>("Name:").DefaultValue(config.Name)
                    .ShowAsync(AnsiConsole.Console, ctx.CancellationToken); }
                catch (OperationCanceledException) { return CommandResult.Ok(); }
                break;

            case "Model":
                try
                {
                    var prov = await new TextPrompt<string>("Provider key:")
                        .DefaultValue(config.Provider?.ProviderKey ?? "")
                        .ShowAsync(AnsiConsole.Console, ctx.CancellationToken);
                    var model = await new TextPrompt<string>("Model ID:")
                        .DefaultValue(config.Provider?.ModelName ?? "")
                        .ShowAsync(AnsiConsole.Console, ctx.CancellationToken);
                    config.Provider = new ProviderConfig { ProviderKey = prov, ModelName = model };
                }
                catch (OperationCanceledException) { return CommandResult.Ok(); }
                break;

            case "System instructions":
                await ConfigureSystemInstructionsAsync(config, ctx.CancellationToken);
                break;

            case "Max iterations":
                try { config.MaxAgenticIterations = await new TextPrompt<int>($"Max tool-call turns (default {config.MaxAgenticIterations}):")
                    .DefaultValue(config.MaxAgenticIterations)
                    .ShowAsync(AnsiConsole.Console, ctx.CancellationToken); }
                catch (OperationCanceledException) { return CommandResult.Ok(); }
                break;

            case "Toolkits":
                await ConfigureBehaviourAsync(config, ctx.CancellationToken, onlyToolkits: true);
                break;

            case "Behaviours":
                await ConfigureBehaviourAsync(config, ctx.CancellationToken, onlyToolkits: false);
                break;

            case "Open full config in editor":
                await OpenConfigInEditorAsync(config, ctx.CancellationToken);
                break;
        }

        try
        {
            var resp = await http.PutAsJsonAsync($"/agents/{target.Id}",
                new UpdateAgentRequest(config), HpdosJsonOptions.Http, ctx.CancellationToken);
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex) { return CommandResult.Error($"Agent update failed: {ex.Message}"); }

        AnsiConsole.MarkupLine($"[green]✓[/] Agent \"{Markup.Escape(config.Name)}\" updated.");
        return CommandResult.Ok();
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    private static async Task<CommandResult> RunDeleteAsync(
        HttpClient http, CommandContext ctx,
        List<AgentSummaryDto> agents, string currentAgentId)
    {
        var deletable = agents.Where(a => a.Id != "default").ToList();
        if (deletable.Count == 0) return CommandResult.Error("Cannot delete the default agent.");

        var prompt = new SelectionPrompt<AgentSummaryDto>()
            .Title("[red]Delete which agent?[/]")
            .UseConverter(a => a.Id == currentAgentId
                ? $"[white]{Markup.Escape(a.Name)}[/] [dim cyan]← active[/]"
                : $"[white]{Markup.Escape(a.Name)}[/]");
        prompt.AddChoices(deletable.OrderByDescending(a => a.UpdatedAt));

        AgentSummaryDto toDelete;
        try { toDelete = await prompt.ShowAsync(AnsiConsole.Console, ctx.CancellationToken); }
        catch (OperationCanceledException) { return CommandResult.Ok(); }

        bool confirmed;
        try { confirmed = await new ConfirmationPrompt($"Delete agent \"{Markup.Escape(toDelete.Name)}\"?")
            { DefaultValue = false }.ShowAsync(AnsiConsole.Console, ctx.CancellationToken); }
        catch (OperationCanceledException) { return CommandResult.Ok(); }

        if (!confirmed) return CommandResult.Ok();

        try { (await http.DeleteAsync($"/agents/{toDelete.Id}", ctx.CancellationToken)).EnsureSuccessStatusCode(); }
        catch (Exception ex) { return CommandResult.Error($"Delete failed: {ex.Message}"); }

        if (toDelete.Id == currentAgentId)
            ctx.Data["AgentId"] = "default";

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule(
            $"[dim]Deleted \"{Markup.Escape(toDelete.Name)}\". " +
            (toDelete.Id == currentAgentId ? "Switched to: default" : "") + "[/]")
            .LeftJustified().RuleStyle(new Style(Theme.Text.Muted)));

        return CommandResult.Ok();
    }

    // ── Shared wizard helpers ─────────────────────────────────────────────────

    private static async Task ConfigureSystemInstructionsAsync(AgentConfig config, CancellationToken ct)
    {
        var choice = await new SelectionPrompt<string>()
            .Title("[bold]System instructions:[/]")
            .AddChoices("Keep current", "Open in editor")
            .ShowAsync(AnsiConsole.Console, ct);

        if (choice != "Open in editor") return;

        var tmpFile = Path.Combine(Path.GetTempPath(), $"hpdos-agent-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(tmpFile, config.SystemInstructions, ct);

        var editor = Environment.GetEnvironmentVariable("EDITOR")
            ?? Environment.GetEnvironmentVariable("VISUAL")
            ?? (OperatingSystem.IsMacOS() ? "nano" : "notepad");

        try
        {
            var proc = Process.Start(new ProcessStartInfo(editor, $"\"{tmpFile}\"") { UseShellExecute = false });
            proc?.WaitForExit();
            config.SystemInstructions = await File.ReadAllTextAsync(tmpFile, ct);
        }
        catch (Exception ex) { AnsiConsole.MarkupLine($"[dim]Could not open editor: {Markup.Escape(ex.Message)}[/]"); }
        finally { try { File.Delete(tmpFile); } catch { } }
    }

    private static async Task ConfigureBehaviourAsync(AgentConfig config, CancellationToken ct, bool onlyToolkits = false)
    {
        var currentToolkits = config.Toolkits.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toolkitPrompt = new MultiSelectionPrompt<string>()
            .Title("Select [cyan]toolkits[/]:").NotRequired().UseConverter(d => d);

        foreach (var (display, reference) in KnownToolkits)
        {
            toolkitPrompt.AddChoice(display);
            if (currentToolkits.Contains(reference)) toolkitPrompt.Select(display);
        }

        List<string> selectedToolkits;
        try { selectedToolkits = await toolkitPrompt.ShowAsync(AnsiConsole.Console, ct); }
        catch (OperationCanceledException) { return; }

        config.Toolkits = selectedToolkits
            .Select(d => KnownToolkits.First(k => k.Display == d).Reference)
            .Select(r => new ToolkitReference { Name = r })
            .ToList();

        if (onlyToolkits) return;

        var currentMiddlewares = config.Middlewares.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var middlewarePrompt = new MultiSelectionPrompt<string>()
            .Title("Select [cyan]behaviours[/]:").NotRequired().UseConverter(d => d);

        foreach (var (display, reference) in KnownMiddlewares)
        {
            middlewarePrompt.AddChoice(display);
            if (currentMiddlewares.Contains(reference)) middlewarePrompt.Select(display);
        }

        List<string> selectedMiddlewares;
        try { selectedMiddlewares = await middlewarePrompt.ShowAsync(AnsiConsole.Console, ct); }
        catch (OperationCanceledException) { return; }

        config.Middlewares = selectedMiddlewares
            .Select(d => KnownMiddlewares.First(k => k.Display == d).Reference)
            .Select(r => new MiddlewareReference { Name = r })
            .ToList();

        if (config.Middlewares.Any(m => m.Name == "HistoryReductionMiddleware"))
        {
            config.HistoryReduction ??= new HistoryReductionConfig { Enabled = true };

            var strategyPrompt = new SelectionPrompt<HistoryReductionStrategy>()
                .Title("History summarisation strategy:")
                .UseConverter(s => s == HistoryReductionStrategy.MessageCounting
                    ? "Sliding window (keep last N messages)"
                    : "Summarise (LLM-generated summary)")
                .AddChoices(HistoryReductionStrategy.MessageCounting, HistoryReductionStrategy.Summarizing);

            try { config.HistoryReduction.Strategy = await strategyPrompt.ShowAsync(AnsiConsole.Console, ct); }
            catch (OperationCanceledException) { return; }
        }

        try
        {
            var retries = await new TextPrompt<int>($"Max retries on provider error (default {config.ErrorHandling?.MaxRetries ?? 3}):")
                .DefaultValue(config.ErrorHandling?.MaxRetries ?? 3)
                .ShowAsync(AnsiConsole.Console, ct);
            config.ErrorHandling ??= new ErrorHandlingConfig();
            config.ErrorHandling.MaxRetries = retries;
        }
        catch (OperationCanceledException) { }
    }

    private static async Task OpenConfigInEditorAsync(AgentConfig config, CancellationToken ct)
    {
        var json    = JsonSerializer.Serialize(config, HpdosJsonOptions.Http);
        var tmpFile = Path.Combine(Path.GetTempPath(), $"hpdos-agent-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(tmpFile, json, ct);

        var editor = Environment.GetEnvironmentVariable("EDITOR")
            ?? Environment.GetEnvironmentVariable("VISUAL")
            ?? (OperatingSystem.IsMacOS() ? "nano" : "notepad");

        try
        {
            var proc = Process.Start(new ProcessStartInfo(editor, $"\"{tmpFile}\"") { UseShellExecute = false });
            proc?.WaitForExit();

            var parsed = JsonSerializer.Deserialize<AgentConfig>(await File.ReadAllTextAsync(tmpFile, ct), HpdosJsonOptions.Http);
            if (parsed != null)
            {
                config.Name                        = parsed.Name;
                config.SystemInstructions          = parsed.SystemInstructions;
                config.MaxAgenticIterations        = parsed.MaxAgenticIterations;
                config.ContinuationExtensionAmount = parsed.ContinuationExtensionAmount;
                config.Provider                    = parsed.Provider;
                config.Toolkits                    = parsed.Toolkits;
                config.Middlewares                 = parsed.Middlewares;
                config.HistoryReduction            = parsed.HistoryReduction;
                config.ErrorHandling               = parsed.ErrorHandling;
                config.Collapsing                  = parsed.Collapsing;
                config.Mcp                         = parsed.Mcp;
            }
        }
        catch (Exception ex) { AnsiConsole.MarkupLine($"[dim]Editor error: {Markup.Escape(ex.Message)}[/]"); }
        finally { try { File.Delete(tmpFile); } catch { } }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static HttpClient? GetHttp(CommandContext ctx) =>
        ctx.Data.TryGetValue("HttpClient", out var hcObj) && hcObj is HttpClient http ? http : null;

    private static string FormatModel(ModelInfo? m, string fallbackId)
    {
        if (m is null) return fallbackId;
        var suffix = m.IsFree ? " [dim](free)[/]" : "";
        return $"{Markup.Escape(m.Description ?? m.Id)}{suffix}";
    }
}
