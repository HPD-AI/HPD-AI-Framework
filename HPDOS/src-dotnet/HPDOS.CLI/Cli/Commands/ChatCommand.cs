using HPD.Agent;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Serialization;
using HPDOS.Core.Auth;
using HPDOS.Core.Shell;
using HPDOS.Shell.Cli;
using HPDOS.Shell.Cli.TUI;
using HPDOS.Shell.Cli.TUI.Commands;
using HPDOS.Shell.Shell;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace HPDOS.Shell.Cli.Commands;

/// <summary>
/// Implements the `hpdos chat` command — a full TUI REPL against the Kestrel agent API.
/// Starts Kestrel if not already running, then opens a readline loop. Each message
/// opens an SSE stream to /sessions/{sid}/branches/{bid}/stream and renders events
/// via AgentUIRenderer.
/// </summary>
public static class ChatCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        string baseUrl;
        var session = SpectreConsoleSession.CreateDefault();

        bool ownedKestrel = false;

        if (ShellConfig.RemoteServerUrl is { } remoteUrl)
        {
            // Remote mode — talk directly to the configured server, no local Kestrel needed.
            baseUrl = remoteUrl.TrimEnd('/');
        }
        // else if (await TryAttachToRunningInstanceAsync() is { } attachedUrl)
        // {
        //     // A GUI instance is already running — attach to it.
        //     baseUrl = attachedUrl;
        //     AnsiConsole.MarkupLine("[dim]Attached to running HPDOS instance.[/]");
        // }
        // else
        else
        {
            // Local mode — start our own Kestrel.
            await GUIMode.StartServerAsync();

            if (ShellConfig.Port == 0)
            {
                session.MarkupLine("[red]Failed to start server.[/]");
                return 1;
            }

            baseUrl = $"http://localhost:{ShellConfig.Port}";
            ownedKestrel = true;
        }
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        using var cts = CtrlCTokenSource.Create();

        // Create or resume a session
        var currentSession = await EnsureSessionAsync(http, session);
        if (currentSession == null) return 1;

        var sessionId = currentSession.Id;
        var branchId = "main";
        var agentId  = "default";

        // Read provider/model from session metadata, or seed from /api/defaults
        string? providerKey = null;
        string? modelId = null;

        if (currentSession.Metadata?.TryGetValue("providerKey", out var pk) == true)
            providerKey = pk?.ToString();
        if (currentSession.Metadata?.TryGetValue("modelId", out var mk) == true)
            modelId = mk?.ToString();

        if (providerKey == null || modelId == null)
        {
            try
            {
                var defaults = await http.GetFromJsonAsync<DefaultsResponseDto>("/api/defaults", HpdosJsonOptions.Http);
                if (defaults != null)
                {
                    providerKey ??= defaults.ProviderKey;
                    modelId ??= defaults.ModelId;
                    // Seed into session metadata
                    await http.PatchAsJsonAsync($"/sessions/{sessionId}",
                        new SessionMetadataPatch(new SessionMetadataDto(providerKey, modelId)),
                        HpdosJsonOptions.Http);
                }
            }
            catch { /* non-fatal — fall through with nulls */ }
        }

        // Onboarding: if no provider is connected, run setup before entering chat.
        {
            List<AuthSummary>? summaries = null;
            try { summaries = await http.GetFromJsonAsync<List<AuthSummary>>("/api/providers", HpdosJsonOptions.Http); }
            catch { /* non-fatal — treat as no providers */ }

            var hasAny = summaries?.Any(s => s.IsAuthenticated) ?? false;
            if (!hasAny)
            {
                session.Write(new Panel(
                    "No AI provider connected. Connect one to start chatting.\n" +
                    "[dim]OpenRouter has free models — no credit card needed.[/]")
                    .Header("[cyan] Setup required [/]")
                    .BorderColor(Color.Cyan1).CapToTerminal());
                session.WriteLine();

                // Re-use existing provider setup flow — same as /providers inside chat.
                IProviderOperations ops;
                if (ShellConfig.RemoteServerUrl is null && GUIMode.Services is { } svcEarly)
                    ops = new LocalProviderOperations(svcEarly.GetRequiredService<AuthManager>());
                else
                    ops = new RemoteProviderOperations(http);

                using var setupCts = CtrlCTokenSource.Create();
                await ProviderSetupFlow.ConnectProviderAsync(ops, session, preselectedId: null, setupCts.Token);
                session.WriteLine();
            }
        }

        // Set up TUI
        var renderer = new AgentUIRenderer(session);
        renderer.SetStreamContext(http, sessionId, branchId);
        if (providerKey != null && modelId != null)
            renderer.SetModelInfo(providerKey, modelId);

        var contextData = new Dictionary<string, object>
        {
            ["HttpClient"] = http,
            ["SessionId"] = sessionId,
            ["BranchId"] = branchId,
            ["AgentId"]  = agentId,
        };
        if (providerKey != null) contextData["ProviderKey"] = providerKey;
        if (modelId != null)     contextData["ModelId"]     = modelId;
        // In local mode, expose AuthManager so /providers can run OAuth in-process.
        if (ShellConfig.RemoteServerUrl is null && GUIMode.Services is { } svc)
            contextData["AuthManager"] = svc.GetRequiredService<AuthManager>();

        // Load provider-specific runtime options store.
        var providerOptionsStore = await ProviderOptionsStore.LoadAsync();
        contextData["ProviderOptionsStore"] = providerOptionsStore;

        var processor = new CommandProcessor(renderer.CommandRegistry, renderer, session, contextData);
        var input = new CommandAwareInput(processor, session);

        ShowHeader(session, sessionId);

        bool titleSet = currentSession.Metadata?.ContainsKey("title") == true;
        string? prefillText = null;

        while (true)
        {
            // ── Consume signal keys set by commands ────────────────────────────

            if (contextData.TryGetValue("SwitchSessionId", out var switchId))
            {
                contextData.Remove("SwitchSessionId");
                sessionId = switchId.ToString()!;
                branchId = contextData.TryGetValue("SwitchBranchId", out var bid)
                    ? bid.ToString()!
                    : "main";
                contextData.Remove("SwitchBranchId");
                agentId = "default";
                contextData["AgentId"] = agentId;
                renderer.SetStreamContext(http, sessionId, branchId);
                contextData["SessionId"] = sessionId;
                contextData["BranchId"] = branchId;
                titleSet = false;
            }

            // BranchId may be updated in-place by /branch commands.
            if (contextData.TryGetValue("BranchId", out var ctxBid) && ctxBid.ToString() != branchId)
            {
                branchId = ctxBid.ToString()!;
                renderer.SetStreamContext(http, sessionId, branchId);
            }

            // AgentId may be updated by /agent commands.
            if (contextData.TryGetValue("AgentId", out var ctxAid) && ctxAid.ToString() != agentId)
                agentId = ctxAid.ToString()!;

            if (contextData.TryGetValue("ShouldCreateNewSession", out _))
            {
                contextData.Remove("ShouldCreateNewSession");
                var newSession = await CreateSessionAsync(http, session);
                if (newSession != null)
                {
                    sessionId = newSession.Id;
                    branchId = "main";
                    agentId  = "default";
                    renderer.SetStreamContext(http, sessionId, branchId);
                    contextData["SessionId"] = sessionId;
                    contextData["BranchId"] = branchId;
                    contextData["AgentId"]  = agentId;
                    titleSet = false;
                    Console.Clear();
                    ShowHeader(session, sessionId);
                }
            }

            // PrefillInput: set by /branch fork — pre-fill readline with the forked message.
            if (contextData.TryGetValue("PrefillInput", out var pf))
            {
                contextData.Remove("PrefillInput");
                prefillText = pf?.ToString();
            }

            // ── Build prompt label ─────────────────────────────────────────────

            var prompt = BuildPrompt(agentId, branchId);

            // ── Read input ─────────────────────────────────────────────────────

            var line = input.ReadLine(prompt, prefillText);
            prefillText = null;
            if (line == null) break; // Ctrl+C

            line = line.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Slash command
            if (CommandProcessor.IsCommand(line))
            {
                var result = await processor.ExecuteAsync(line, cts.Token);
                if (result.ShouldExit) break;
                continue;
            }

            // Re-read from contextData in case /model command updated them
            var activeProvider = contextData.TryGetValue("ProviderKey", out var apk) ? apk?.ToString() : providerKey;
            var activeModel    = contextData.TryGetValue("ModelId",     out var amk) ? amk?.ToString() : modelId;
            var activeAgent    = contextData.TryGetValue("AgentId",     out var aaid) ? aaid?.ToString() : agentId;

            // Merge provider-specific runtime options (from /providers → Configure).
            // Values stay as JsonElement (from store load) — source-gen serializes JsonElement cleanly.
            // Server-side DtoMappingExtensions.CoerceJsonElement() unwraps them to primitives.
            Dictionary<string, object>? providerAdditional = null;
            if (activeProvider is not null)
            {
                var opts = providerOptionsStore.GetOptions(activeProvider);
                if (opts.Count > 0) providerAdditional = opts;
            }

            // Per-session run config (from /config).
            var sessionRunConfig = contextData.TryGetValue("RunConfig", out var rcObj) && rcObj is SessionRunConfig src
                ? src : null;

            // Send message and stream response
            var modelNotFound = await StreamMessageAsync(session, http, renderer, sessionId, branchId, line, activeProvider, activeModel, activeAgent, providerAdditional, sessionRunConfig);
            if (modelNotFound)
            {
                // Revert to the previous model so the user isn't stuck with a broken model
                contextData.Remove("ProviderKey");
                contextData.Remove("ModelId");
                if (providerKey != null) contextData["ProviderKey"] = providerKey;
                if (modelId != null)     contextData["ModelId"]     = modelId;
                if (providerKey != null && modelId != null)
                    renderer.SetModelInfo(providerKey, modelId);
                session.MarkupLine($"[yellow]Reverted to:[/] [cyan]{Markup.Escape(providerKey ?? "default")}[/] / [cyan]{Markup.Escape(modelId ?? "default")}[/]");
            }

            // Auto-derive session title from first user message.
            if (!titleSet && !modelNotFound)
            {
                titleSet = true;
                var title = line.Length > 50 ? line[..50] + "…" : line;
                _ = TrySetTitleAsync(http, sessionId, title);
            }
        }

        if (ownedKestrel)
            await GUIMode.StopServerAsync();
        return 0;
    }

    private static HPD.Agent.ReasoningOptions? BuildReasoningOptions(string effort) => effort switch
    {
        "none"       => new HPD.Agent.ReasoningOptions { Effort = HPD.Agent.ReasoningEffort.None },
        "low"        => new HPD.Agent.ReasoningOptions { Effort = HPD.Agent.ReasoningEffort.Low },
        "medium"     => new HPD.Agent.ReasoningOptions { Effort = HPD.Agent.ReasoningEffort.Medium },
        "high"       => new HPD.Agent.ReasoningOptions { Effort = HPD.Agent.ReasoningEffort.High },
        "extra-high" => new HPD.Agent.ReasoningOptions { Effort = HPD.Agent.ReasoningEffort.ExtraHigh },
        _            => null,
    };

    private static string BuildPrompt(string agentId, string branchId)
    {
        // Show "[agent] branch > " when either differs from the defaults.
        var agentPart  = agentId  != "default" ? $"[dim]{Markup.Escape(agentId)}[/] "  : "";
        var branchPart = branchId != "main"    ? $"[dim]{Markup.Escape(branchId)}[/] " : "";

        return (agentPart + branchPart).Length > 0
            ? agentPart + branchPart + "[cyan]>[/] "
            : "[cyan]>[/] ";
    }

    private static async Task TrySetTitleAsync(HttpClient http, string sessionId, string title)
    {
        try
        {
            await http.PatchAsJsonAsync($"/sessions/{sessionId}",
                new UpdateSessionRequest(new Dictionary<string, object?> { ["title"] = title }),
                HpdosJsonOptions.Http);
        }
        catch { /* non-fatal */ }
    }

    private static async Task<bool> StreamMessageAsync(
        IConsoleSession session,
        HttpClient http,
        AgentUIRenderer renderer,
        string sessionId,
        string branchId,
        string userMessage,
        string? providerKey = null,
        string? modelId = null,
        string? agentId = null,
        Dictionary<string, object>? providerAdditional = null,
        SessionRunConfig? sessionRunConfig = null)
    {
        using var cts = new CancellationTokenSource();

        // Watch for Escape asynchronously via Spectre's input channel.
        // This runs on its own task — no background thread competing with ReadKey.
        var escapeWatcher = Task.Run(async () =>
        {
            session.MarkupLine("[dim]Press [yellow]Escape[/] to cancel[/]");
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var key = await session.Input.ReadKeyAsync(intercept: true, cts.Token);
                    if (key?.Key == ConsoleKey.Escape)
                    {
                        session.MarkupLine("\n[yellow]⊘ Cancelled by user[/]");
                        await cts.CancelAsync();
                        return;
                    }
                }
            }
            catch (OperationCanceledException) { }
        });

        renderer.ShowUserMessage(userMessage);

        // Build Chat config — merge provider-specific options + session run config.
        var hasChat = providerAdditional is { Count: > 0 } || sessionRunConfig is { IsEmpty: false };
        ChatRunConfigDto? chatConfig = hasChat
            ? new ChatRunConfigDto(
                Temperature:      sessionRunConfig?.Temperature,
                MaxOutputTokens:  sessionRunConfig?.MaxOutputTokens,
                TopP:             sessionRunConfig?.TopP,
                FrequencyPenalty: sessionRunConfig?.FrequencyPenalty,
                PresencePenalty:  sessionRunConfig?.PresencePenalty,
                AdditionalProperties: providerAdditional is { Count: > 0 } ? providerAdditional : null,
                Reasoning: sessionRunConfig?.ReasoningEffort is { } eff ? BuildReasoningOptions(eff) : null)
            : null;

        StreamRunConfigDto? runConfig = null;
        if (providerKey != null || modelId != null || hasChat ||
            sessionRunConfig?.AdditionalSystemInstructions != null ||
            sessionRunConfig?.SkipTools == true)
        {
            runConfig = new StreamRunConfigDto(
                Chat: chatConfig,
                ProviderKey: providerKey,
                ModelId: modelId,
                AdditionalSystemInstructions: sessionRunConfig?.AdditionalSystemInstructions,
                ContextOverrides: null,
                PermissionOverrides: null,
                CoalesceDeltas: null,
                SkipTools: sessionRunConfig?.SkipTools == true ? true : null,
                RunTimeout: null);
        }

        var body = new StreamRequest(
            Messages: [new StreamMessage(userMessage, "user")],
            clientToolKits: null,
            Context: null,
            State: null,
            ExpandedContainers: null,
            HiddenTools: null,
            ResetClientState: false,
            RunConfig: runConfig,
            AgentId: agentId == "default" ? null : agentId);

        HttpResponseMessage response;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/sessions/{sessionId}/branches/{branchId}/stream")
            {
                Content = JsonContent.Create(body, options: HpdosJsonOptions.Http)
            };
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        }
        catch (OperationCanceledException)
        {
            await escapeWatcher;
            return false;
        }
        catch (Exception ex)
        {
            session.MarkupLine($"[red]Stream failed:[/] {Markup.Escape(ex.Message)}");
            await cts.CancelAsync();
            await escapeWatcher;
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            session.MarkupLine($"[red]Server error {(int)response.StatusCode}:[/] {Markup.Escape(err)}");
            await cts.CancelAsync();
            await escapeWatcher;
            return false;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        bool modelNotFound = false;
        await foreach (var evt in ReadSseEventsAsync(reader, cts.Token))
        {
            if (evt is AgentEvent agentEvt)
            {
                renderer.RenderAgentEvent(agentEvt);
                if (agentEvt is MessageTurnErrorEvent err && err.IsModelNotFound)
                    modelNotFound = true;
            }
        }

        await cts.CancelAsync();
        await escapeWatcher;
        return modelNotFound;
    }

    /// <summary>
    /// Read SSE events from the stream and deserialise them as AgentEvents.
    /// </summary>
    private static async IAsyncEnumerable<AgentEvent> ReadSseEventsAsync(
        StreamReader reader,
        [EnumeratorCancellation] CancellationToken ct)
    {
        string? eventType = null;
        var dataBuilder = new StringBuilder();

        while (!ct.IsCancellationRequested && !reader.EndOfStream)
        {
            string? line;
            try { line = await reader.ReadLineAsync(ct); }
            catch (OperationCanceledException) { yield break; }

            if (line == null) continue;

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventType = line[6..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                dataBuilder.Append(line[5..].Trim());
            }
            else if (line.Length == 0 && dataBuilder.Length > 0)
            {
                // Blank line — dispatch event
                var data = dataBuilder.ToString();
                dataBuilder.Clear();

                var agentEvt = TryDeserialiseEvent(eventType, data);
                if (agentEvt != null) yield return agentEvt;
                eventType = null;
            }
        }
    }

    private static AgentEvent? TryDeserialiseEvent(string? eventType, string data)
    {
        // AgentEventSerializer.FromJson reads the "type" discriminator from the JSON
        // and deserializes to the correct concrete AgentEvent subtype.
        return AgentEventSerializer.FromJson(data);
    }

    private static readonly FigletFont _headerFont = LoadHeaderFont();

    private static FigletFont LoadHeaderFont()
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Cli", "Fonts", "ANSIShadow.flf");
        return File.Exists(fontPath) ? FigletFont.Load(fontPath) : FigletFont.Default;
    }

    private static void ShowHeader(IConsoleSession session, string sessionId)
    {
        session.WriteLine();
        session.Write(new FigletText(_headerFont, "HPD-OS").LeftJustified().Color(new Color(0, 255, 255)));
        session.MarkupLine($"[dim]Session: {sessionId[..Math.Min(8, sessionId.Length)]}…  " +
                               "Type [cyan]/help[/] for commands, [cyan]Ctrl+C[/] to exit[/]");
        session.WriteLine();
    }

    private static async Task<SessionDto?> EnsureSessionAsync(HttpClient http, IConsoleSession session)
    {
        // Always start fresh — resume is available via /sessions
        return await CreateSessionAsync(http, session);

        // try
        // {
        //     var sessions = await http.GetFromJsonAsync<SessionDto[]>("/sessions", HpdosJsonOptions.Http);
        //     if (sessions != null && sessions.Length > 0)
        //         return sessions.OrderByDescending(s => s.LastActivity).First();
        //
        //     return await CreateSessionAsync(http);
        // }
        // catch (Exception ex)
        // {
        //     AnsiConsole.MarkupLine($"[red]Failed to load sessions:[/] {Markup.Escape(ex.Message)}");
        //     return null;
        // }
    }

    // When the GUI app (or a previous `hpdos chat`) is already running it writes
    // its Kestrel port to a file (~/.config/hpdos/port).  If that file exists and
    // the port responds to /api/status we skip starting a new Kestrel instance and
    // reuse the one that is already running.  The port file is deleted on clean
    // exit (Ctrl+C, SIGTERM) so a stale file just means we fall through and start
    // fresh.
    private static async Task<string?> TryAttachToRunningInstanceAsync()
    {
        var portFile = HpdosDataPaths.ActivePortFile;
        if (!File.Exists(portFile)) return null;

        string text;
        try { text = await File.ReadAllTextAsync(portFile); }
        catch { return null; }

        if (!int.TryParse(text.Trim(), out var port)) return null;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var resp = await http.GetAsync($"http://localhost:{port}/api/status");
            if (resp.IsSuccessStatusCode)
                return $"http://localhost:{port}";
        }
        catch { }

        // Stale port file — clean it up
        try { File.Delete(portFile); } catch { }
        return null;
    }

    private static async Task<SessionDto?> CreateSessionAsync(HttpClient http, IConsoleSession session)
    {
        try
        {
            var response = await http.PostAsJsonAsync("/sessions", new CreateSessionRequest(null, null), HpdosJsonOptions.Http);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<SessionDto>(HpdosJsonOptions.Http);
        }
        catch (Exception ex)
        {
            session.MarkupLine($"[red]Failed to create session:[/] {Markup.Escape(ex.Message)}");
            return null;
        }
    }
}
