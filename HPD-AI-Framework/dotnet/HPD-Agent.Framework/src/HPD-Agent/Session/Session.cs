using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Session represents a chat conversation container.
/// Contains metadata, session-scoped middleware state, and provides access to the session store.
/// Does NOT contain messages - messages are in Branch objects.
/// </summary>
/// <remarks>
/// <para><b>Architecture:</b></para>
/// <para>
/// Session is the top-level container that holds:
/// - Metadata (user info, project context, etc.)
/// - Session-scoped middleware state (permissions, user preferences - shared across all branches)
/// - Reference to session store (for asset access)
/// </para>
///
/// <para><b>Relationship to Branch:</b></para>
/// <para>
/// One Session can have multiple Branches (conversation paths).
/// Each Branch references the same Session via SessionId.
/// </para>
///
/// <para><b>V3 Architecture:</b></para>
/// <para>
/// Session holds metadata; Branch holds messages.
/// This split enables multiple conversation paths (branches) within one session.
/// </para>
/// </remarks>
public class Session
{
    /// <summary>Unique identifier for this session</summary>
    public string Id { get; init; }

    /// <summary>When this session was created</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Last time any branch in this session was updated</summary>
    public DateTime LastActivity { get; set; }

    /// <summary>Session-level metadata (not branch-specific)</summary>
    public Dictionary<string, object> Metadata { get; init; }

    /// <summary>
    /// Session-scoped middleware persistent state.
    /// Stores state that applies across all branches (e.g., permission choices, user preferences).
    /// Only middleware marked with [MiddlewareState(Persistent = true, Scope = StateScope.Session)]
    /// is persisted here. Branch-scoped state lives in Branch.MiddlewareState instead.
    /// </summary>
    /// <remarks>
    /// <para><b>Examples of session-scoped persistent state:</b></para>
    /// <list type="bullet">
    /// <item>PermissionPersistentState: "Always Allow Bash" applies to all branches</item>
    /// <item>User preferences: Theme, language, etc.</item>
    /// </list>
    /// </remarks>
    public Dictionary<string, string> MiddlewareState { get; init; }

    /// <summary>Reference to session store (for asset access)</summary>
    [JsonIgnore]
    public ISessionStore? Store { get; set; }

    /// <summary>
    /// Creates a new session with a generated ID.
    /// Internal - only the framework creates sessions via Agent.LoadSessionAndBranchAsync().
    /// </summary>
    internal Session()
    {
        Id = Guid.NewGuid().ToString();
        CreatedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;
        Metadata = [];
        MiddlewareState = [];
    }

    /// <summary>
    /// Creates a new session with a specific ID.
    /// Internal - only the framework creates sessions via Agent.LoadSessionAndBranchAsync().
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    internal Session(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        Id = sessionId;
        CreatedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;
        Metadata = [];
        MiddlewareState = [];
    }

    /// <summary>
    /// Creates a session with specific values (for deserialization).
    /// </summary>
    [JsonConstructor]
    internal Session(
        string id,
        DateTime createdAt,
        DateTime lastActivity,
        Dictionary<string, object> metadata,
        Dictionary<string, string> middlewareState)
    {
        Id = id;
        CreatedAt = createdAt;
        LastActivity = lastActivity;
        Metadata = metadata;
        MiddlewareState = middlewareState;
    }

    /// <summary>
    /// Add metadata key/value pair to this session.
    /// </summary>
    public void AddMetadata(string key, object value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or whitespace", nameof(key));

        Metadata[key] = value;
        LastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// Sets session-scoped middleware persistent state for a given key.
    /// </summary>
    internal void SetMiddlewareState(string key, string jsonValue)
    {
        MiddlewareState[key] = jsonValue;
        LastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets session-scoped middleware persistent state for a given key.
    /// </summary>
    internal string? GetMiddlewareState(string key)
    {
        return MiddlewareState.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Convenience method to save this session to its associated store.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when Store is null.
    /// </exception>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (Store == null)
            throw new InvalidOperationException(
                "Session has no associated store. " +
                "Load the session using store.LoadSessionAsync() to set the store reference.");

        await Store.SaveSessionAsync(this, cancellationToken);
    }

    /// <summary>
    /// Creates a new branch owned by this session.
    /// Internal - only the framework creates branches via Agent.LoadSessionAndBranchAsync() or Agent.ForkBranchAsync().
    /// </summary>
    /// <param name="branchId">Branch ID (defaults to generated GUID)</param>
    /// <returns>A new Branch linked to this Session</returns>
    internal Branch CreateBranch(string? branchId = null)
    {
        var id = branchId ?? Guid.NewGuid().ToString();
        return new Branch(Id, id) { Session = this };
    }
}


//──────────────────────────────────────────────────────────────────
// SESSION SNAPSHOT: Lightweight session state (messages + metadata)
// Legacy V2 type — kept for backward compatibility with existing stored data.
//──────────────────────────────────────────────────────────────────

/// <summary>
/// Lightweight snapshot of session state (messages, metadata, middleware persistent state).
/// Used for loading V2 session data and migration to V3 Session + Branch format.
/// </summary>
public record SessionSnapshot
{
    /// <summary>
    /// Gets the identifier of the session this snapshot represents.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Unique identifier for this snapshot (for history tracking).
    /// </summary>
    public string? SessionSnapshotId { get; init; }

    /// <summary>
    /// Gets the ordered list of chat messages captured in the snapshot.
    /// </summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>
    /// Gets the session metadata persisted with this snapshot.
    /// </summary>
    public required Dictionary<string, object> Metadata { get; init; }

    /// <summary>
    /// Gets optional persistent state maintained by middleware components.
    /// </summary>
    public Dictionary<string, string>? MiddlewarePersistentState { get; init; }

    /// <summary>
    /// Gets the UTC date/time when this session was created.
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// Gets the UTC date/time of the last activity recorded in this snapshot.
    /// </summary>
    public required DateTime LastActivity { get; init; }

    /// <summary>
    /// Gets an optional conversation identifier associated with this snapshot.
    /// </summary>
    public string? ConversationId { get; init; }

    /// <summary>
    /// Version for schema evolution.
    /// </summary>
    public int Version { get; init; } = 1;
}
