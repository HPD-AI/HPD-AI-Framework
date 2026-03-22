using HPDOS.Core.Shell;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace HPDOS.Shell.Cli.TUI;

/// <summary>
/// Per-provider "Configure" TUI — shows current options and lets the user
/// edit them. All values are persisted to ProviderOptionsStore (provider-options.json)
/// and injected into Chat.AdditionalProperties on every stream request.
/// </summary>
public static class ProviderConfigFlow
{
    /// <summary>Returns true if this provider has any configurable options defined.</summary>
    public static bool HasOptions(string providerId) => GetDefinitions(providerId).Count > 0;

    public static async Task RunAsync(
        IConsoleSession session,
        string providerId,
        string displayName,
        ProviderOptionsStore store,
        CancellationToken ct)
    {
        var defs = GetDefinitions(providerId);
        if (defs.Count == 0)
        {
            session.MarkupLine($"[dim]No configurable options for {Markup.Escape(displayName)}.[/]");
            return;
        }

        // Rows = one per option + Reset + Done
        var rowKeys = defs.Select(d => d.Key).Append("__reset__").Append("__done__").ToArray();
        var selected = 0;
        var lastHeight = 0;

        while (!ct.IsCancellationRequested)
        {
            var current = store.GetOptions(providerId);

            // ── Erase previous render ─────────────────────────────────────────
            if (lastHeight > 0)
            {
                System.Console.Write($"\x1b[{lastHeight}A");
                for (var i = 0; i < lastHeight; i++)
                    System.Console.Write("\x1b[2K\n");
                System.Console.Write($"\x1b[{lastHeight}A");
            }

            // ── Render panel ──────────────────────────────────────────────────
            var panel = BuildProviderPanel(displayName, defs, current, rowKeys, selected);
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

            // ── Read key ──────────────────────────────────────────────────────
            ConsoleKeyInfo key;
            try { key = await Task.Run(() => System.Console.ReadKey(intercept: true), ct); }
            catch (OperationCanceledException) { break; }

            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    selected = (selected - 1 + rowKeys.Length) % rowKeys.Length;
                    break;

                case ConsoleKey.DownArrow:
                    selected = (selected + 1) % rowKeys.Length;
                    break;

                case ConsoleKey.Spacebar:
                {
                    var rowKey = rowKeys[selected];
                    if (rowKey == "__done__") goto done;
                    if (rowKey == "__reset__") { await ResetAsync(session, store, providerId, lastHeight); lastHeight = 0; break; }
                    var def = defs.First(d => d.Key == rowKey);
                    // Bool and Enum: cycle in-place. Text/Int/Float: ignore Space (Enter opens prompt).
                    if (def.Type == OptionType.Bool)
                    {
                        current.TryGetValue(rowKey, out var cv);
                        var curBool = cv is bool b ? b : cv is long l ? l != 0 : (bool?)null;
                        // cycle: null → true → false → null
                        var nextBool = curBool is null ? true : curBool == true ? false : (bool?)null;
                        await store.SetOptionAsync(providerId, rowKey, nextBool.HasValue ? (object)nextBool.Value : null);
                    }
                    else if (def.Type == OptionType.Enum && def.EnumValues is { } evs)
                    {
                        current.TryGetValue(rowKey, out var cv);
                        var curStr = cv?.ToString() ?? "";
                        var idx = Array.IndexOf(evs, curStr);
                        // advance: -1/last → 0, wrap through, then wrap past last back to null (default)
                        var nextIdx = (idx + 1) % (evs.Length + 1); // +1 slot = "clear"
                        await store.SetOptionAsync(providerId, rowKey, nextIdx < evs.Length ? evs[nextIdx] : null);
                    }
                    break;
                }

                case ConsoleKey.Enter:
                {
                    var rowKey = rowKeys[selected];
                    if (rowKey == "__done__") goto done;
                    if (rowKey == "__reset__") { await ResetAsync(session, store, providerId, lastHeight); lastHeight = 0; break; }

                    // Erase panel before showing sub-prompt.
                    EraseLines(lastHeight);
                    lastHeight = 0;

                    var def = defs.First(d => d.Key == rowKey);
                    current.TryGetValue(rowKey, out var existingRaw);
                    var existing = existingRaw is System.Text.Json.JsonElement el
                        ? ProviderOptionsStore.Coerce(new Dictionary<string, object> { ["v"] = el })["v"]
                        : existingRaw;
                    await EditOptionAsync(session, store, providerId, def, existing, ct);
                    break;
                }

                case ConsoleKey.Escape:
                    goto done;
            }
        }

        done:
        EraseLines(lastHeight);
    }

    private static void EraseLines(int count)
    {
        if (count <= 0) return;
        System.Console.Write($"\x1b[{count}A");
        for (var i = 0; i < count; i++)
            System.Console.Write("\x1b[2K\n");
        System.Console.Write($"\x1b[{count}A");
    }

    private static async Task ResetAsync(IConsoleSession session, ProviderOptionsStore store, string providerId, int lastHeight)
    {
        EraseLines(lastHeight);
        await store.ClearAsync(providerId);
        session.MarkupLine("[green]✓ Options reset.[/]");
    }

    // ── Panel display ─────────────────────────────────────────────────────────

    private static IRenderable BuildProviderPanel(
        string displayName,
        List<OptionDef> defs,
        Dictionary<string, object> current,
        string[] rowKeys,
        int selected)
    {
        var table = new Table()
            .NoBorder()
            .HideHeaders()
            .Expand()
            .AddColumn(new TableColumn("").NoWrap())
            .AddColumn(new TableColumn("").NoWrap())
            .AddColumn(new TableColumn("").NoWrap())
            .AddColumn(new TableColumn("").Width(28));

        for (var i = 0; i < rowKeys.Length; i++)
        {
            var rowKey = rowKeys[i];
            var isSel = i == selected;
            var cursor = isSel ? "[bold cyan]▶[/]" : "  ";

            if (rowKey == "__reset__")
            {
                table.AddRow(
                    new Markup(cursor),
                    new Markup(isSel ? "[bold red]Reset all to defaults[/]" : "[dim red]Reset all to defaults[/]"),
                    new Markup(""), new Markup(""));
                continue;
            }
            if (rowKey == "__done__")
            {
                table.AddRow(
                    new Markup(cursor),
                    new Markup(isSel ? "[bold]Done[/]" : "[dim]Done[/]"),
                    new Markup(""), new Markup(""));
                continue;
            }

            var def = defs.First(d => d.Key == rowKey);
            var label = isSel ? $"[bold]{Markup.Escape(def.Label)}[/]" : $"[dim]{Markup.Escape(def.Label)}[/]";

            string valueStr;
            if (current.TryGetValue(def.Key, out var raw))
            {
                var coerced = raw is System.Text.Json.JsonElement je
                    ? ProviderOptionsStore.Coerce(new Dictionary<string, object> { ["v"] = je })["v"]
                    : raw;
                valueStr = $"[cyan]{Markup.Escape(FormatValue(coerced))}[/]";
            }
            else
            {
                valueStr = $"[dim]{Markup.Escape(def.DefaultDisplay)}[/]";
            }

            var hint = isSel ? $"[dim italic]{Markup.Escape(def.Description)}[/]" : "";

            table.AddRow(new Markup(cursor), new Markup(label), new Markup(valueStr), new Markup(""));
            if (isSel)
                table.AddRow(new Markup(""), new Markup(hint), new Markup(""), new Markup(""));
        }

        return new Panel(table)
            .Header($"[bold] Configure {Markup.Escape(displayName)} [/]")
            .BorderColor(Color.Cyan1)
            .Expand();
    }

    private static string FormatValue(object? v) => v switch
    {
        null   => "default",
        bool b => b ? "on" : "off",
        _      => v.ToString() ?? "default"
    };

    // ── Edit a single option ──────────────────────────────────────────────────

    private static async Task EditOptionAsync(
        IConsoleSession session,
        ProviderOptionsStore store,
        string providerId,
        OptionDef def,
        object? current,
        CancellationToken ct)
    {
        session.WriteLine();
        session.MarkupLine($"[bold]{Markup.Escape(def.Label)}[/]  [dim]{Markup.Escape(def.Description)}[/]");
        if (def.HintText is not null)
            session.MarkupLine($"[dim]{Markup.Escape(def.HintText)}[/]");
        session.WriteLine();

        try
        {
            switch (def.Type)
            {
                case OptionType.Bool:
                    await EditBoolAsync(session, store, providerId, def, current, ct);
                    break;

                case OptionType.Int:
                    await EditIntAsync(session, store, providerId, def, current, ct);
                    break;

                case OptionType.Float:
                    await EditFloatAsync(session, store, providerId, def, current, ct);
                    break;

                case OptionType.String:
                    await EditStringAsync(session, store, providerId, def, current, ct);
                    break;

                case OptionType.Enum:
                    await EditEnumAsync(session, store, providerId, def, current, ct);
                    break;
            }
        }
        catch (OperationCanceledException) { }
    }

    private static async Task EditBoolAsync(
        IConsoleSession session, ProviderOptionsStore store, string providerId, OptionDef def, object? current, CancellationToken ct)
    {
        var currentBool = current is bool b ? b : current is long l ? l != 0 : false;
        var choices = new List<string> { "on", "off", "default (clear)" };
        var prompt = new SelectionPrompt<string>()
            .Title($"[dim]Current:[/] {(current is null ? "default" : currentBool ? "on" : "off")}")
            .AddChoices(choices);

        var picked = await prompt.ShowAsync(session.Console, ct);
        if (picked == "default (clear)")
            await store.SetOptionAsync(providerId, def.Key, null);
        else
            await store.SetOptionAsync(providerId, def.Key, picked == "on");

        session.MarkupLine("[green]✓ Saved.[/]");
    }

    private static async Task EditIntAsync(
        IConsoleSession session, ProviderOptionsStore store, string providerId, OptionDef def, object? current, CancellationToken ct)
    {
        var currentStr = current is null ? "" : Convert.ToInt64(current).ToString();
        var hint = def.Range is not null ? $" [dim]({def.Range})[/]" : "";
        var prompt = new TextPrompt<string>($"Enter value{hint} [dim](empty = clear/default):[/]")
            .AllowEmpty()
            .DefaultValue(currentStr);

        var input = await prompt.ShowAsync(session.Console, ct);
        if (string.IsNullOrWhiteSpace(input))
            await store.SetOptionAsync(providerId, def.Key, null);
        else if (long.TryParse(input.Trim(), out var v))
            await store.SetOptionAsync(providerId, def.Key, v);
        else
        {
            session.MarkupLine("[red]Invalid number.[/]");
            return;
        }
        session.MarkupLine("[green]✓ Saved.[/]");
    }

    private static async Task EditFloatAsync(
        IConsoleSession session, ProviderOptionsStore store, string providerId, OptionDef def, object? current, CancellationToken ct)
    {
        var currentStr = current is null ? "" : Convert.ToDouble(current).ToString("G");
        var hint = def.Range is not null ? $" [dim]({def.Range})[/]" : "";
        var prompt = new TextPrompt<string>($"Enter value{hint} [dim](empty = clear/default):[/]")
            .AllowEmpty()
            .DefaultValue(currentStr);

        var input = await prompt.ShowAsync(session.Console, ct);
        if (string.IsNullOrWhiteSpace(input))
            await store.SetOptionAsync(providerId, def.Key, null);
        else if (double.TryParse(input.Trim(), out var v))
            await store.SetOptionAsync(providerId, def.Key, v);
        else
        {
            session.MarkupLine("[red]Invalid number.[/]");
            return;
        }
        session.MarkupLine("[green]✓ Saved.[/]");
    }

    private static async Task EditStringAsync(
        IConsoleSession session, ProviderOptionsStore store, string providerId, OptionDef def, object? current, CancellationToken ct)
    {
        var currentStr = current?.ToString() ?? "";
        var prompt = new TextPrompt<string>("Enter value [dim](empty = clear/default):[/]")
            .AllowEmpty()
            .DefaultValue(currentStr);

        var input = await prompt.ShowAsync(session.Console, ct);
        if (string.IsNullOrWhiteSpace(input))
            await store.SetOptionAsync(providerId, def.Key, null);
        else
            await store.SetOptionAsync(providerId, def.Key, input.Trim());

        session.MarkupLine("[green]✓ Saved.[/]");
    }

    private static async Task EditEnumAsync(
        IConsoleSession session, ProviderOptionsStore store, string providerId, OptionDef def, object? current, CancellationToken ct)
    {
        var currentStr = current?.ToString() ?? "";
        var choices = def.EnumValues!.Append("(clear / default)").ToList();
        var prompt = new SelectionPrompt<string>()
            .Title($"[dim]Current:[/] {(string.IsNullOrEmpty(currentStr) ? "default" : currentStr)}")
            .WrapAround(true)
            .AddChoices(choices);

        var picked = await prompt.ShowAsync(session.Console, ct);
        if (picked == "(clear / default)")
            await store.SetOptionAsync(providerId, def.Key, null);
        else
            await store.SetOptionAsync(providerId, def.Key, picked);

        session.MarkupLine("[green]✓ Saved.[/]");
    }

    // ── Option definitions per provider ───────────────────────────────────────

    private static List<OptionDef> GetDefinitions(string providerId) => providerId switch
    {
        "anthropic" =>
        [
            new("thinkingBudgetTokens", "Thinking budget (tokens)", OptionType.Int,
                "Token budget for extended thinking. Must be ≥ 1024 and < max_tokens. 0 = disabled.",
                "≥ 1024", DefaultDisplay: "disabled"),
            new("serviceTier", "Service tier", OptionType.Enum,
                "Priority tier: auto = use priority capacity if available, standard_only = standard only.",
                EnumValues: ["auto", "standard_only"], DefaultDisplay: "auto"),
            new("enablePromptCaching", "Prompt caching", OptionType.Bool,
                "Cache large contexts to reduce costs on repeated prompts.",
                DefaultDisplay: "off"),
            new("promptCacheTTLMinutes", "Cache TTL (minutes)", OptionType.Int,
                "How long cached content stays valid (1–60).",
                "1–60", DefaultDisplay: "5"),
        ],

        "openai" =>
        [
            new("reasoningEffortLevel", "Reasoning effort", OptionType.Enum,
                "Reasoning effort for o1+ models. Reducing effort = faster, fewer tokens.",
                EnumValues: ["minimal", "low", "medium", "high"], DefaultDisplay: "medium"),
            new("webSearchEnabled", "Web search", OptionType.Bool,
                "Enable web search tool for this provider.",
                DefaultDisplay: "off"),
            new("allowParallelToolCalls", "Parallel tool calls", OptionType.Bool,
                "Allow the model to call multiple tools simultaneously.",
                DefaultDisplay: "on"),
            new("serviceTier", "Service tier", OptionType.Enum,
                "Processing policy: auto or default.",
                EnumValues: ["auto", "default"], DefaultDisplay: "auto"),
            new("seed", "Seed", OptionType.Int,
                "Seed for deterministic generation (best-effort).",
                DefaultDisplay: "random"),
            new("storedOutputEnabled", "Store output", OptionType.Bool,
                "Allow OpenAI to store output for model distillation or evals.",
                DefaultDisplay: "off"),
        ],

        "google-ai" =>
        [
            new("thinkingBudget", "Thinking budget (tokens)", OptionType.Int,
                "Thinking token budget for Gemini 3+ models.",
                DefaultDisplay: "disabled"),
            new("thinkingLevel", "Thinking level", OptionType.Enum,
                "Depth of internal reasoning for Gemini 3+.",
                EnumValues: ["LOW", "HIGH"], DefaultDisplay: "unspecified"),
            new("includeThoughts", "Include thoughts in response", OptionType.Bool,
                "Return thinking traces in the response (Gemini 3+).",
                DefaultDisplay: "off"),
            new("modelRoutingPreference", "Model routing", OptionType.Enum,
                "Automatic model routing preference.",
                EnumValues: ["PRIORITIZE_QUALITY", "BALANCED", "PRIORITIZE_COST"], DefaultDisplay: "unspecified"),
            new("functionCallingMode", "Function calling mode", OptionType.Enum,
                "AUTO = model decides, ANY = must call a function, NONE = no function calls.",
                EnumValues: ["AUTO", "ANY", "NONE"], DefaultDisplay: "AUTO"),
        ],

        "mistral" =>
        [
            new("safePrompt", "Safe prompt", OptionType.Bool,
                "Inject a safety system prompt before every conversation.",
                DefaultDisplay: "off"),
            new("parallelToolCalls", "Parallel tool calls", OptionType.Bool,
                "Allow parallel function calling.",
                DefaultDisplay: "on"),
            new("randomSeed", "Random seed", OptionType.Int,
                "Seed for reproducible outputs.",
                DefaultDisplay: "random"),
        ],

        "bedrock" =>
        [
            new("enablePromptCaching", "Prompt caching", OptionType.Bool,
                "Enable prompt caching for Claude 3.5+ models via Bedrock.",
                DefaultDisplay: "off"),
            new("guardrailIdentifier", "Guardrail ID", OptionType.String,
                "Bedrock Guardrail ID for content filtering.",
                DefaultDisplay: "none"),
            new("guardrailVersion", "Guardrail version", OptionType.String,
                "Guardrail version (\"DRAFT\" or a specific version string).",
                DefaultDisplay: "none"),
            new("requestTimeoutMs", "Request timeout (ms)", OptionType.Int,
                "Request timeout in milliseconds.",
                DefaultDisplay: "provider default"),
            new("maxRetryAttempts", "Max retries", OptionType.Int,
                "Maximum retry attempts for failed requests.",
                DefaultDisplay: "provider default"),
        ],

        "ollama" =>
        [
            new("numCtx", "Context window (tokens)", OptionType.Int,
                "Size of the context window. Default: 2048.",
                DefaultDisplay: "2048"),
            new("numPredict", "Max tokens to predict", OptionType.Int,
                "Maximum tokens to generate. -1 = infinite, -2 = fill context.",
                DefaultDisplay: "128"),
            new("think", "Thinking mode", OptionType.Enum,
                "Enable extended thinking for reasoning models (qwen3, deepseek-r1, etc.).",
                EnumValues: ["true", "false", "high", "medium", "low"], DefaultDisplay: "off"),
            new("keepAlive", "Keep-alive duration", OptionType.String,
                "How long the model stays loaded (e.g. \"5m\", \"1h\", \"-1\" = forever).",
                DefaultDisplay: "5m"),
            new("repeatPenalty", "Repeat penalty", OptionType.Float,
                "Strength of repetition penalty. Default: 1.1.",
                "0.0–2.0", DefaultDisplay: "1.1"),
            new("seed", "Seed", OptionType.Int,
                "Random seed for deterministic generation. 0 = random.",
                DefaultDisplay: "random"),
        ],

        "huggingface" =>
        [
            new("maxNewTokens", "Max new tokens", OptionType.Int,
                "Number of new tokens to generate. Default: 250.",
                DefaultDisplay: "250"),
            new("doSample", "Sampling", OptionType.Bool,
                "Use sampling instead of greedy decoding.",
                DefaultDisplay: "on"),
            new("waitForModel", "Wait for model", OptionType.Bool,
                "Wait for model to load instead of returning 503.",
                DefaultDisplay: "off"),
            new("useCache", "Use inference cache", OptionType.Bool,
                "Use the HuggingFace inference API cache layer.",
                DefaultDisplay: "on"),
        ],

        "azure-ai" or "azure-ai-inference" =>
        [
            new("seed", "Seed", OptionType.Int,
                "Seed for deterministic generation (best-effort).",
                DefaultDisplay: "random"),
            new("toolChoice", "Tool choice", OptionType.Enum,
                "Control how the model uses tools.",
                EnumValues: ["auto", "none", "required"], DefaultDisplay: "auto"),
            .. (providerId == "azure-ai-inference" ? new List<OptionDef>
            {
                new("extraParametersMode", "Extra parameters mode", OptionType.Enum,
                    "How to handle unknown parameters sent to the endpoint.",
                    EnumValues: ["pass-through", "error", "drop"], DefaultDisplay: "error"),
            } : new List<OptionDef>()),
        ],

        "onnx-runtime" =>
        [
            new("numBeams", "Beam search width", OptionType.Int,
                "Beam search width. 1 = no beam search (greedy).",
                "≥ 1", DefaultDisplay: "1"),
            new("doSample", "Sampling", OptionType.Bool,
                "Use randomized sampling instead of greedy decoding.",
                DefaultDisplay: "off"),
            new("randomSeed", "Random seed", OptionType.Int,
                "RNG seed. -1 = random.",
                DefaultDisplay: "-1 (random)"),
        ],

        _ => []
    };

    // ── Model ─────────────────────────────────────────────────────────────────

    private enum OptionType { Bool, Int, Float, String, Enum }

    private record OptionDef(
        string Key,
        string Label,
        OptionType Type,
        string Description,
        string? Range = null,
        string[]? EnumValues = null,
        string DefaultDisplay = "default",
        string? HintText = null);
}
