using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Session represents a chat conversation container.
/// Contains metadata, session-scoped middleware state, and provides access to the session store.
/// Does NOT contain messages - messages are in Thread objects.
/// </summary>
/// <remarks>
/// <para><b>Architecture:</b></para>
/// <para>
/// Session is the top-level container that holds:
/// - Metadata (user info, project context, etc.)
/// - Session-scoped middleware state (permissions, user preferences - shared across all threads)
/// - Reference to session store (for session and thread persistence)
/// </para>
///
/// <para><b>Relationship to Thread:</b></para>
/// <para>
/// One Session can have multiple Threads (conversation paths).
/// Each Thread references the same Session via SessionId.
/// </para>
///
/// <para><b>V3 Architecture:</b></para>
/// <para>
/// Session holds metadata; Thread holds messages.
/// This split enables multiple conversation paths (threads) within one session.
/// </para>
/// </remarks>
public class Session
{
    /// <summary>Unique identifier for this session</summary>
    public string Id { get; init; }

    /// <summary>When this session was created</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Last time any thread in this session was updated</summary>
    public DateTime LastActivity { get; set; }

    /// <summary>Session-level metadata (not thread-specific)</summary>
    public Dictionary<string, object> Metadata { get; init; }

    /// <summary>
    /// Session-scoped middleware persistent state.
    /// Stores state that applies across all threads (e.g., permission choices, user preferences).
    /// Only middleware marked with [MiddlewareState(Persistent = true, Scope = StateScope.Session)]
    /// is persisted here. Thread-scoped state lives in Thread.MiddlewareState instead.
    /// </summary>
    /// <remarks>
    /// <para><b>Examples of session-scoped persistent state:</b></para>
    /// <list type="bullet">
    /// <item>Versioned permission preferences may apply across threads in this session.</item>
    /// <item>User preferences: Theme, language, etc.</item>
    /// </list>
    /// </remarks>
    public Dictionary<string, string> MiddlewareState { get; init; }

    /// <summary>Reference to session store (for session and thread persistence)</summary>
    [JsonIgnore]
    public ISessionStore? Store { get; set; }

    /// <summary>
    /// Creates a new session with a generated ID.
    /// Internal - only the framework creates sessions via Agent.LoadSessionAndThreadAsync().
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
    /// Internal - only the framework creates sessions via Agent.LoadSessionAndThreadAsync().
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
    /// Creates a new thread owned by this session.
    /// Internal - only the framework creates threads via Agent.LoadSessionAndThreadAsync() or Agent.ForkThreadAsync().
    /// </summary>
    /// <param name="defaultAgentId">Agent identity selected when continuation does not choose another agent.</param>
    /// <param name="threadId">Thread ID (defaults to generated GUID).</param>
    /// <returns>A new Thread linked to this Session</returns>
    internal Thread CreateThread(string defaultAgentId, string? threadId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultAgentId);
        var id = threadId ?? Guid.NewGuid().ToString();
        return new Thread(Id, id, defaultAgentId) { Session = this };
    }
}
