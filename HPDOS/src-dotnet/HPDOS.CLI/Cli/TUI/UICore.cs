using HPD.Agent;
using HPD.Agent.Hosting.Serialization;
using HPD.Agent.Serialization;
using HPDOS.Core.Auth;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace HPDOS.Shell.Cli.TUI;

// ============================================================================
// BASE CLASSES
// ============================================================================

/// <summary>
/// Base class for all UI components.
/// Components are composable, renderable units with consistent styling.
/// </summary>
public abstract class UIComponent
{
    public abstract IRenderable Render();

    public void Display() => SpectreConsoleSession.CreateDefault().Write(Render());

    public void Display(IConsoleSession session) => session.Write(Render());
}

// ============================================================================
// THEME
// ============================================================================

/// <summary>
/// Theme colors for consistent styling across components.
/// </summary>
public static class Theme
{
    public static class Text
    {
        public static Color Primary => Color.White;
        public static Color Secondary => Color.Grey;
        public static Color Accent => Color.Cyan1;
        public static Color Muted => Color.Grey50;
    }
    
    public static class Status
    {
        public static Color Success => Color.Green;
        public static Color Error => Color.Red;
        public static Color Warning => Color.Yellow;
        public static Color Info => Color.Blue;
        public static Color Pending => Color.Yellow;
        public static Color Executing => Color.Cyan1;
    }
    
    public static class Tool
    {
        public static Color Border => Color.Grey;
        public static Color Header => Color.Yellow;
        public static Color Result => Color.Green;
        public static Color Args => Color.Grey;
    }
    
    public static class Diff
    {
        public static Color Added => Color.Green;
        public static Color Removed => Color.Red;
        public static Color Context => Color.Grey;
        public static Color Hunk => Color.Cyan1;

        // Background colors for inline word highlighting (GitHub-style)
        public static Color AddedBackground => Color.Green3;
        public static Color RemovedBackground => Color.Red3_1;
        public static Color ModifiedBackground => Color.Yellow3;
    }

    /// <summary>
    /// Converts a Spectre Color to its rgb() markup string, safe for use inside markup tags.
    /// </summary>
    public static string Markup(Color color) => $"rgb({color.R},{color.G},{color.B})";
}

// ============================================================================
// UTILITY FUNCTIONS
// ============================================================================

/// <summary>
/// Shared JSON serializer options for all HTTP calls against the Kestrel agent API.
/// Combines the HPD Agent API context with the HPD domain context.
/// </summary>
public static class HpdosJsonOptions
{
    public static readonly JsonSerializerOptions Http = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = JsonTypeInfoResolver.Combine(
            HPDAgentApiJsonSerializerContext.Default,
            HpdosCliJsonContext.Default,
            HPDJsonContext.Default)
    };
}

/// <summary>
/// Types used by the CLI HTTP client that aren't covered by the agent framework contexts.
/// </summary>
[JsonSerializable(typeof(List<AuthSummary>))]
[JsonSerializable(typeof(List<ModelInfo>))]
[JsonSerializable(typeof(List<AuthMethodInfoDto>))]
[JsonSerializable(typeof(DefaultsResponseDto))]
[JsonSerializable(typeof(SessionMetadataPatch))]
[JsonSerializable(typeof(DefaultsPatchDto))]
[JsonSerializable(typeof(ModelSessionPatch))]
[JsonSerializable(typeof(LoginCompleteDto))]
[JsonSerializable(typeof(SetActiveRequestDto))]
[JsonSerializable(typeof(StoredEntryInfo))]
[JsonSerializable(typeof(List<StoredEntryInfo>))]
// Bidirectional event responses
[JsonSerializable(typeof(PermissionResponseEvent))]
[JsonSerializable(typeof(ContinuationResponseEvent))]
[JsonSerializable(typeof(PermissionChoice))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class HpdosCliJsonContext : JsonSerializerContext { }

/// <summary>Deserialized shape of GET /api/providers/{id}/methods.</summary>
public record AuthMethodInfoDto(int Index, string Label, string? Description, bool IsRecommended);

/// <summary>Deserialized shape of GET /api/defaults.</summary>
public record DefaultsResponseDto(string? ProviderKey, string? ModelId);

/// <summary>Body for PATCH /sessions/{id} to seed provider/model into metadata.</summary>
public record SessionMetadataPatch(SessionMetadataDto Metadata);
public record SessionMetadataDto(string? ProviderKey, string? ModelId);

/// <summary>Body for PATCH /api/defaults.</summary>
public record DefaultsPatchDto(string? ProviderKey, string? ModelId);

/// <summary>Body for PATCH /sessions/{id} from /model command.</summary>
public record ModelSessionPatch(ModelSessionMetadata Metadata);
public record ModelSessionMetadata(string ProviderKey, string ModelId);

/// <summary>Body for POST /api/providers/{id}/login/complete.</summary>
public record LoginCompleteDto(string Input);

/// <summary>Body for PUT /api/providers/{id}/active.</summary>
public record SetActiveRequestDto(string EntryId);

/// <summary>
/// Extension methods for Spectre widgets.
/// </summary>
public static class WidgetExtensions
{
    /// <summary>
    /// Caps the panel render width to the current terminal width.
    /// Prevents visual corruption when the terminal is later resized narrower.
    /// </summary>
    public static Panel CapToTerminal(this Panel panel)
    {
        panel.Width = Console.WindowWidth;
        return panel;
    }
}

/// <summary>
/// Shared utility functions for UI components.
/// </summary>
public static class UIHelpers
{
    /// <summary>
    /// Format JSON with indentation. Used by multiple components.
    /// </summary>
    public static string FormatJson(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch
        {
            return json;
        }
    }

    /// <summary>
    /// Returns a human-readable relative time string ("just now", "5m ago", "2h ago", etc.).
    /// </summary>
    public static string RelativeTime(DateTime dt)
    {
        var diff = DateTime.Now - dt;
        if (diff.TotalSeconds < 60)  return "just now";
        if (diff.TotalMinutes < 60)  return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24)    return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7)      return $"{(int)diff.TotalDays}d ago";
        return dt.ToString("MMM d");
    }

    public static bool IsToday(DateTime dt)    => dt.Date == DateTime.Today;
    public static bool IsThisWeek(DateTime dt) => (DateTime.Now - dt).TotalDays <= 7;
}

// ============================================================================
// STATE MANAGEMENT
// ============================================================================

/// <summary>
/// UI state management for the agent.
/// Mirrors Gemini's UIStateContext.tsx
/// Tracks conversation history, streaming state, and active tool calls.
/// </summary>
public class UIState
{
    /// <summary>Current streaming state</summary>
    public StreamingState State { get; set; } = StreamingState.Idle;
    
    /// <summary>Active tool calls being executed</summary>
    public Dictionary<string, ToolCallState> ActiveToolCalls { get; } = new();
    
    /// <summary>Conversation history items</summary>
    public List<HistoryItem> History { get; } = new();
    
    /// <summary>Current message being streamed</summary>
    public string CurrentStreamingText { get; set; } = "";
    
    /// <summary>Terminal width for layout</summary>
    public int TerminalWidth { get; set; } = Console.WindowWidth;
    
    /// <summary>Session statistics</summary>
    public SessionStats Stats { get; } = new();
}

public enum StreamingState
{
    Idle,
    WaitingForConfirmation,
    Thinking,
    ExecutingTools
}

public enum ToolCallStatus
{
    Pending,
    Executing,
    Completed,
    Error
}

public class ToolCallState
{
    public string CallId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Args { get; set; }
    public string? Result { get; set; }
    public DateTime StartTime { get; set; } = DateTime.Now;
    public DateTime? EndTime { get; set; }
    public bool IsComplete { get; set; }
    public bool IsError { get; set; }
}

public class HistoryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public HistoryItemType Type { get; set; }
    public string Content { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public TimeSpan? Duration { get; set; }
    public List<ToolCallState>? ToolCalls { get; set; }
}

public enum HistoryItemType
{
    UserMessage,
    AssistantMessage,
    ToolKit,
    Error,
    Info
}

public class SessionStats
{
    public int TotalTokens { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int ToolCalls { get; set; }
    public TimeSpan TotalTime { get; set; }
    public int MessageCount { get; set; }
}

/// <summary>
/// Processes agent events and updates UI state.
/// Centralizes state management separate from rendering.
/// </summary>
public class UIStateManager
{
    private readonly UIState _state;
    private string? _currentMessageId;
    
    public UIState State => _state;
    
    public event Action? OnStateChanged;
    
    public UIStateManager()
    {
        _state = new UIState();
    }
    
    public void ProcessEvent(AgentEvent evt)
    {
        switch (evt)
        {
            case MessageTurnFinishedEvent turnEnd:
                _state.State = StreamingState.Idle;
                if (!string.IsNullOrEmpty(_state.CurrentStreamingText))
                {
                    _state.History.Add(new HistoryItem
                    {
                        Type = HistoryItemType.AssistantMessage,
                        Content = _state.CurrentStreamingText,
                        Duration = turnEnd.Duration
                    });
                }
                _state.CurrentStreamingText = "";
                _state.Stats.MessageCount++;
                break;
                
            case MessageTurnErrorEvent error:
                _state.State = StreamingState.Idle;
                _state.History.Add(new HistoryItem
                {
                    Type = HistoryItemType.Error,
                    Content = error.Message
                });
                break;
                
            case TextMessageStartEvent textStart:
                _currentMessageId = textStart.MessageId;
                break;
                
            case TextDeltaEvent textDelta:
                _state.CurrentStreamingText += textDelta.Text;
                break;
            
                
            case ToolCallStartEvent toolStart:
                _state.State = StreamingState.ExecutingTools;
                _state.ActiveToolCalls[toolStart.CallId] = new ToolCallState
                {
                    CallId = toolStart.CallId,
                    Name = toolStart.Name,
                    StartTime = DateTime.Now
                };
                _state.Stats.ToolCalls++;
                break;
                
            case ToolCallArgsEvent toolArgs:
                if (_state.ActiveToolCalls.TryGetValue(toolArgs.CallId, out var argsCall))
                {
                    argsCall.Args = toolArgs.ArgsJson;
                }
                break;
                
            case ToolCallEndEvent toolEnd:
                if (_state.ActiveToolCalls.TryGetValue(toolEnd.CallId, out var endCall))
                {
                    endCall.EndTime = DateTime.Now;
                    endCall.IsComplete = true;
                }
                break;
                
            case ToolCallResultEvent toolResult:
                if (_state.ActiveToolCalls.TryGetValue(toolResult.CallId, out var resultCall))
                {
                    resultCall.Result = toolResult.Result;
                    resultCall.IsError = toolResult.Result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);
                }
                break;
                
            case ReasoningMessageStartEvent:
                _state.State = StreamingState.Thinking;
                break;

            case ReasoningDeltaEvent reasoningDelta:
                // Accumulate reasoning content similar to text deltas
                _state.CurrentStreamingText += reasoningDelta.Text;
                break;

            case ReasoningMessageEndEvent:
                // Reasoning complete, return to idle state
                _state.State = StreamingState.Idle;
                break;
        }

        OnStateChanged?.Invoke();
    }
    
    public void AddUserMessage(string content)
    {
        _state.History.Add(new HistoryItem
        {
            Type = HistoryItemType.UserMessage,
            Content = content
        });
        _state.Stats.MessageCount++;
        OnStateChanged?.Invoke();
    }
    
    public void ClearHistory()
    {
        _state.History.Clear();
        _state.ActiveToolCalls.Clear();
        _state.CurrentStreamingText = "";
        OnStateChanged?.Invoke();
    }
}
