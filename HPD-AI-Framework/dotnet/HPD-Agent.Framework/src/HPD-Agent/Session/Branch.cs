using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent;

public enum BranchKind
{
    Conversation,
    SubAgent
}

public enum BranchVisibility
{
    Visible,
    Hidden
}

/// <summary>
/// Branch represents a conversation path within a session.
/// Contains messages and branch-specific state.
/// Multiple branches can exist in one session (for exploring alternatives).
/// </summary>
/// <remarks>
/// <para><b>Mental Model:</b></para>
/// <para>
/// Think of branches like ChatGPT's message editing feature:
/// - User edits a message → creates a new branch from that point
/// - Each branch is an independent conversation path
/// - All branches share the same session (metadata and session-scoped state)
/// </para>
///
/// <para><b>Relationship to Session:</b></para>
/// <para>
/// Branch belongs to a Session (via SessionId).
/// Multiple branches can exist in one session, all sharing:
/// - Session metadata
/// - Session-scoped middleware state (permissions, preferences)
/// </para>
///
/// <para><b>Branch-Scoped vs Session-Scoped:</b></para>
/// <list type="bullet">
/// <item><b>Branch-scoped:</b> Messages, plan progress, history cache (diverges per branch)</item>
/// <item><b>Session-scoped:</b> Permissions and user preferences (shared across branches)</item>
/// </list>
/// </remarks>
public class Branch
{
    /// <summary>Unique identifier for this branch</summary>
    public string Id { get; init; }

    /// <summary>Parent session ID</summary>
    public string SessionId { get; init; }

    /// <summary>
    /// Back-reference to the parent Session.
    /// Set by Session.CreateBranch() and by the framework when loading from store.
    /// Not serialized — reconstructed at load time.
    /// </summary>
    [JsonIgnore]
    public Session? Session { get; internal set; }

    /// <summary>Conversation messages in this branch</summary>
    public List<ChatMessage> Messages { get; init; }

    /// <summary>Source branch ID if this was forked (null for original branches)</summary>
    public string? ForkedFrom { get; internal set; }

    /// <summary>
    /// Message id of the last shared message before this branch diverges from its siblings (null for original branches).
    /// Siblings are grouped by ForkedFrom + ForkedAtMessageId.
    /// </summary>
    public string? ForkedAtMessageId { get; internal set; }

    /// <summary>
    /// Resolved index of the last shared message when the fork was created (null for original branches).
    /// This is diagnostic metadata only; fork identity is ForkedAtMessageId.
    /// </summary>
    public int? ForkedAtMessageIndex { get; internal set; }

    /// <summary>When this branch was created</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Last time this branch was updated</summary>
    public DateTime LastActivity { get; set; }

    /// <summary>
    /// Optional display name for this branch.
    /// Used as the primary label in UI (e.g., "Feature Branch", "Experiment 1").
    /// If not set, GetDisplayName() will fall back to Description or generate a name from first message.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Optional user-friendly description of this branch.
    /// Useful for explaining the purpose or approach of this conversation variant.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional tags for categorizing or filtering branches.
    /// Examples: ["draft", "formal-tone"], ["v1", "experiment"]
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Arbitrary branch-level application metadata.
    /// Use this for UI/app state that belongs to one conversation path rather than the whole session.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; }

    /// <summary>
    /// Runtime classification for this branch. Infrastructure uses this instead of magic metadata keys.
    /// </summary>
    public BranchKind Kind { get; set; } = BranchKind.Conversation;

    /// <summary>
    /// Whether this branch should be shown in ordinary branch lists.
    /// Subagent branches default to hidden.
    /// </summary>
    public BranchVisibility Visibility { get; set; } = BranchVisibility.Visible;

    /// <summary>Parent session for runtime child branches such as subagents.</summary>
    public string? ParentSessionId { get; set; }

    /// <summary>Parent branch for runtime child branches such as subagents.</summary>
    public string? ParentBranchId { get; set; }

    /// <summary>Name of the subagent that owns this branch, when Kind is SubAgent.</summary>
    public string? SubAgentName { get; set; }

    /// <summary>Run id of the subagent invocation that created this branch.</summary>
    public string? SubAgentRunId { get; set; }

    /// <summary>Source kind for the subagent definition, when Kind is SubAgent.</summary>
    public string? SubAgentSourceKind { get; set; }

    /// <summary>Parent tool call id that created this child branch.</summary>
    public string? ParentToolCallId { get; set; }

    /// <summary>Subagent session policy captured for inspection and routing.</summary>
    public string? SessionPolicy { get; set; }

    /// <summary>Subagent branch policy captured for inspection and routing.</summary>
    public string? BranchPolicy { get; set; }

    /// <summary>
    /// Full ancestry chain for multi-level fork tracking.
    /// Key: depth (0 = root), Value: branch ID at that depth.
    /// Example: { "0": "main", "1": "experimental", "2": "formal" }
    /// Enables UI to show "main → experimental → formal" lineage.
    /// </summary>
    public Dictionary<string, string>? Ancestors { get; set; }

    // ============================================
    // NEW: Tree Structure Navigation (V3)
    // ============================================

    /// <summary>
    /// Position among siblings at this fork point (0-based).
    /// Siblings are branches that forked from the same parent at the same message id.
    /// Stable ordering: original branch = 0, subsequent forks ordered chronologically.
    /// </summary>
    public int SiblingIndex { get; set; }

    /// <summary>
    /// Total number of sibling branches at this fork point (including this branch).
    /// Updated atomically when siblings are added or removed.
    /// </summary>
    public int TotalSiblings { get; set; }

    /// <summary>
    /// True if this is the original branch (not forked from another).
    /// Equivalent to: ForkedFrom == null
    /// Denormalized for query convenience.
    /// </summary>
    public bool IsOriginal { get; set; }

    /// <summary>
    /// ID of the original branch in this sibling group.
    /// For original branches: null
    /// For forked branches: ID of the branch they forked from
    /// </summary>
    public string? OriginalBranchId { get; set; }

    // ============================================
    // NEW: Navigation Pointers
    // ============================================

    /// <summary>
    /// ID of the previous sibling (sibling at index - 1).
    /// Null if this is the first sibling (SiblingIndex == 0).
    /// Enables O(1) previous sibling navigation without scanning.
    /// </summary>
    public string? PreviousSiblingId { get; set; }

    /// <summary>
    /// ID of the next sibling (sibling at index + 1).
    /// Null if this is the last sibling (SiblingIndex == TotalSiblings - 1).
    /// Enables O(1) next sibling navigation without scanning.
    /// </summary>
    public string? NextSiblingId { get; set; }

    // ============================================
    // NEW: Child Tracking
    // ============================================

    /// <summary>
    /// IDs of branches that forked directly from this branch.
    /// Updated when:
    /// - A branch forks from this one (add to list)
    /// - A child branch is deleted (remove from list)
    /// Enables O(1) "show forks" without scanning all branches.
    /// </summary>
    public List<string> ChildBranches { get; set; } = new();

    /// <summary>
    /// Count of direct child branches (forks from this branch).
    /// Computed property: ChildBranches.Count
    /// Denormalized for API convenience.
    /// </summary>
    public int TotalForks => ChildBranches.Count;

    /// <summary>
    /// Branch-scoped middleware persistent state.
    /// Stores state tied to this specific conversation path (e.g., plan progress, summarization cache).
    /// Only middleware marked with [MiddlewareState(Persistent = true, Scope = StateScope.Branch)]
    /// (or just [MiddlewareState(Persistent = true)] since Branch is the default) is persisted here.
    /// Session-scoped state (e.g., permissions) lives in Session.MiddlewareState instead.
    /// </summary>
    /// <remarks>
    /// <para><b>Examples of branch-scoped persistent state:</b></para>
    /// <list type="bullet">
    /// <item>PlanModePersistentState: Current plan steps and progress</item>
    /// <item>CompactionState: Conversation summarization cache</item>
    /// </list>
    ///
    /// <para>
    /// State is serialized as JSON and saved per branch because different branches
    /// have different conversation contexts (different messages → different caches/progress).
    /// </para>
    ///
    /// <para><b>On fork:</b> Branch middleware state is COPIED from the source branch.</para>
    /// <para><b>After fork:</b> Each branch maintains its own copy and can diverge independently.</para>
    /// </remarks>
    public Dictionary<string, string> MiddlewareState { get; init; }

    /// <summary>Current execution state (for crash recovery, null when idle)</summary>
    [JsonIgnore]
    public AgentLoopState? ExecutionState { get; set; }

    /// <summary>
    /// Parameterless constructor for JSON deserialization.
    /// Properties are populated via init setters.
    /// </summary>
    internal Branch()
    {
        Id = Guid.NewGuid().ToString();
        SessionId = string.Empty;
        Messages = [];
        MiddlewareState = [];
        Metadata = [];
        CreatedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;

        //  Initialize tree navigation properties with safe defaults
        SiblingIndex = 0;
        TotalSiblings = 1;
        IsOriginal = true;
        ChildBranches = [];
    }

    /// <summary>
    /// Creates a new branch with a generated ID.
    /// Internal - only the framework creates branches via Session.CreateBranch() or Agent methods.
    /// </summary>
    internal Branch(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        Id = Guid.NewGuid().ToString();
        SessionId = sessionId;
        Messages = [];
        MiddlewareState = [];
        Metadata = [];
        CreatedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;

        //  Initialize tree navigation properties with safe defaults
        SiblingIndex = 0;
        TotalSiblings = 1;
        IsOriginal = true;
        ChildBranches = [];
    }

    /// <summary>
    /// Creates a new branch with a specific ID.
    /// Internal - only the framework creates branches via Session.CreateBranch() or Agent methods.
    /// </summary>
    internal Branch(string sessionId, string branchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        Id = branchId;
        SessionId = sessionId;
        Messages = [];
        MiddlewareState = [];
        Metadata = [];
        CreatedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;

        //  Initialize tree navigation properties with safe defaults
        SiblingIndex = 0;
        TotalSiblings = 1;
        IsOriginal = true;
        ChildBranches = [];
    }

    /// <summary>
    /// Creates a branch with specific values (for deserialization).
    /// </summary>
    [JsonConstructor]
    internal Branch(
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
        //  Tree navigation properties
        int siblingIndex,
        int totalSiblings,
        bool isOriginal,
        List<string>? childBranches,
        string? originalBranchId = null,
        string? previousSiblingId = null,
        string? nextSiblingId = null,
        BranchKind kind = BranchKind.Conversation,
        BranchVisibility visibility = BranchVisibility.Visible,
        string? parentSessionId = null,
        string? parentBranchId = null,
        string? subAgentName = null,
        string? subAgentRunId = null,
        string? subAgentSourceKind = null,
        string? parentToolCallId = null,
        string? sessionPolicy = null,
        string? branchPolicy = null)
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
        Kind = kind;
        Visibility = visibility;
        ParentSessionId = parentSessionId;
        ParentBranchId = parentBranchId;
        SubAgentName = subAgentName;
        SubAgentRunId = subAgentRunId;
        SubAgentSourceKind = subAgentSourceKind;
        ParentToolCallId = parentToolCallId;
        SessionPolicy = sessionPolicy;
        BranchPolicy = branchPolicy;
        Ancestors = ancestors;
        MiddlewareState = middlewareState;

        //  Tree navigation properties
        if (totalSiblings <= 0)
            throw new JsonException("Branch JSON is missing or has invalid required tree property 'totalSiblings'.");
        if (siblingIndex < 0 || siblingIndex >= totalSiblings)
            throw new JsonException("Branch JSON is missing or has invalid required tree property 'siblingIndex'.");

        SiblingIndex = siblingIndex;
        TotalSiblings = totalSiblings;
        IsOriginal = isOriginal;
        OriginalBranchId = originalBranchId;
        PreviousSiblingId = previousSiblingId;
        NextSiblingId = nextSiblingId;
        ChildBranches = childBranches ?? throw new JsonException("Branch JSON is missing required tree property 'childBranches'.");
    }

    /// <summary>
    /// Gets the number of messages in this branch.
    /// </summary>
    public int MessageCount => Messages.Count;

    /// <summary>
    /// Adds a message to the branch.
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
    /// Adds multiple messages to the branch.
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
            Kind = BranchKind.SubAgent;
        }

        if (TryRemoveString(metadata, "visibility", out var visibility))
        {
            Visibility = string.Equals(visibility, "hidden", StringComparison.OrdinalIgnoreCase)
                ? BranchVisibility.Hidden
                : BranchVisibility.Visible;
        }

        if (TryRemoveString(metadata, "parentSessionId", out var parentSessionId))
            ParentSessionId = parentSessionId;
        if (TryRemoveString(metadata, "parentBranchId", out var parentBranchId))
            ParentBranchId = parentBranchId;
        if (TryRemoveString(metadata, "subAgentName", out var subAgentName))
            SubAgentName = subAgentName;
        if (TryRemoveString(metadata, "subAgentRunId", out var subAgentRunId))
            SubAgentRunId = subAgentRunId;
        if (TryRemoveString(metadata, "subAgentSourceKind", out var subAgentSourceKind))
            SubAgentSourceKind = subAgentSourceKind;
        if (TryRemoveString(metadata, "parentToolCallId", out var parentToolCallId))
            ParentToolCallId = parentToolCallId;
        if (TryRemoveString(metadata, "sessionPolicy", out var sessionPolicy))
            SessionPolicy = sessionPolicy;
        if (TryRemoveString(metadata, "branchPolicy", out var branchPolicy))
            BranchPolicy = branchPolicy;

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
    /// Sets branch-scoped middleware persistent state for a given key.
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
        IsOriginal = forkedFrom is null;
        OriginalBranchId = forkedFrom;
        LastActivity = DateTime.UtcNow;
    }

    internal void SetTreeMetadata(
        string? forkedFrom,
        string? forkedAtMessageId,
        int? forkedAtMessageIndex,
        int siblingIndex,
        int totalSiblings,
        bool isOriginal,
        string? originalBranchId,
        string? previousSiblingId,
        string? nextSiblingId,
        List<string> childBranches)
    {
        ForkedFrom = forkedFrom;
        ForkedAtMessageId = forkedAtMessageId;
        ForkedAtMessageIndex = forkedAtMessageIndex;
        SiblingIndex = siblingIndex;
        TotalSiblings = totalSiblings;
        IsOriginal = isOriginal;
        OriginalBranchId = originalBranchId;
        PreviousSiblingId = previousSiblingId;
        NextSiblingId = nextSiblingId;
        ChildBranches = childBranches;
        LastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets branch-scoped middleware persistent state for a given key.
    /// </summary>
    internal string? GetMiddlewareState(string key)
    {
        return MiddlewareState.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Clear all messages from this branch.
    /// </summary>
    public void Clear()
    {
        Messages.Clear();
        LastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// Get a display name for this branch based on Name, Description, or first user message.
    /// Useful for UI display in branch lists.
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
            return Id; // Use branch ID as last resort

        var text = firstUserMessage.Text ?? string.Empty;
        if (text.Length <= maxLength)
            return text;

        return text.Substring(0, maxLength - 3) + "...";
    }

    /// <summary>
    ///  Check if this branch is a leaf (has no children).
    /// </summary>
    public bool IsLeaf => ChildBranches.Count == 0;

    /// <summary>
    ///  Check if this branch is the root (no parent).
    /// </summary>
    public bool IsRoot => ForkedFrom == null;

    /// <summary>
    ///  Validate branch tree invariants.
    /// Throws InvalidOperationException if any invariant is violated.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when tree invariants are violated</exception>
    public void ValidateTreeInvariants()
    {
        // Invariant 1: Original branches
        if ((ForkedFrom == null) != IsOriginal)
        {
            throw new InvalidOperationException(
                $"Branch {Id}: IsOriginal={IsOriginal} but ForkedFrom={ForkedFrom ?? "null"}");
        }

        // Invariant 2: Sibling index range
        if (SiblingIndex < 0 || SiblingIndex >= TotalSiblings)
        {
            throw new InvalidOperationException(
                $"Branch {Id}: SiblingIndex={SiblingIndex} out of range [0, {TotalSiblings})");
        }

        // Invariant 3: Total siblings must be positive
        if (TotalSiblings <= 0)
        {
            throw new InvalidOperationException(
                $"Branch {Id}: TotalSiblings={TotalSiblings} must be positive");
        }

        // Invariant 4: First sibling
        if (SiblingIndex == 0 && PreviousSiblingId != null)
        {
            throw new InvalidOperationException(
                $"Branch {Id}: First sibling (index=0) has PreviousSiblingId={PreviousSiblingId}");
        }

        // Invariant 5: Last sibling
        if (SiblingIndex == TotalSiblings - 1 && NextSiblingId != null)
        {
            throw new InvalidOperationException(
                $"Branch {Id}: Last sibling (index={TotalSiblings - 1}) has NextSiblingId={NextSiblingId}");
        }

        // Invariant 6: Middle siblings must have both pointers
        if (SiblingIndex > 0 && PreviousSiblingId == null)
        {
            throw new InvalidOperationException(
                $"Branch {Id}: Middle sibling (index={SiblingIndex}) has null PreviousSiblingId");
        }

        if (SiblingIndex < TotalSiblings - 1 && NextSiblingId == null)
        {
            throw new InvalidOperationException(
                $"Branch {Id}: Middle sibling (index={SiblingIndex}) has null NextSiblingId");
        }

        // Invariant 7: Original branch ID consistency
        if (IsOriginal && OriginalBranchId != null)
        {
            throw new InvalidOperationException(
                $"Branch {Id}: Original branch should have OriginalBranchId=null, but has {OriginalBranchId}");
        }
    }
}
