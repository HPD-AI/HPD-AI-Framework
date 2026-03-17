using System.Net.Http.Json;
using HPDOS.Core.Auth;
using HPDOS.Shell.Cli.TUI;
using HPDOS.Shell.Shell;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace HPDOS.Shell.Cli.TUI.Commands;

/// <summary>
/// Built-in slash commands for hpdos chat.
/// </summary>
public static class BuiltInCommands
{
    public static void RegisterAll(CommandRegistry registry)
    {
        registry.Register(CreateModelCommand());
        registry.Register(CreateSessionsCommand());
        BranchCommands.RegisterAll(registry);
        registry.Register(CreateProvidersCommand());
        registry.Register(CreateConfigCommand());
        AgentCommands.RegisterAll(registry);
        registry.Register(CreateHelpCommand());
        registry.Register(CreateExitCommand());
    }

    private static SlashCommand CreateHelpCommand() => new()
    {
        Name = "help",
        AltNames = ["?"],
        Description = "Show available commands",
        AutoExecute = true,
        Action = ctx =>
        {
            ctx.UIRenderer?.ShowHelp();
            return Task.FromResult(CommandResult.Ok());
        }
    };

    private static SlashCommand CreateSessionsCommand() => new()
    {
        Name = "sessions",
        AltNames = ["history"],
        Description = "Browse and switch sessions",
        AutoExecute = true,
        Action = async ctx =>
        {
            if (!ctx.Data.TryGetValue("HttpClient", out var hcObj) || hcObj is not HttpClient http)
                return CommandResult.Error("HTTP client not available");

            if (ctx.UIRenderer is null)
                return CommandResult.Error("Renderer not available");

            var activeSessionId = ctx.Data.TryGetValue("SessionId", out var sid) ? sid?.ToString() : null;

            try
            {
                var result = await SessionBrowserCommand.RunAsync(
                    http, ctx.UIRenderer,
                    activeSessionId: activeSessionId,
                    ct: ctx.CancellationToken);
                if (result is null) return CommandResult.Ok();

                if (result.NewSession || result.DeletedActiveSession)
                {
                    ctx.Data["ShouldCreateNewSession"] = true;
                    return CommandResult.Ok();
                }

                ctx.Data["SwitchSessionId"] = result.SessionId;
                ctx.Data["SwitchBranchId"]  = result.BranchId;
                return CommandResult.Ok();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return CommandResult.Error($"Session browser failed: {ex.Message}");
            }
        }
    };

    private static SlashCommand CreateProvidersCommand() => new()
    {
        Name = "providers",
        Description = "View and manage AI provider connections",
        AutoExecute = true,
        Action = async ctx =>
        {
            IProviderOperations ops;

            if (ShellConfig.RemoteServerUrl is not null
                && ctx.Data.TryGetValue("HttpClient", out var hcObj)
                && hcObj is HttpClient remoteHttp)
            {
                // Remote server — route through HTTP API.
                ops = new RemoteProviderOperations(remoteHttp);
            }
            else if (ctx.Data.TryGetValue("AuthManager", out var amObj)
                && amObj is HPDOS.Core.Auth.AuthManager localAuthManager)
            {
                // Local mode — run OAuth flows in-process so browser callbacks work.
                ops = new LocalProviderOperations(localAuthManager);
            }
            else if (ctx.Data.TryGetValue("HttpClient", out var localHcObj)
                && localHcObj is HttpClient localHttp)
            {
                // Fallback: no AuthManager in context, use HTTP.
                ops = new RemoteProviderOperations(localHttp);
            }
            else
            {
                return CommandResult.Error("HTTP client not available");
            }

            var optionsStore = ctx.Data.TryGetValue("ProviderOptionsStore", out var posObj)
                && posObj is HPDOS.Core.Shell.ProviderOptionsStore pos ? pos : null;

            try
            {
                await ProviderSetupFlow.RunAsync(ops, ctx.CancellationToken, optionsStore);
            }
            catch (Exception ex)
            {
                return CommandResult.Error($"Provider setup failed: {ex.Message}");
            }

            return CommandResult.Ok();
        }
    };

    private static SlashCommand CreateModelCommand() => new()
    {
        Name = "model",
        Description = "Switch the AI model for this session",
        AutoExecute = true,
        Action = async ctx =>
        {
            if (!ctx.Data.TryGetValue("HttpClient", out var hcObj) || hcObj is not HttpClient http)
                return CommandResult.Error("HTTP client not available");

            var sessionId = ctx.Data.TryGetValue("SessionId", out var sid) ? sid?.ToString() : null;

            // Show current
            var currentProvider = ctx.Data.TryGetValue("ProviderKey", out var pk) ? pk?.ToString() : null;
            var currentModel    = ctx.Data.TryGetValue("ModelId",    out var mk) ? mk?.ToString() : null;
            if (currentProvider != null && currentModel != null)
                AnsiConsole.MarkupLine($"[dim]Current:[/] [cyan]{Markup.Escape(currentProvider)}[/] / [cyan]{Markup.Escape(currentModel)}[/]");

            // Fetch providers
            List<AuthSummary>? summaries = null;
            try
            {
                summaries = await http.GetFromJsonAsync<List<AuthSummary>>("/api/providers", HpdosJsonOptions.Http);
            }
            catch (Exception ex)
            {
                return CommandResult.Error($"Failed to fetch providers: {ex.Message}");
            }

            var connected    = summaries?.Where(s => s.IsAuthenticated && !s.IsExpired).ToList() ?? [];
            var notConnected = summaries?.Where(s => !s.IsAuthenticated || s.IsExpired).ToList() ?? [];

            if (connected.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No providers connected. Use [bold]/providers[/] to connect one.[/]");
                return CommandResult.Ok();
            }

            // Build provider choices: connected + disabled placeholders for disconnected.
            // Spectre SelectionPrompt doesn't support disabled items natively, so only show connected.
            string selectedProviderId;
            if (connected.Count == 1)
            {
                selectedProviderId = connected[0].ProviderId;
                AnsiConsole.MarkupLine($"[dim]Provider:[/] {Markup.Escape(connected[0].DisplayName)}");
            }
            else
            {
                var providerPrompt = new SelectionPrompt<string>()
                    .Title("Select [cyan]provider[/]:")
                    .UseConverter(id => summaries!.FirstOrDefault(s => s.ProviderId == id)?.DisplayName ?? id)
                    .AddChoices(connected.Select(s => s.ProviderId));
                try { selectedProviderId = await providerPrompt.ShowAsync(AnsiConsole.Console, ctx.CancellationToken); }
                catch (OperationCanceledException) { throw; }
            }

            // Fetch static model list
            List<ModelInfo>? models = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching models\u2026", async _ =>
                {
                    try
                    {
                        models = await http.GetFromJsonAsync<List<ModelInfo>>(
                            $"/api/providers/{selectedProviderId}/models", HpdosJsonOptions.Http);
                    }
                    catch { /* fall through to empty list */ }
                });

            models ??= [];

            if (models.Count == 0)
            {
                // No known models — free-text entry
                var manualId = AnsiConsole.Ask<string>("Enter model ID:");
                if (string.IsNullOrWhiteSpace(manualId)) return CommandResult.Ok();
                await ApplyModelAsync(http, ctx, sessionId, selectedProviderId, manualId.Trim());
                return CommandResult.Ok();
            }

            // Build grouped selection from static list
            var recommended = models.Where(m => m.IsRecommended).ToList();
            var listed      = models.Where(m => !m.IsRecommended).ToList();

            var selectedProviderSummary = summaries?.FirstOrDefault(s => s.ProviderId == selectedProviderId);
            var providerSupportsFreeSearch = selectedProviderSummary?.SupportsFreeModels ?? false;

            var prompt = new SelectionPrompt<string>()
                .Title("Select [cyan]model[/]:")
                .UseConverter(id => id switch
                {
                    "__search_all__"  => "[dim]Search All — all available models[/]",
                    "__search_free__" => "[dim]Search Free — free /:free models[/]",
                    "__custom__"      => "[dim]Enter model ID manually…[/]",
                    _ => FormatModel(models.FirstOrDefault(x => x.Id == id), id)
                });

            if (recommended.Count > 0)
                prompt.AddChoiceGroup("Recommended", recommended.Select(m => m.Id));
            if (listed.Count > 0)
                prompt.AddChoiceGroup("Models", listed.Select(m => m.Id));

            string[] otherChoices = providerSupportsFreeSearch
                ? ["__search_all__", "__search_free__", "__custom__"]
                : ["__search_all__", "__custom__"];
            prompt.AddChoiceGroup("Other", otherChoices);

            string selectedModelId;
            try { selectedModelId = await prompt.ShowAsync(AnsiConsole.Console, ctx.CancellationToken); }
            catch (OperationCanceledException) { throw; }

            if (selectedModelId is "__search_all__" or "__search_free__")
            {
                var isFreeSearch = selectedModelId == "__search_free__";
                var liveUrl = isFreeSearch
                    ? $"/api/providers/{selectedProviderId}/models?live=true&filter=free"
                    : $"/api/providers/{selectedProviderId}/models?live=true";
                var liveTitle = isFreeSearch ? "Fetching free models\u2026" : "Fetching all models\u2026";

                List<ModelInfo>? liveModels = null;
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync(liveTitle, async _ =>
                    {
                        try { liveModels = await http.GetFromJsonAsync<List<ModelInfo>>(liveUrl, HpdosJsonOptions.Http); }
                        catch { }
                    });

                liveModels ??= models;

                var livePrompt = new SelectionPrompt<string>()
                    .Title(isFreeSearch
                        ? "Select [cyan]model[/] [dim](free)[/]:"
                        : "Select [cyan]model[/] [dim](all available)[/]:")
                    .PageSize(20)
                    .UseConverter(id => id == "__custom__"
                        ? "[dim]Enter model ID manually…[/]"
                        : FormatModel(liveModels.FirstOrDefault(x => x.Id == id), id));

                var liveRec  = liveModels.Where(m => m.IsRecommended).ToList();
                var livePaid = liveModels.Where(m => !m.IsRecommended && !m.IsFree).ToList();
                var liveFree = liveModels.Where(m => !m.IsRecommended && m.IsFree).ToList();
                if (liveRec.Count > 0)  livePrompt.AddChoiceGroup("Recommended", liveRec.Select(m => m.Id));
                if (livePaid.Count > 0) livePrompt.AddChoiceGroup("Models", livePaid.Select(m => m.Id));
                if (liveFree.Count > 0) livePrompt.AddChoiceGroup("Free", liveFree.Select(m => m.Id));
                livePrompt.AddChoiceGroup("Other", ["__custom__"]);

                try { selectedModelId = await livePrompt.ShowAsync(AnsiConsole.Console, ctx.CancellationToken); }
                catch (OperationCanceledException) { throw; }
            }

            if (selectedModelId == "__custom__")
            {
                var customPrompt = new TextPrompt<string>("Enter model ID:");
                try { selectedModelId = await customPrompt.ShowAsync(AnsiConsole.Console, ctx.CancellationToken); }
                catch (OperationCanceledException) { throw; }
            }

            if (string.IsNullOrWhiteSpace(selectedModelId)) return CommandResult.Ok();

            await ApplyModelAsync(http, ctx, sessionId, selectedProviderId, selectedModelId);
            return CommandResult.Ok();
        }
    };

    private static async Task ApplyModelAsync(
        HttpClient http,
        CommandContext ctx,
        string? sessionId,
        string providerKey,
        string modelId)
    {
        // Update local context state
        ctx.Data["ProviderKey"] = providerKey;
        ctx.Data["ModelId"]     = modelId;

        // Patch session metadata
        if (sessionId != null)
        {
            try
            {
                await http.PatchAsJsonAsync($"/sessions/{sessionId}",
                    new ModelSessionPatch(new ModelSessionMetadata(providerKey, modelId)),
                    HpdosJsonOptions.Http);
            }
            catch { /* non-fatal */ }
        }

        // Patch global defaults
        try
        {
            await http.PatchAsJsonAsync("/api/defaults",
                new DefaultsPatchDto(providerKey, modelId),
                HpdosJsonOptions.Http);
        }
        catch { /* non-fatal */ }

        ctx.UIRenderer?.SetModelInfo(providerKey, modelId);
        AnsiConsole.MarkupLine($"[green]\u2713[/] Model set to [cyan]{Markup.Escape(providerKey)}[/] / [cyan]{Markup.Escape(modelId)}[/]");
    }

    private static string FormatModel(ModelInfo? m, string fallbackId)
    {
        if (m is null) return fallbackId;
        var suffix = m.IsFree ? " [dim](free)[/]" : "";
        var tools  = m.IsFree && !m.SupportsTools ? " [dim][yellow]\u26a0 limited tools[/][/]" : "";
        return $"{Markup.Escape(m.Description ?? m.Id)}{suffix}{tools}";
    }

    // ── /config row definitions ───────────────────────────────────────────────

    private enum ConfigRowType { Text, Enum, Bool, Action }
    private record ConfigRow(string Key, string Label, string Hint, ConfigRowType Type, string[]? EnumValues = null);

    private static readonly ConfigRow[] _configRows =
    [
        new("temperature",                  "Temperature",                   "Enter a value (0.0–2.0)",          ConfigRowType.Text),
        new("maxOutputTokens",              "Max output tokens",             "Enter a value",                    ConfigRowType.Text),
        new("topP",                         "Top-P",                         "Enter a value (0.0–1.0)",          ConfigRowType.Text),
        new("frequencyPenalty",             "Frequency penalty",             "Enter a value (-2.0–2.0)",         ConfigRowType.Text),
        new("presencePenalty",              "Presence penalty",              "Enter a value (-2.0–2.0)",         ConfigRowType.Text),
        new("reasoningEffort",              "Reasoning effort",              "Space to cycle",                   ConfigRowType.Enum,
            ["default", "none", "low", "medium", "high", "extra-high"]),
        new("additionalSystemInstructions", "Additional system instructions","Enter to edit",                    ConfigRowType.Text),
        new("skipTools",                    "Skip tools",                    "Space to toggle",                  ConfigRowType.Bool),
        new("resetAll",                     "Reset all to defaults",         "",                                 ConfigRowType.Action),
    ];

    private static IRenderable BuildConfigPanel(SessionRunConfig cfg, int selected)
    {
        var table = new Table()
            .NoBorder()
            .HideHeaders()
            .Expand()
            .AddColumn(new TableColumn("").NoWrap())
            .AddColumn(new TableColumn("").NoWrap())
            .AddColumn(new TableColumn("").Width(30));

        for (var i = 0; i < _configRows.Length; i++)
        {
            var row = _configRows[i];
            var isSelected = i == selected;

            var cursor = isSelected ? "[bold cyan]▶[/]" : "  ";

            string label = row.Key == "resetAll"
                ? (isSelected ? "[bold red]Reset all to defaults[/]" : "[dim red]Reset all to defaults[/]")
                : (isSelected ? $"[bold]{row.Label}[/]" : $"[dim]{row.Label}[/]");

            string value = row.Key switch
            {
                "temperature"                  => cfg.Temperature     is not null ? $"[cyan]{Markup.Escape(cfg.Temperature.ToString()!)}[/]"      : "[dim]default[/]",
                "maxOutputTokens"              => cfg.MaxOutputTokens is not null ? $"[cyan]{Markup.Escape(cfg.MaxOutputTokens.ToString()!)}[/]"  : "[dim]default[/]",
                "topP"                         => cfg.TopP            is not null ? $"[cyan]{Markup.Escape(cfg.TopP.ToString()!)}[/]"             : "[dim]default[/]",
                "frequencyPenalty"             => cfg.FrequencyPenalty is not null ? $"[cyan]{Markup.Escape(cfg.FrequencyPenalty.ToString()!)}[/]" : "[dim]default[/]",
                "presencePenalty"              => cfg.PresencePenalty  is not null ? $"[cyan]{Markup.Escape(cfg.PresencePenalty.ToString()!)}[/]"  : "[dim]default[/]",
                "reasoningEffort"              => cfg.ReasoningEffort  is not null ? $"[cyan]{Markup.Escape(cfg.ReasoningEffort)}[/]"              : "[dim]default[/]",
                "additionalSystemInstructions" => cfg.AdditionalSystemInstructions is not null
                    ? $"[cyan]{Markup.Escape(cfg.AdditionalSystemInstructions.Length > 35 ? cfg.AdditionalSystemInstructions[..35] + "…" : cfg.AdditionalSystemInstructions)}[/]"
                    : "[dim]none[/]",
                "skipTools"                    => cfg.SkipTools ? "[cyan]yes[/]" : "[dim]no[/]",
                _                              => "",
            };

            string hint = isSelected && !string.IsNullOrEmpty(row.Hint) ? $"[dim italic]{Markup.Escape(row.Hint)}[/]" : "";

            table.AddRow(new Markup(cursor), new Markup(label), new Markup(value));
            if (isSelected && !string.IsNullOrEmpty(hint))
                table.AddRow(new Markup(""), new Markup(hint), new Markup(""));
        }

        return new Panel(table)
            .Header("[bold] Session Config [/]")
            .BorderColor(Color.Cyan1)
            .Expand();
    }

    private static SlashCommand CreateConfigCommand() => new()
    {
        Name = "config",
        Description = "Configure run parameters for this session",
        AutoExecute = true,
        Action = async ctx =>
        {
            var runConfig = ctx.Data.TryGetValue("RunConfig", out var rcObj) && rcObj is SessionRunConfig rc
                ? rc
                : new SessionRunConfig();

            var selected = 0;
            var lastHeight = 0; // tracks how many lines we drew so we can erase them

            while (true)
            {
                // ── Erase previous render ─────────────────────────────────────
                if (lastHeight > 0)
                {
                    // Move cursor up lastHeight lines, then clear each line downward.
                    System.Console.Write($"\x1b[{lastHeight}A");
                    for (var i = 0; i < lastHeight; i++)
                        System.Console.Write("\x1b[2K\n");
                    System.Console.Write($"\x1b[{lastHeight}A");
                }

                // ── Render panel via Spectre (not Live — just plain Write) ────
                var panel = BuildConfigPanel(runConfig, selected);
                // Capture the rendered height so we can erase it next iteration.
                using var sw = new System.IO.StringWriter();
                var recorder = AnsiConsole.Create(new AnsiConsoleSettings
                {
                    Out = new AnsiConsoleOutput(sw),
                    ColorSystem = ColorSystemSupport.Detect,
                });
                recorder.Write(panel);
                var rendered = sw.ToString();
                lastHeight = rendered.Count(c => c == '\n');
                System.Console.Write(rendered);

                // ── Read a single key (raw, no Spectre involvement) ───────────
                ConsoleKeyInfo key;
                try { key = await Task.Run(() => System.Console.ReadKey(intercept: true), ctx.CancellationToken); }
                catch (OperationCanceledException) { break; }

                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        selected = (selected - 1 + _configRows.Length) % _configRows.Length;
                        break;

                    case ConsoleKey.DownArrow:
                        selected = (selected + 1) % _configRows.Length;
                        break;

                    case ConsoleKey.Spacebar:
                    {
                        var row = _configRows[selected];
                        if (row.Type != ConfigRowType.Text)
                        {
                            CycleOrToggleInPlace(row, runConfig, ref runConfig);
                            ctx.Data["RunConfig"] = runConfig;
                        }
                        break;
                    }

                    case ConsoleKey.Enter:
                    {
                        var row = _configRows[selected];
                        if (row.Type == ConfigRowType.Text)
                        {
                            // Erase panel, run prompt, then loop back to redraw.
                            if (lastHeight > 0)
                            {
                                System.Console.Write($"\x1b[{lastHeight}A");
                                for (var i = 0; i < lastHeight; i++)
                                    System.Console.Write("\x1b[2K\n");
                                System.Console.Write($"\x1b[{lastHeight}A");
                                lastHeight = 0;
                            }
                            var edited = await RunConfigEditAsync(row.Key, runConfig, ctx.CancellationToken);
                            if (edited) ctx.Data["RunConfig"] = runConfig;
                        }
                        else
                        {
                            CycleOrToggleInPlace(row, runConfig, ref runConfig);
                            ctx.Data["RunConfig"] = runConfig;
                        }
                        break;
                    }

                    case ConsoleKey.Escape:
                        // Erase and exit.
                        if (lastHeight > 0)
                        {
                            System.Console.Write($"\x1b[{lastHeight}A");
                            for (var i = 0; i < lastHeight; i++)
                                System.Console.Write("\x1b[2K\n");
                            System.Console.Write($"\x1b[{lastHeight}A");
                        }
                        return CommandResult.Ok();
                }
            }

            return CommandResult.Ok();
        }
    };

    private static void CycleOrToggleInPlace(ConfigRow row, SessionRunConfig cfg, ref SessionRunConfig runConfig)
    {
        switch (row.Key)
        {
            case "skipTools":
                cfg.SkipTools = !cfg.SkipTools;
                break;

            case "resetAll":
                // Replace the object — caller must update runConfig ref too.
                runConfig = new SessionRunConfig();
                break;

            case "reasoningEffort":
                var effortValues = row.EnumValues!;
                var currentEffort = cfg.ReasoningEffort ?? "default";
                var effortIdx = Array.IndexOf(effortValues, currentEffort);
                var nextEffort = effortValues[(effortIdx + 1) % effortValues.Length];
                cfg.ReasoningEffort = nextEffort == "default" ? null : nextEffort;
                break;
        }
    }

    private static async Task<bool> RunConfigEditAsync(string key, SessionRunConfig cfg, CancellationToken ct)
    {
        switch (key)
        {
            case "temperature":
                var temp = await new TextPrompt<string>($"[bold]Temperature[/] [dim](0.0–2.0, blank=clear):[/]")
                    .AllowEmpty().ShowAsync(AnsiConsole.Console, ct);
                if (string.IsNullOrWhiteSpace(temp)) { cfg.Temperature = null; return true; }
                if (double.TryParse(temp, out var td)) { cfg.Temperature = td; return true; }
                return false;

            case "maxOutputTokens":
                var maxTok = await new TextPrompt<string>($"[bold]Max output tokens[/] [dim](blank=clear):[/]")
                    .AllowEmpty().ShowAsync(AnsiConsole.Console, ct);
                if (string.IsNullOrWhiteSpace(maxTok)) { cfg.MaxOutputTokens = null; return true; }
                if (int.TryParse(maxTok, out var mtd)) { cfg.MaxOutputTokens = mtd; return true; }
                return false;

            case "topP":
                var topP = await new TextPrompt<string>($"[bold]Top-P[/] [dim](0.0–1.0, blank=clear):[/]")
                    .AllowEmpty().ShowAsync(AnsiConsole.Console, ct);
                if (string.IsNullOrWhiteSpace(topP)) { cfg.TopP = null; return true; }
                if (double.TryParse(topP, out var tpd)) { cfg.TopP = tpd; return true; }
                return false;

            case "frequencyPenalty":
                var freqP = await new TextPrompt<string>($"[bold]Frequency penalty[/] [dim](-2.0–2.0, blank=clear):[/]")
                    .AllowEmpty().ShowAsync(AnsiConsole.Console, ct);
                if (string.IsNullOrWhiteSpace(freqP)) { cfg.FrequencyPenalty = null; return true; }
                if (double.TryParse(freqP, out var fpd)) { cfg.FrequencyPenalty = fpd; return true; }
                return false;

            case "presencePenalty":
                var presP = await new TextPrompt<string>($"[bold]Presence penalty[/] [dim](-2.0–2.0, blank=clear):[/]")
                    .AllowEmpty().ShowAsync(AnsiConsole.Console, ct);
                if (string.IsNullOrWhiteSpace(presP)) { cfg.PresencePenalty = null; return true; }
                if (double.TryParse(presP, out var ppd)) { cfg.PresencePenalty = ppd; return true; }
                return false;

            case "reasoningEffort":
                var effort = await new SelectionPrompt<string>()
                    .Title("[bold]Reasoning effort:[/]")
                    .AddChoices("default", "none", "low", "medium", "high", "extra-high")
                    .ShowAsync(AnsiConsole.Console, ct);
                cfg.ReasoningEffort = effort == "default" ? null : effort;
                return true;

            case "additionalSystemInstructions":
                var instr = await new TextPrompt<string>($"[bold]Additional system instructions[/] [dim](blank=clear):[/]")
                    .AllowEmpty().ShowAsync(AnsiConsole.Console, ct);
                cfg.AdditionalSystemInstructions = string.IsNullOrWhiteSpace(instr) ? null : instr;
                return true;
        }
        return false;
    }

    private static SlashCommand CreateExitCommand() => new()
    {
        Name = "exit",
        AltNames = ["quit", "q"],
        Description = "Exit the chat",
        AutoExecute = true,
        Action = _ => Task.FromResult(CommandResult.Exit("Goodbye!"))
    };
}
