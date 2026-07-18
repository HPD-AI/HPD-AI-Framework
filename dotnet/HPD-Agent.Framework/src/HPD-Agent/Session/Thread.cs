using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent;

public enum ThreadKind
{
    MainAgent,
    SubAgent
}

public enum ThreadVisibility
{
    Visible,
    Hidden
}

/// <summary>
/// Thread represents a conversation path within a session.
/// Its durable history is the thread event stream; <see cref="Messages"/> is the
/// projected read model for middleware and application code.
/// </summary>
/// <remarks>
/// <para><b>Mental Model:</b></para>
/// <para>
/// Think of threads like ChatGPT's message editing feature:
/// - User edits or retries a turn -> creates a new thread at that message boundary
/// - The new thread receives a cloned event prefix from the source path
/// - Each thread then continues as an independent event stream
/// - Fork groups are derived from lineage and message boundaries, not stored neighbor pointers
/// </para>
///
/// <para><b>Relationship to Session:</b></para>
/// <para>
/// Thread belongs to a Session (via SessionId).
/// Multiple threads can exist in one session, all sharing:
/// - Session metadata
/// - Session-scoped middleware state (permissions, preferences)
/// </para>
///
/// <para><b>Thread-Scoped vs Session-Scoped:</b></para>
/// <list type="bullet">
/// <item><b>Thread-scoped:</b> Event stream, projected messages, plan progress, history cache (diverges per thread)</item>
/// <item><b>Session-scoped:</b> Permissions and user preferences (shared across threads)</item>
/// </list>
/// </remarks>
public class Thread
{
    /// <summary>Unique identifier for this thread</summary>
    public string Id { get; init; }

    /// <summary>Parent session ID</summary>
    public string SessionId { get; init; }

    /// <summary>
    /// Back-reference to the parent Session.
    /// Set by Session.CreateThread() and by the framework when loading from store.
    /// Not serialized — reconstructed at load time.
    /// </summary>
    [JsonIgnore]
    public Session? Session { get; internal set; }

    /// <summary>
    /// Projected conversation messages in this thread.
    /// Persistence is event-first; this list is rebuilt from the thread event document
    /// when a durable store is used.
    /// </summary>
    public List<ChatMessage> Messages { get; init; }

    /// <summary>Source thread ID if this was forked (null for original threads)</summary>
    public string? ForkedFrom { get; internal set; }

    /// <summary>
    /// Message id of the requested fork boundary (null for original/root forks).
    /// Fork groups are graph projections derived from visible lineage and this message boundary,
    /// not direct parent alone.
    /// </summary>
    public string? ForkedAtMessageId { get; internal set; }

    /// <summary>
    /// Resolved index of the requested fork boundary when the fork was created (null for original/root forks).
    /// This is placement metadata; exact fork identity is <see cref="ForkedAtMessageId"/>.
    /// </summary>
    public int? ForkedAtMessageIndex { get; internal set; }

    /// <summary>When this thread was created</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Last time this thread was updated</summary>
    public DateTime LastActivity { get; set; }

    /// <summary>
    /// Optional display name for this thread.
    /// Used as the primary label in UI (e.g., "Feature Thread", "Experiment 1").
    /// If not set, GetDisplayName() will fall back to Description or generate a name from first message.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Optional user-friendly description of this thread.
    /// Useful for explaining the purpose or approach of this conversation variant.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional tags for categorizing or filtering threads.
    /// Examples: ["draft", "formal-tone"], ["v1", "experiment"]
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Arbitrary thread-level application metadata.
    /// Use this for UI/app state that belongs to one conversation path rather than the whole session.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; }

    /// <summary>Stable agent definition that owns and reconstructs this thread.</summary>
    public string OwnerAgentId { get; set; } = string.Empty;

    /// <summary>
    /// Runtime classification for this thread. Infrastructure uses this instead of magic metadata keys.
    /// </summary>
    public ThreadKind Kind { get; set; } = ThreadKind.MainAgent;

    /// <summary>
    /// Whether this thread should be shown in ordinary thread lists.
    /// Subagent threads default to hidden.
    /// </summary>
    public ThreadVisibility Visibility { get; set; } = ThreadVisibility.Visible;

    /// <summary>Parent session for runtime child threads such as subagents.</summary>
    public string? ParentSessionId { get; set; }

    /// <summary>Parent thread for runtime child threads such as subagents.</summary>
    public string? ParentThreadId { get; set; }

    /// <summary>Name of the subagent that owns this thread, when Kind is SubAgent.</summary>
    public string? SubAgentName { get; set; }

    public string? SubAgentTaskName { get; set; }

    public string? SubAgentStatus { get; set; }

    /// <summary>Delegation invocation that created this subagent thread.</summary>
    public string? InvocationId { get; set; }

    /// <summary>Source kind for the subagent definition, when Kind is SubAgent.</summary>
    public string? SubAgentSourceKind { get; set; }

    /// <summary>Parent tool call id that created this child thread.</summary>
    public string? ParentToolCallId { get; set; }

    /// <summary>Subagent session policy captured for inspection and routing.</summary>
    public string? SessionPolicy { get; set; }

    /// <summary>Subagent thread policy captured for inspection and routing.</summary>
    public string? ThreadPolicy { get; set; }

    /// <summary>
    /// Full ancestry chain for multi-level fork tracking.
    /// Key: depth (0 = root), Value: thread ID at that depth.
    /// Example: { "0": "main", "1": "experimental", "2": "formal" }
    /// Enables UI to show "main → experimental → formal" lineage.
    /// </summary>
    public Dictionary<string, string>? Ancestors { get; set; }

    // ============================================
    // Direct Lineage Tracking
    // ============================================

    /// <summary>
    /// IDs of threads that forked directly from this thread.
    /// This is direct lineage only. User-visible fork groups are derived by
    /// <see cref="ThreadForkGraph"/> from all visible threads in the session.
    /// </summary>
    public List<string> ChildThreads { get; set; } = new();

    /// <summary>
    /// Count of direct child threads.
    /// Computed property: ChildThreads.Count
    /// Denormalized for API convenience.
    /// </summary>
    public int TotalForks => ChildThreads.Count;

    /// <summary>
    /// Thread-scoped middleware persistent state.
    /// Stores state tied to this specific conversation path (e.g., plan progress, summarization cache).
    /// Only middleware marked with [MiddlewareState(Persistent = true, Scope = StateScope.Thread)]
    /// (or just [MiddlewareState(Persistent = true)] since Thread is the default) is persisted here.
    /// Session-scoped state (e.g., permissions) lives in Session.MiddlewareState instead.
    /// </summary>
    /// <remarks>
    /// <para><b>Examples of thread-scoped persistent state:</b></para>
    /// <list type="bullet">
    /// <item>PlanModePersistentState: Current plan steps and progress</item>
    /// <item>CompactionState: Conversation summarization cache</item>
    /// </list>
    ///
    /// <para>
    /// State is serialized as JSON and saved per thread because different threads
    /// have different conversation contexts (different messages → different caches/progress).
    /// </para>
    ///
    /// <para><b>On fork:</b> Thread middleware state is COPIED from the source thread.</para>
    /// <para><b>After fork:</b> Each thread maintains its own copy and can diverge independently.</para>
    /// </remarks>
    public Dictionary<string, string> MiddlewareState { get; init; }

    /// <summary>
    /// Parameterless constructor for JSON deserialization.
    /// Properties are populated via init setters.
    /// </summary>
    internal Thread()
    {
        Id = Guid.NewGuid().ToString();
        SessionId = string.Empty;
        Messages = [];
        MiddlewareState = [];
        Metadata = [];
        CreatedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;

        ChildThreads = [];
    }

    /// <summary>
    /// Creates a new thread with a generated ID.
    /// Internal - only the framework creates threads via Session.CreateThread() or Agent methods.
    /// </summary>
    internal Thread(string sessionId, string ownerAgentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerAgentId);
        Id = Guid.NewGuid().ToString();
        SessionId = sessionId;
        OwnerAgentId = ownerAgentId;
        Messages = [];
        MiddlewareState = [];
        Metadata = [];
        CreatedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;

        ChildThreads = [];
    }

    /// <summary>
    /// Creates a new thread with a specific ID.
    /// Internal - only the framework creates threads via Session.CreateThread() or Agent methods.
    /// </summary>
    internal Thread(string sessionId, string threadId, string ownerAgentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerAgentId);
        Id = threadId;
        SessionId = sessionId;
        OwnerAgentId = ownerAgentId;
        Messages = [];
        MiddlewareState = [];
        Metadata = [];
        CreatedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;

        ChildThreads = [];
    }

    /// <summary>
    /// Creates a thread with specific values (for deserialization).
    /// </summary>
    [JsonConstructor]
    internal Thread(
        string id,
        string sessionId,
        List<ChatMessage> messages,
        string? forkedFrom,
        string? forkedAtMessageId,
        int? forkedAtMessageIndex,
        DateTime createdAt,
        DateTime lastActivity,
        string? name,
        string? description,
        List<string>? tags,
        Dictionary<string, string>? ancestors,
        Dictionary<string, string> middlewareState,
        Dictionary<string, object>? metadata,
        List<string>? childThreads,
        string ownerAgentId,
        ThreadKind kind = ThreadKind.MainAgent,
        ThreadVisibility visibility = ThreadVisibility.Visible,
        string? parentSessionId = null,
        string? parentThreadId = null,
        string? subAgentName = null,
        string? subAgentTaskName = null,
        string? subAgentStatus = null,
        string? invocationId = null,
        string? subAgentSourceKind = null,
        string? parentToolCallId = null,
        string? sessionPolicy = null,
        string? threadPolicy = null)
    {
        Id = id;
        SessionId = sessionId;
        Messages = messages;
        ForkedFrom = forkedFrom;
        ForkedAtMessageId = forkedAtMessageId;
        ForkedAtMessageIndex = forkedAtMessageIndex;
        CreatedAt = createdAt;
        LastActivity = lastActivity;
        Name = name;
        Description = description;
        Tags = tags;
        Metadata = metadata ?? [];
        OwnerAgentId = ownerAgentId;
        Kind = kind;
        Visibility = visibility;
        ParentSessionId = parentSessionId;
        ParentThreadId = parentThreadId;
        SubAgentName = subAgentName;
        SubAgentTaskName = subAgentTaskName;
        SubAgentStatus = subAgentStatus;
        InvocationId = invocationId;
        SubAgentSourceKind = subAgentSourceKind;
        ParentToolCallId = parentToolCallId;
        SessionPolicy = sessionPolicy;
        ThreadPolicy = threadPolicy;
        Ancestors = ancestors;
        MiddlewareState = middlewareState;

        ChildThreads = childThreads ?? throw new JsonException("Thread JSON is missing required tree property 'childThreads'.");
    }

    /// <summary>
    /// Gets the number of messages in this thread.
    /// </summary>
    public int MessageCount => Messages.Count;

    /// <summary>
    /// Adds a message to the thread.
    /// </summary>
    public void AddMessage(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.MessageId ??= Guid.NewGuid().ToString();
        message.CreatedAt ??= DateTimeOffset.UtcNow;
        Messages.Add(message);
        LastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// Adds multiple messages to the thread.
    /// </summary>
    public void AddMessages(IEnumerable<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var now = DateTimeOffset.UtcNow;
        foreach (var message in messages)
        {
            message.MessageId ??= Guid.NewGuid().ToString();
            message.CreatedAt ??= now;
            Messages.Add(message);
        }
        LastActivity = DateTime.UtcNow;
    }

    internal void ApplyRuntimeMetadata(Dictionary<string, object>? metadata)
    {
        if (metadata == null)
            return;

        if (TryRemoveString(metadata, "kind", out var kind) &&
            string.Equals(kind, "subagent", StringComparison.OrdinalIgnoreCase))
        {
            Kind = ThreadKind.SubAgent;
        }

        if (TryRemoveString(metadata, "visibility", out var visibility))
        {
            Visibility = string.Equals(visibility, "hidden", StringComparison.OrdinalIgnoreCase)
                ? ThreadVisibility.Hidden
                : ThreadVisibility.Visible;
        }

        if (TryRemoveString(metadata, "parentSessionId", out var parentSessionId))
            ParentSessionId = parentSessionId;
        if (TryRemoveString(metadata, "parentThreadId", out var parentThreadId))
            ParentThreadId = parentThreadId;
        if (TryRemoveString(metadata, "subAgentName", out var subAgentName))
            SubAgentName = subAgentName;
        if (TryRemoveString(metadata, "subAgentTaskName", out var subAgentTaskName))
            SubAgentTaskName = subAgentTaskName;
        if (TryRemoveString(metadata, "invocationId", out var invocationId))
            InvocationId = invocationId;
        if (TryRemoveString(metadata, "ownerAgentId", out var ownerAgentId))
            OwnerAgentId = ownerAgentId ?? string.Empty;
        if (TryRemoveString(metadata, "subAgentSourceKind", out var subAgentSourceKind))
            SubAgentSourceKind = subAgentSourceKind;
        if (TryRemoveString(metadata, "parentToolCallId", out var parentToolCallId))
            ParentToolCallId = parentToolCallId;
        if (TryRemoveString(metadata, "sessionPolicy", out var sessionPolicy))
            SessionPolicy = sessionPolicy;
        if (TryRemoveString(metadata, "threadPolicy", out var threadPolicy))
            ThreadPolicy = threadPolicy;

        metadata.Remove("createdBy");
    }

    private static bool TryRemoveString(Dictionary<string, object> metadata, string key, out string? value)
    {
        if (metadata.TryGetValue(key, out var raw))
        {
            metadata.Remove(key);
            value = Convert.ToString(raw);
            return !string.IsNullOrWhiteSpace(value);
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Sets thread-scoped middleware persistent state for a given key.
    /// </summary>
    internal void SetMiddlewareState(string key, string jsonValue)
    {
        MiddlewareState[key] = jsonValue;
        LastActivity = DateTime.UtcNow;
    }

    internal void SetForkMetadata(
        string? forkedFrom,
        string? forkedAtMessageId,
        int? forkedAtMessageIndex,
        Dictionary<string, string>? ancestors)
    {
        ForkedFrom = forkedFrom;
        ForkedAtMessageId = forkedAtMessageId;
        ForkedAtMessageIndex = forkedAtMessageIndex;
        Ancestors = ancestors;
        LastActivity = DateTime.UtcNow;
    }

    internal void SetTreeMetadata(
        string? forkedFrom,
        string? forkedAtMessageId,
        int? forkedAtMessageIndex,
        List<string> childThreads)
    {
        ForkedFrom = forkedFrom;
        ForkedAtMessageId = forkedAtMessageId;
        ForkedAtMessageIndex = forkedAtMessageIndex;
        ChildThreads = childThreads;
        LastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets thread-scoped middleware persistent state for a given key.
    /// </summary>
    internal string? GetMiddlewareState(string key)
    {
        return MiddlewareState.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Clear all messages from this thread.
    /// </summary>
    public void Clear()
    {
        Messages.Clear();
        LastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// Get a display name for this thread based on Name, Description, or first user message.
    /// Useful for UI display in thread lists.
    /// </summary>
    public string GetDisplayName(int maxLength = 30)
    {
        // Check for explicit name first
        if (!string.IsNullOrEmpty(Name))
        {
            return Name.Length <= maxLength
                ? Name
                : Name.Substring(0, maxLength - 3) + "...";
        }

        // Fall back to description
        if (!string.IsNullOrEmpty(Description))
        {
            return Description.Length <= maxLength
                ? Description
                : Description.Substring(0, maxLength - 3) + "...";
        }

        // Fall back to first user message
        var firstUserMessage = Messages.FirstOrDefault(m => m.Role == ChatRole.User);
        if (firstUserMessage == null)
            return Id; // Use thread ID as last resort

        var text = firstUserMessage.Text ?? string.Empty;
        if (text.Length <= maxLength)
            return text;

        return text.Substring(0, maxLength - 3) + "...";
    }

    /// <summary>
    ///  Check if this thread is a leaf (has no children).
    /// </summary>
    public bool IsLeaf => ChildThreads.Count == 0;

    /// <summary>
    ///  Check if this thread is the root (no parent).
    /// </summary>
    public bool IsRoot => ForkedFrom == null;

    /// <summary>
    ///  Validate thread tree invariants.
    /// Throws InvalidOperationException if any invariant is violated.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when tree invariants are violated</exception>
    public void ValidateTreeInvariants()
    {
        if (ForkedFrom == Id)
            throw new InvalidOperationException($"Thread {Id}: ForkedFrom cannot reference itself.");
    }
}
