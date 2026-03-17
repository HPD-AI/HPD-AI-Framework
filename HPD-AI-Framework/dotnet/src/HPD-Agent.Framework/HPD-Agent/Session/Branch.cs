using Microsoft.Extensions.AI;
using System.Text.Json.Serialization;

namespace HPD.Agent;

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
/// - All branches share the same session (metadata, assets, session-scoped state)
/// </para>
///
/// <para><b>Relationship to Session:</b></para>
/// <para>
/// Branch belongs to a Session (via SessionId).
/// Multiple branches can exist in one session, all sharing:
/// - Session metadata
/// - Session assets (uploaded files)
/// - Session-scoped middleware state (permissions, preferences)
/// </para>
///
/// <para><b>Branch-Scoped vs Session-Scoped:</b></para>
/// <list type="bullet">
/// <item><b>Branch-scoped:</b> Messages, plan progress, history cache (diverges per branch)</item>
/// <item><b>Session-scoped:</b> Permissions, assets, user preferences (shared across branches)</item>
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
    public string? ForkedFrom { get; init; }

    /// <summary>
    /// Index of the last shared message before this branch diverges from its siblings (null for original branches).
    /// The first diverging message is at ForkedAtMessageIndex + 1.
    /// Siblings are grouped by ForkedFrom + ForkedAtMessageIndex — all branches that share the same preceding context.
    /// </summary>
    public int? ForkedAtMessageIndex { get; init; }

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
    /// Siblings are branches that forked from the same parent at the same message index.
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
    /// <item>HistoryReductionState: Conversation summarization cache</item>
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
        CreatedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;

        // V3: Initialize tree navigation properties with safe defaults
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
        CreatedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;

        // V3: Initialize tree navigation properties with safe defaults
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
        CreatedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;

        // V3: Initialize tree navigation properties with safe defaults
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
        int? forkedAtMessageIndex,
        DateTime createdAt,
        DateTime lastActivity,
        string? name,
        string? description,
        List<string>? tags,
        Dictionary<string, string>? ancestors,
        Dictionary<string, string> middlewareState,
        // V3: Tree navigation properties (with safe defaults for backward compatibility)
        int siblingIndex = 0,
        int totalSiblings = 1,
        bool isOriginal = true,
        string? originalBranchId = null,
        string? previousSiblingId = null,
        string? nextSiblingId = null,
        List<string>? childBranches = null)
    {
        Id = id;
        SessionId = sessionId;
        Messages = messages;
        ForkedFrom = forkedFrom;
        ForkedAtMessageIndex = forkedAtMessageIndex;
        CreatedAt = createdAt;
        LastActivity = lastActivity;
        Name = name;
        Description = description;
        Tags = tags;
        Ancestors = ancestors;
        MiddlewareState = middlewareState;

        // V3: Tree navigation properties
        SiblingIndex = siblingIndex;
        TotalSiblings = totalSiblings;
        IsOriginal = isOriginal;
        OriginalBranchId = originalBranchId;
        PreviousSiblingId = previousSiblingId;
        NextSiblingId = nextSiblingId;
        ChildBranches = childBranches ?? [];
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

    /// <summary>
    /// Sets branch-scoped middleware persistent state for a given key.
    /// </summary>
    internal void SetMiddlewareState(string key, string jsonValue)
    {
        MiddlewareState[key] = jsonValue;
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
    /// V3: Check if this branch is a leaf (has no children).
    /// </summary>
    public bool IsLeaf => ChildBranches.Count == 0;

    /// <summary>
    /// V3: Check if this branch is the root (no parent).
    /// </summary>
    public bool IsRoot => ForkedFrom == null;

    /// <summary>
    /// V3: Validate branch tree invariants.
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
