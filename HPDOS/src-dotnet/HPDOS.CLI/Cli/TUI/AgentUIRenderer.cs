using HPD.Agent;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Planning;
using HPD.Events;
using HPDOS.Shell.Cli.TUI.Commands;
using HPDOS.Shell.Cli.TUI.Markdown;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Net.Http.Json;

namespace HPDOS.Shell.Cli.TUI;


/// <summary>
/// Component-based UI renderer for HPD Agent events.
/// Uses UIState for state management and components for rendering.
/// </summary>
public class AgentUIRenderer
{
    private readonly IConsoleSession _session;
    private readonly UIStateManager _stateManager;
    private readonly ToolRenderer _toolRenderer;
    private readonly object _lock = new();
    private bool _isFirstOutput = true;
    private bool _assistantThreadStarted;
    private string? _pendingAssistantHeader;
    private bool _showMarkerOnNextAssistantTextBlock;

    // Thinking spinner — runs between turn start and first output, and during tool execution
    private CancellationTokenSource? _spinnerCts;
    private Task? _spinnerTask;

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

        // Initialize tool renderer with callbacks
        _toolRenderer = new ToolRenderer(new ToolRenderContext(
            Session: session,
            WriteThread: WriteAssistantThreadRenderable,
            FlushText: FlushPendingText,
            StartSpinner: StartSpinner,
            StopSpinner: StopSpinner,
            SetShowMarkerOnNext: () => _showMarkerOnNextAssistantTextBlock = true
        ));
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
                    _toolRenderer.RenderToolStart(toolStart);
                    break;

                case ToolCallArgsEvent toolArgs:
                    _toolRenderer.RenderToolArgs(toolArgs);
                    break;

                case ToolCallResultEvent toolResult:
                    _toolRenderer.RenderToolResult(toolResult);
                    break;
                    
                case PermissionRequestEvent permissionRequest:
                    RenderPermissionRequest(permissionRequest).GetAwaiter().GetResult();
                    break;

                case ContinuationRequestEvent continuationRequest:
                    RenderContinuationRequest(continuationRequest).GetAwaiter().GetResult();
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
        _toolRenderer.Clear();
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

        _session.WriteLine();
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

    private async Task RenderContinuationRequest(ContinuationRequestEvent evt)
    {
        _session.WriteLine();
        var panel = new Panel(
            new Markup($"[yellow]Agent has reached the iteration limit:[/]\n\n" +
                      $"Current iteration: [bold]{evt.CurrentIteration}[/]\n" +
                      $"Max iterations: [bold]{evt.MaxIterations}[/]\n\n" +
                      $"[dim]The agent would like to continue exploring and executing more steps.[/]")
        )
        .Header("[yellow]🔄 Continuation Request[/]")
        .Border(BoxBorder.Double)
        .BorderColor(Color.Yellow).CapToTerminal();

        _session.Write(panel);

        // Prompt user for continuation decision
        var choice = _session.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Continue with more iterations?[/]")
                .AddChoices("Continue (+3 iterations)", "Continue (+5 iterations)", "Stop"));

        var (approved, extensionAmount) = choice switch
        {
            "Continue (+3 iterations)" => (true, 3),
            "Continue (+5 iterations)" => (true, 5),
            "Stop" => (false, 0),
            _ => (false, 0)
        };

        // Send response to the API endpoint to unblock the agent middleware
        if (_httpClient != null && _sessionId != null && _branchId != null)
        {
            var responseEvent = new ContinuationResponseEvent(
                evt.ContinuationId,
                "CLI",
                approved,
                extensionAmount);

            try
            {
                await _httpClient.PostAsJsonAsync(
                    $"/sessions/{_sessionId}/branches/{_branchId}/continuation/respond",
                    responseEvent,
                    HpdosJsonOptions.Http);
            }
            catch (Exception ex)
            {
                _session.MarkupLine($"[red dim]Continuation response failed: {Markup.Escape(ex.Message)}[/]");
            }
        }

        _session.MarkupLine(approved
            ? "[green]✓ Continuing with more iterations[/]"
            : "[red]✗ Stopping execution[/]");
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
                            var argsJson = call.Arguments != null
                                ? System.Text.Json.JsonSerializer.Serialize(call.Arguments)
                                : "{}";
                            _toolRenderer.RenderHistoryToolCall(call.Name, argsJson);
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
