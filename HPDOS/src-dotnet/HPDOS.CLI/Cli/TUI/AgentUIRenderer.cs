using HPD.Agent;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Planning;
using HPD.Events;
using HPDOS.Shell.Cli.TUI.Commands;
using HPDOS.Shell.Cli.TUI.Markdown;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace HPDOS.Shell.Cli.TUI;


/// <summary>
/// Component-based UI renderer for HPD Agent events.
/// Mirrors Gemini CLI's architecture with reusable components.
/// Uses UIState for state management and components for rendering.
/// </summary>
public class AgentUIRenderer
{
    private readonly IConsoleSession _session;
    private readonly UIStateManager _stateManager;
    private readonly ConcurrentDictionary<string, ToolMessage> _toolComponents = new();
    private readonly ConcurrentDictionary<string, string?> _callIdToToolkit = new();
    private readonly ConcurrentDictionary<string, string?> _callIdToRenderedLine = new();
    private readonly object _lock = new();
    private bool _isFirstOutput = true;
    private bool _assistantThreadStarted;
    private string? _pendingAssistantHeader;
    private bool _showMarkerOnNextAssistantTextBlock;

    // Thinking spinner — runs between turn start and first output, and during tool execution
    private CancellationTokenSource? _spinnerCts;
    private Task? _spinnerTask;

    // Known CodingToolkit tools (for fallback detection when ToolkitName is null)
    private static readonly HashSet<string> CodingToolkitTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "ReadFile", "read_file", "ReadManyFiles", "read_many_files",
        "EditFile", "edit_file", "WriteFile", "write_file",
        "ListDirectory", "list_directory", "GlobSearch", "glob_search",
        "Grep", "grep", "DiffFiles", "diff_files",
        "GetFileInfo", "get_file_info", "ExecuteCommand", "execute_command"
    };

    // Tools whose results should be hidden (rendered via dedicated events instead)
    private static readonly HashSet<string> HiddenResultTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "CreatePlanAsync", "create_plan_async", "CreatePlan", "create_plan",
        "UpdatePlanStepAsync", "update_plan_step_async", "UpdatePlanStep", "update_plan_step",
        "CodingToolkit", "MathToolkit"
    };

    // Streaming markdown support - Codex-style line accumulator
    private readonly StreamCollector<IRenderable> _lineCollector = new(new SpectreMarkdownRenderer());
    private bool _useStreamingMarkdown = true;

    // Command system - slash commands with autocomplete
    private readonly CommandRegistry _commandRegistry = new();

    // HttpClient + session/branch context for sending permission responses
    private HttpClient? _httpClient;
    private string? _sessionId;
    private string? _branchId;

    // Current model info for display in response headers
    private string? _currentProvider;
    private string? _currentModel;

    public UIStateManager StateManager => _stateManager;
    public CommandRegistry CommandRegistry => _commandRegistry;

    /// <summary>
    /// Enable or disable streaming markdown rendering.
    /// When disabled, text streams as plain characters (original behavior).
    /// </summary>
    public bool UseStreamingMarkdown
    {
        get => _useStreamingMarkdown;
        set => _useStreamingMarkdown = value;
    }

    public AgentUIRenderer(IConsoleSession session)
    {
        _session = session;
        _stateManager = new UIStateManager();
        BuiltInCommands.RegisterAll(_commandRegistry);
    }

    /// <summary>
    /// Sets the HttpClient and active session/branch context for sending permission responses.
    /// </summary>
    public void SetStreamContext(HttpClient httpClient, string sessionId, string branchId)
    {
        _httpClient = httpClient;
        _sessionId = sessionId;
        _branchId = branchId;
    }

    /// <summary>
    /// Updates the model info displayed in response headers.
    /// Used when switching models via AgentRunConfig (without rebuilding agent).
    /// </summary>
    public void SetModelInfo(string provider, string model)
    {
        _currentProvider = provider;
        _currentModel = model;
    }
    
    /// <summary>
    /// Display the app header on startup.
    /// </summary>
    public void ShowHeader(string version = "1.0.0", string? model = null)
    {
        var header = new AppHeader
        {
            Title = "HPD Agent",
            Version = version,
            Model = model
        };
        header.Display(_session);
    }
    
    /// <summary>
    /// Display help panel with registered commands.
    /// </summary>
    public void ShowHelp()
    {
        var helpPanel = new HelpPanel
        {
            Commands = _commandRegistry.GetVisibleCommands()
        };
        helpPanel.Display(_session);
    }
    
    /// <summary>
    /// Display session statistics.
    /// </summary>
    public void ShowStats()
    {
        var stats = new StatsDisplay
        {
            TotalTokens = _stateManager.State.Stats.TotalTokens,
            PromptTokens = _stateManager.State.Stats.PromptTokens,
            CompletionTokens = _stateManager.State.Stats.CompletionTokens,
            TotalTime = _stateManager.State.Stats.TotalTime,
            ToolCalls = _stateManager.State.Stats.ToolCalls
        };
        stats.Display(_session);
    }
    
    /// <summary>
    /// Record user input and display.
    /// </summary>
    public void ShowUserMessage(string content)
    {
        _stateManager.AddUserMessage(content);
        _session.WriteLine();
        new UserMessage { Content = content }.Display(_session);
    }
    
    /// <summary>
    /// Process and render any HPD event (AgentEvent, including workflow events).
    /// </summary>
    public void RenderEvent(Event evt)
    {
        // All events are now AgentEvent-derived (workflow events wrap graph events)
        if (evt is AgentEvent agentEvt)
        {
            RenderAgentEvent(agentEvt);
        }
    }

    /// <summary>
    /// Process and render an agent event using components.
    /// </summary>
    public void RenderAgentEvent(AgentEvent evt)
    {
        lock (_lock)
        {
            // Update state
            _stateManager.ProcessEvent(evt);

            // Render based on event type
            switch (evt)
            {
                case MessageTurnStartedEvent turnStart:
                    RenderTurnStart(turnStart);
                    break;
                    
                case MessageTurnFinishedEvent turnEnd:
                    RenderTurnFinished(turnEnd);
                    break;
                    
                case MessageTurnErrorEvent error:
                    RenderError(error);
                    break;
                    
                case TextDeltaEvent textDelta:
                    RenderTextDelta(textDelta);
                    break;
                    
                case ToolCallStartEvent toolStart:
                    RenderToolStart(toolStart);
                    break;
                    
                case ToolCallArgsEvent toolArgs:
                    RenderToolArgs(toolArgs);
                    break;
                    
                case ToolCallResultEvent toolResult:
                    RenderToolResult(toolResult);
                    break;
                    
                case PermissionRequestEvent permissionRequest:
                    RenderPermissionRequest(permissionRequest).GetAwaiter().GetResult();
                    break;

                case ReasoningMessageStartEvent:
                    StartSpinner("Reasoning...");
                    break;

                case ReasoningDeltaEvent delta:
                    // Reasoning text hidden - users find it too verbose
                    // AnsiConsole.Markup($"[dim]{Markup.Escape(delta.Text)}[/]");
                    break;

                case ReasoningMessageEndEvent:
                    _session.WriteLine();
                    break;

                // Plan Mode events
                case PlanUpdatedEvent planUpdate:
                    RenderPlanUpdate(planUpdate);
                    break;

                // History reduction events
                case HistoryReductionEvent historyReduction:
                    RenderHistoryReduction(historyReduction);
                    break;
            }
        }
    }
    
    private void StartSpinner(string message)
    {
        StopSpinner();
        _spinnerCts = new CancellationTokenSource();
        var ct = _spinnerCts.Token;
        _spinnerTask = Task.Run(async () =>
        {
            var frames = new[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
            var i = 0;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // Route through AnsiConsole so output serialises with other Spectre writes.
                    // ControlCode carries raw ANSI positioning; Markup carries the styled text.
                    _session.Write(new ControlCode("\r\x1b[2K"));
                    _session.Markup($"[dim]{frames[i % frames.Length]} {Markup.Escape(message)}[/]");
                    i++;
                    await Task.Delay(80, ct);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                // Erase the spinner line via AnsiConsole to stay in the same output pipeline.
                _session.Write(new ControlCode("\r\x1b[2K"));
            }
        }, ct);
    }

    private void StopSpinner()
    {
        if (_spinnerCts == null) return;
        _spinnerCts.Cancel();
        try { _spinnerTask?.GetAwaiter().GetResult(); } catch { }
        _spinnerCts.Dispose();
        _spinnerCts = null;
        _spinnerTask = null;
    }

    private void RenderTurnStart(MessageTurnStartedEvent evt)
    {
        _isFirstOutput = true;
        _assistantThreadStarted = false;
        _showMarkerOnNextAssistantTextBlock = false;
        _toolComponents.Clear();
        _lineCollector.Clear();

        _session.WriteLine();

        // Build header: "AgentName - provider:model" or just "AgentName" if no model info
        var headerText = evt.AgentName;
        if (!string.IsNullOrEmpty(_currentProvider) && !string.IsNullOrEmpty(_currentModel))
        {
            headerText = $"{evt.AgentName} [dim]-[/] [cyan]{_currentProvider}[/]:[white]{_currentModel}[/]";
        }

        _pendingAssistantHeader = headerText;

        StartSpinner("Thinking...");
    }

    private void RenderTurnFinished(MessageTurnFinishedEvent evt)
    {
        StopSpinner();

        // Finalize any remaining content (incomplete line without trailing newline)
        if (_useStreamingMarkdown)
        {
            var remaining = _lineCollector.Finalize();
            foreach (var line in remaining)
                WriteAssistantTextRenderable(line);
        }
    }

    private void RenderError(MessageTurnErrorEvent evt)
    {
        StopSpinner();
        _session.WriteLine();

        // Show model-specific error with helpful suggestion
        if (evt.IsModelNotFound)
        {
            _session.MarkupLine("[red bold]Model not found[/]");
            _session.MarkupLine($"[dim]{Markup.Escape(evt.Message)}[/]");
            _session.MarkupLine("[yellow]Tip:[/] Use [cyan]/models[/] to see available models, or check your model ID.");
        }
        else if (evt.Category != null)
        {
            // Show category-specific error
            _session.MarkupLine($"[red bold][{evt.Category}][/] ");
            new ErrorMessage { Message = evt.Message }.Display(_session);

            if (evt.IsRetryable)
            {
                _session.MarkupLine("[dim]This error may be temporary. Try again.[/]");
            }
        }
        else
        {
            new ErrorMessage { Message = evt.Message }.Display(_session);
        }
    }
    
    private void RenderTextDelta(TextDeltaEvent evt)
    {
        string text = evt.Text;
        if (_isFirstOutput)
        {
            StopSpinner();
            _isFirstOutput = false;
            // Trim leading newlines from the first delta to avoid redundant blank lines after the rule.
            // The Rule component already handles its own line ending.
            text = text.TrimStart('\n', '\r');
            if (string.IsNullOrEmpty(text)) return;
        }

        if (_useStreamingMarkdown)
        {
            _lineCollector.Push(text);

            if (_lineCollector.HasCompleteLines)
            {
                _lineCollector.CommitCompleteLines();
                foreach (var line in _lineCollector.GetQueuedLines())
                    WriteAssistantTextRenderable(line);
            }
        }
        else
        {
            WriteAssistantTextRenderable(new Markup(Markup.Escape(text)));
        }
    }
    
    private void RenderToolStart(ToolCallStartEvent evt)
    {
        StopSpinner();

        // Flush any pending streamed text BEFORE showing tool call
        // This ensures text appears in correct order relative to tool outputs
        FlushPendingText();

        var toolMessage = new ToolMessage
        {
            Name = evt.Name,
            Status = ToolCallStatus.Executing
        };
        _toolComponents[evt.CallId] = toolMessage;
        _callIdToToolkit[evt.CallId] = evt.ToolkitName;

        // Hide toolkit containers and tools with dedicated event rendering
        if (evt.Name.EndsWith("Toolkit") || HiddenResultTools.Contains(evt.Name))
        {
            StartSpinner($"{evt.Name}...");
            return;
        }

        // CodingToolkit tools: don't show anything on start - we'll show inline with result
        if (IsCodingToolkitTool(evt.Name, evt.ToolkitName))
        {
            StartSpinner($"{evt.Name}...");
            return;
        }

        // Default: show full tool call info for non-CodingToolkit tools
        _session.WriteLine();
        WriteAssistantThreadRenderable(new Markup($"[yellow]⚙ Calling:[/] [bold]{Markup.Escape(evt.Name)}[/]"));
        _showMarkerOnNextAssistantTextBlock = true;
        StartSpinner("Waiting for result...");
    }

    /// <summary>
    /// Detects if a tool belongs to CodingToolkit (by name or explicit ToolkitName)
    /// </summary>
    private static bool IsCodingToolkitTool(string toolName, string? toolkitName)
    {
        if (toolkitName == "CodingToolkit") return true;
        if (CodingToolkitTools.Contains(toolName)) return true;
        return false;
    }

    /// <summary>
    /// Flushes any pending streamed text from the line collector.
    /// Call this before rendering tool calls/results to maintain proper ordering.
    /// </summary>
    private void FlushPendingText()
    {
        if (!_useStreamingMarkdown) return;

        foreach (var line in _lineCollector.Finalize())
            WriteAssistantTextRenderable(line);

        _lineCollector.Clear();
    }
    
    private void RenderToolArgs(ToolCallArgsEvent evt)
    {
        if (!_toolComponents.TryGetValue(evt.CallId, out var tool))
            return;

        tool.Args = evt.ArgsJson;
        _callIdToToolkit.TryGetValue(evt.CallId, out var toolkit);

        // For CodingToolkit tools: buffer the display line (will be shown with result)
        if (IsCodingToolkitTool(tool.Name, toolkit))
        {
            var displayLine = BuildCodingToolkitDisplayLine(tool.Name, evt.ArgsJson);
            _callIdToRenderedLine[evt.CallId] = displayLine;
        }
    }

    /// <summary>
    /// Builds the display line for a CodingToolkit tool call (returned as markup string)
    /// </summary>
    private static string BuildCodingToolkitDisplayLine(string toolName, string argsJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(argsJson);
            var root = doc.RootElement;

            switch (toolName)
            {
                case "ReadFile" or "read_file":
                    var path = root.TryGetProperty("path", out var p) ? p.GetString() :
                               root.TryGetProperty("filePath", out var fp) ? fp.GetString() : null;
                    var startLine = root.TryGetProperty("startLine", out var sl) ? sl.GetInt32() : (int?)null;
                    var endLine = root.TryGetProperty("endLine", out var el) ? el.GetInt32() : (int?)null;
                    if (path != null)
                    {
                        var lineInfo = (startLine.HasValue && endLine.HasValue)
                            ? $" [dim](lines {startLine}-{endLine})[/]"
                            : startLine.HasValue ? $" [dim](from line {startLine})[/]" : "";
                        return $"[dim]⚙ ReadFile:[/] [blue]{Markup.Escape(path)}[/]{lineInfo}";
                    }
                    break;

                case "ReadManyFiles" or "read_many_files":
                    if (root.TryGetProperty("paths", out var paths) && paths.ValueKind == System.Text.Json.JsonValueKind.Array)
                        return $"[dim]⚙ ReadManyFiles:[/] [blue]{paths.GetArrayLength()} files[/]";
                    break;

                case "EditFile" or "edit_file":
                case "WriteFile" or "write_file":
                    var editPath = root.TryGetProperty("path", out var ep) ? ep.GetString() :
                                   root.TryGetProperty("filePath", out var efp) ? efp.GetString() : null;
                    if (editPath != null)
                        return $"[dim]⚙ {Markup.Escape(toolName)}:[/] [blue]{Markup.Escape(editPath)}[/]";
                    break;

                case "ListDirectory" or "list_directory":
                    var dirPath = root.TryGetProperty("directoryPath", out var dp) ? dp.GetString() :
                                  root.TryGetProperty("path", out var dp2) ? dp2.GetString() : null;
                    var displayDir = string.IsNullOrWhiteSpace(dirPath) ? "." : dirPath;
                    return $"[dim]⚙ ListDirectory:[/] [blue]{Markup.Escape(displayDir)}[/]";

                case "GlobSearch" or "glob_search":
                    var pattern = root.TryGetProperty("pattern", out var pat) ? pat.GetString() : null;
                    if (pattern != null)
                        return $"[dim]⚙ GlobSearch:[/] [blue]{Markup.Escape(pattern)}[/]";
                    break;

                case "Grep" or "grep":
                    var query = root.TryGetProperty("pattern", out var q) ? q.GetString() :
                                root.TryGetProperty("query", out var qry) ? qry.GetString() : null;
                    if (query != null)
                        return $"[dim]⚙ Grep:[/] [blue]{Markup.Escape(query)}[/]";
                    break;

                case "ExecuteCommand" or "execute_command":
                    var cmd = root.TryGetProperty("command", out var c) ? c.GetString() : null;
                    if (cmd != null)
                    {
                        var displayCmd = cmd.Length > 60 ? cmd[..60] + "..." : cmd;
                        return $"[dim]⚙ ExecuteCommand:[/] [yellow]{Markup.Escape(displayCmd)}[/]";
                    }
                    break;
            }
        }
        catch { /* JSON parse failed */ }

        return $"[dim]⚙ {Markup.Escape(toolName)}[/]";
    }
    
    private void RenderToolResult(ToolCallResultEvent evt)
    {
        StopSpinner();

        // Flush any pending streamed text before showing tool result
        FlushPendingText();

        if (!_toolComponents.TryGetValue(evt.CallId, out var tool))
            return;

        tool.Result = evt.Result;

        var isError = ResultDetector.IsError(evt.Result);
        tool.Status = isError ? ToolCallStatus.Error : ToolCallStatus.Completed;

        // Get toolkit name (from event or cached from start event)
        var toolkitName = evt.ToolkitName ?? (_callIdToToolkit.TryGetValue(evt.CallId, out var cached) ? cached : null);

        // Hide results for tools with dedicated event rendering
        if (HiddenResultTools.Contains(tool.Name) || tool.Name.EndsWith("Toolkit"))
        {
            _toolComponents.TryRemove(evt.CallId, out _);
            _callIdToToolkit.TryRemove(evt.CallId, out _);
            _callIdToRenderedLine.TryRemove(evt.CallId, out _);
            return;
        }

        // CodingToolkit tools: show inline with colored gear
        if (IsCodingToolkitTool(tool.Name, toolkitName))
        {
            RenderCodingToolkitResult(tool, evt.Result, isError, evt.CallId);
            _showMarkerOnNextAssistantTextBlock = true;
            _toolComponents.TryRemove(evt.CallId, out _);
            _callIdToToolkit.TryRemove(evt.CallId, out _);
            _callIdToRenderedLine.TryRemove(evt.CallId, out _);
            return;
        }

        // Default: show full result for non-CodingToolkit tools
        WriteAssistantThreadRenderable(tool.Render());
        if (!isError)
        {
            RenderResultByType(evt.Result);
        }

        _showMarkerOnNextAssistantTextBlock = true;

        _toolComponents.TryRemove(evt.CallId, out _);
        _callIdToToolkit.TryRemove(evt.CallId, out _);
    }

    private void RenderCodingToolkitResult(ToolMessage tool, string result, bool isError, string callId)
    {
        // Get buffered display line and colorize gear based on result
        _callIdToRenderedLine.TryRemove(callId, out var displayLine);
        displayLine ??= $"⚙ {Markup.Escape(tool.Name)}";

        // Replace dim gear with colored gear based on success/failure
        var coloredLine = isError
            ? displayLine.Replace("[dim]⚙", "[red]⚙")
            : displayLine.Replace("[dim]⚙", "[green]⚙");

        _session.WriteLine();
        WriteAssistantThreadRenderable(new Markup(coloredLine));

        if (isError)
        {
            WriteAssistantThreadRenderable(new Markup($"[red dim]  {Markup.Escape(TruncateResult(result, 100))}[/]"));
            return;
        }

        // Show diff for write operations
        var isWriteOp = tool.Name is "EditFile" or "WriteFile" or "edit_file" or "write_file";

        if (isWriteOp && ResultDetector.Detect(result) == ResultType.Diff)
        {
            DisplayToolDiff(result);
        }
    }

    private static string TruncateResult(string result, int maxLength)
    {
        if (result.Length <= maxLength) return result;
        return result[..maxLength] + "...";
    }
    
    /// <summary>
    /// Smart content-based rendering. Detects result type and renders accordingly.
    /// This is Toolkit-agnostic: any tool outputting a diff gets diff rendering, etc.
    /// </summary>
    private void RenderResultByType(string result)
    {
        var resultType = ResultDetector.Detect(result);

        switch (resultType)
        {
            case ResultType.Diff:
                DisplayToolDiff(result);
                break;
            case ResultType.Json:
                // Future: DisplayJson(result);
                break;
            case ResultType.Table:
                // Future: DisplayTable(result);
                break;
            // Plain text already shown in tool.Render()
        }
    }

    private void DisplayToolDiff(string result)
    {
        try
        {
            // The result might contain diff information
            // Look for diff markers like +++ and --- (unified diff format)
            if (result.Contains("+++") && result.Contains("---"))
            {
                var lines = result.Split('\n');
                var diffContent = new StringBuilder();
                bool inDiff = false;
                string? fileName = null;
                
                foreach (var line in lines)
                {
                    // Extract filename from --- line
                    if (line.StartsWith("---") && fileName == null)
                    {
                        var parts = line.Split('\t');
                        if (parts.Length > 0)
                        {
                            fileName = parts[0].Substring(4).Trim(); // Remove "--- "
                        }
                        inDiff = true;
                    }
                    
                    if (inDiff)
                        diffContent.AppendLine(line);
                }
                
                if (diffContent.Length > 0)
                {
                    // Use DiffRenderer component for rich diff display
                    var diffRenderer = new DiffRenderer
                    {
                        DiffContent = diffContent.ToString(),
                        Filename = fileName,
                        MaxLines = 50
                    };
                    
                    WriteAssistantThreadRenderable(diffRenderer.Render());
                    _session.WriteLine();
                }
            }
        }
        catch (Exception ex)
        {
            WriteAssistantThreadRenderable(new Markup($"[dim]Note: Could not parse diff: {Markup.Escape(ex.Message)}[/]"));
        }
    }
    
    private async Task RenderPermissionRequest(PermissionRequestEvent evt)
    {
        _session.WriteLine();
        var panel = new Panel(
            new Markup($"[yellow]Permission requested:[/]\n\n" +
                      $"[bold]{Markup.Escape(evt.FunctionName)}[/]\n" +
                      $"{Markup.Escape(evt.Description ?? "No description")}")
        )
        .Header("[yellow]🔒 Permission Required[/]")
        .Border(BoxBorder.Double)
        .BorderColor(Color.Yellow).CapToTerminal();

        _session.Write(panel);

        // Prompt user for permission decision
        var choice = _session.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Grant permission?[/]")
                .AddChoices("Allow once", "Allow always", "Deny once", "Deny always"));

        var (approved, choiceStr) = choice switch
        {
            "Allow once"   => (true,  "ask"),
            "Allow always" => (true,  "always_allow"),
            "Deny once"    => (false, "ask"),
            "Deny always"  => (false, "always_deny"),
            _              => (false, "ask")
        };

        // Send response to the API endpoint to unblock the agent middleware
        if (_httpClient != null && _sessionId != null && _branchId != null)
        {
            var permissionChoice = choiceStr?.ToLower() switch
            {
                "allow_always" => PermissionChoice.AlwaysAllow,
                "deny_always" => PermissionChoice.AlwaysDeny,
                _ => PermissionChoice.Ask
            };

            var responseEvent = new PermissionResponseEvent(
                evt.PermissionId,
                "CLI",
                approved,
                approved ? null : "User denied permission",
                permissionChoice);

            try
            {
                await _httpClient.PostAsJsonAsync(
                    $"/sessions/{_sessionId}/branches/{_branchId}/permissions/respond",
                    responseEvent,
                    HpdosJsonOptions.Http);
            }
            catch (Exception ex)
            {
                _session.MarkupLine($"[red dim]Permission response failed: {Markup.Escape(ex.Message)}[/]");
            }
        }

        _session.MarkupLine(approved
            ? "[green]✓ Permission granted[/]"
            : "[red]✗ Permission denied[/]");
    }

    /// <summary>
    /// Renders a tail of stored messages from the branch history.
    /// Uses the same component pipeline as live streaming so the output is visually identical.
    /// Called by SessionBrowserCommand after switching sessions.
    /// </summary>
    public void RenderHistoryTail(IEnumerable<MessageDto> messages)
    {
        lock (_lock)
        {
            foreach (var msg in messages)
            {
                switch (msg.Role)
                {
                    case "user":
                        var userText = ExtractText(msg);
                        if (!string.IsNullOrWhiteSpace(userText))
                        {
                            _session.WriteLine();
                            new UserMessage { Content = userText }.Display(_session);
                        }
                        break;

                    case "assistant":
                        _session.WriteLine();
                        _assistantThreadStarted = false;
                        _pendingAssistantHeader = "Agent";

                        _lineCollector.Clear();
                        var text = msg.GetText();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            _lineCollector.Push(text);
                            var flushed = _lineCollector.Finalize();
                            foreach (var line in flushed)
                                WriteAssistantThreadRenderable(line);
                            _lineCollector.Clear();
                            _session.WriteLine();
                        }
                        foreach (var call in msg.GetToolCalls())
                        {
                            var callLine = BuildCodingToolkitDisplayLine(
                                call.Name,
                                call.Arguments != null
                                    ? JsonSerializer.Serialize(call.Arguments)
                                    : "{}");
                            // All history is completed — colour gear green
                            var doneLine = callLine.Replace("[dim]⚙", "[green]⚙");
                            _session.WriteLine();
                            WriteAssistantThreadRenderable(new Markup(doneLine));
                        }
                        break;
                }
            }
        }
    }

    private static string ExtractText(MessageDto msg) => msg.GetText();

    private void RenderHistoryReduction(HistoryReductionEvent evt)
    {
        // Only show if reduction actually happened (not skipped)
        if (evt.Status == HistoryReductionStatus.Skipped)
        {
            // Optionally show skipped events in debug mode
            // AnsiConsole.MarkupLine($"[dim]⊘ History reduction skipped: {evt.Reason}[/]");
            return;
        }

        var icon = evt.Status switch
        {
            HistoryReductionStatus.CacheHit => "◇",
            HistoryReductionStatus.Performed => "≡",
            _ => "◈"
        };

        var color = evt.Status switch
        {
            HistoryReductionStatus.CacheHit => "cyan",
            HistoryReductionStatus.Performed => "yellow",
            _ => "dim"
        };

        // Show reduction summary
        _session.MarkupLine($"[{color}]{icon} History Reduction ({evt.Status}):[/]");

        if (evt.OriginalMessageCount.HasValue && evt.ReducedMessageCount.HasValue)
        {
            _session.MarkupLine($"[dim]  {evt.OriginalMessageCount} → {evt.ReducedMessageCount} messages[/]");
        }

        if (evt.MessagesRemoved.HasValue)
        {
            _session.MarkupLine($"[dim]  Removed: {evt.MessagesRemoved} messages[/]");
        }

        if (evt.CacheAge.HasValue)
        {
            _session.MarkupLine($"[dim]  Cache age: {evt.CacheAge.Value.TotalMinutes:F1}m[/]");
        }

        // Show summary content if available (for Summarizing strategy)
        if (evt.Strategy == HistoryReductionStrategy.Summarizing &&
            !string.IsNullOrEmpty(evt.SummaryContent))
        {
            var summaryPreview = evt.SummaryContent.Length > 200
                ? evt.SummaryContent[..200] + "..."
                : evt.SummaryContent;

            var panel = new Panel(Markup.Escape(summaryPreview))
            {
                Header = new PanelHeader($"Summary ({evt.SummaryLength} chars)", Justify.Left),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(foreground: Color.Grey),
                Width = Console.WindowWidth
            };

            _session.Write(panel);
        }

        _session.MarkupLine($"[dim]  Duration: {evt.Duration.TotalMilliseconds:F0}ms[/]");
        _session.WriteLine();
    }

    private void RenderPlanUpdate(PlanUpdatedEvent evt)
    {
        // Cast Plan to AgentPlanData
        if (evt.Plan is not HPD.Agent.Planning.AgentPlanData plan)
        {
            _session.MarkupLine("[red]⚠ Invalid plan data in PlanUpdatedEvent[/]");
            return;
        }

        var icon = evt.UpdateType switch
        {
            PlanUpdateType.Created => "≡",
            PlanUpdateType.StepUpdated => "◐",
            PlanUpdateType.StepAdded => "+",
            PlanUpdateType.NoteAdded => "»",
            PlanUpdateType.Completed => "●",
            _ => "•"
        };

        var color = evt.UpdateType switch
        {
            PlanUpdateType.Created => "cyan",
            PlanUpdateType.StepUpdated => "yellow",
            PlanUpdateType.StepAdded => "green",
            PlanUpdateType.NoteAdded => "blue",
            PlanUpdateType.Completed => "green bold",
            _ => "white"
        };

        // Display plan update header
        _session.WriteLine();
        _session.MarkupLine($"[{color}]{icon} Plan {evt.UpdateType}:[/] [dim]{Markup.Escape(evt.Explanation ?? "")}[/]");

        // Display plan details in a panel
        var panel = new Panel(BuildPlanDisplay(plan, evt.UpdateType))
        {
            Header = new PanelHeader($"Plan: {Markup.Escape(plan.Goal)}", Justify.Left),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(foreground: Color.Grey),
            Width = Console.WindowWidth
        };

        _session.Write(panel);
        _session.WriteLine();
    }

    private IRenderable BuildPlanDisplay(HPD.Agent.Planning.AgentPlanData plan, PlanUpdateType updateType)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("").Width(3))
            .AddColumn(new TableColumn("").Width(12))
            .AddColumn(new TableColumn(""));

        // Show steps
        foreach (var step in plan.Steps)
        {
            var statusIcon = step.Status switch
            {
                HPD.Agent.Planning.PlanStepStatus.Pending => "○",
                HPD.Agent.Planning.PlanStepStatus.InProgress => "◐",
                HPD.Agent.Planning.PlanStepStatus.Completed => "●",
                HPD.Agent.Planning.PlanStepStatus.Blocked => "⊘",
                _ => "•"
            };

            var statusColor = step.Status switch
            {
                HPD.Agent.Planning.PlanStepStatus.Pending => "dim",
                HPD.Agent.Planning.PlanStepStatus.InProgress => "yellow",
                HPD.Agent.Planning.PlanStepStatus.Completed => "green",
                HPD.Agent.Planning.PlanStepStatus.Blocked => "red",
                _ => "white"
            };

            var statusText = $"[{statusColor}]{step.Status}[/]";
            var description = Markup.Escape(step.Description);

            // Highlight the step if it was just updated
            if (updateType == PlanUpdateType.StepUpdated || updateType == PlanUpdateType.StepAdded)
            {
                description = $"[bold]{description}[/]";
            }

            table.AddRow(
                $"[{statusColor}]{statusIcon}[/]",
                statusText,
                description
            );

            // Show notes if available
            if (!string.IsNullOrEmpty(step.Notes))
            {
                table.AddRow("", "", $"[dim italic]→ {Markup.Escape(step.Notes)}[/]");
            }
        }

        // Show context notes if any
        if (plan.ContextNotes.Count > 0)
        {
            table.AddEmptyRow();
            table.AddRow("[blue]»[/]", "[blue]Notes:[/]", "");
            foreach (var note in plan.ContextNotes)
            {
                table.AddRow("", "", $"[dim]• {Markup.Escape(note)}[/]");
            }
        }

        // Show completion status
        if (plan.IsComplete)
        {
            table.AddEmptyRow();
            table.AddRow("[green]●[/]", "[green bold]Complete[/]", $"[dim]{plan.CompletedAt:g}[/]");
        }

        return table;
    }

    private void EnsureAssistantThreadStarted()
    {
        if (_assistantThreadStarted)
            return;

        if (!string.IsNullOrWhiteSpace(_pendingAssistantHeader))
            WriteAssistantThreadRenderableCore(new Markup($"[bold green]{_pendingAssistantHeader}[/]"), showMarker: true);

        _pendingAssistantHeader = null;
        _assistantThreadStarted = true;
    }

    private void WriteAssistantThreadRenderable(IRenderable renderable)
    {
        EnsureAssistantThreadStarted();

        WriteAssistantThreadRenderableCore(renderable, showMarker: false);
    }

    private void WriteAssistantTextRenderable(IRenderable renderable)
    {
        EnsureAssistantThreadStarted();

        var showMarker = _showMarkerOnNextAssistantTextBlock;
        _showMarkerOnNextAssistantTextBlock = false;

        WriteAssistantThreadRenderableCore(renderable, showMarker);
    }

    private void WriteAssistantThreadRenderableCore(IRenderable renderable, bool showMarker)
    {
        var marker = showMarker ? "[green]⏺[/]" : " ";

        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().Width(2));
        grid.AddColumn();
        grid.AddRow(new Markup(marker), renderable);

        _session.Write(grid);
    }

}
