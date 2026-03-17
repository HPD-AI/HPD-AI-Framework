namespace HPD.Agent;

/// <summary>
/// Persistent state for permission middleware. Immutable record with static abstract key.
/// Stores permission choices that should persist across agent runs within the same session.
/// </summary>
/// <remarks>
/// <para><b>Session Scoping:</b></para>
/// <para>
/// Permission preferences are stored per-session in Session.MiddlewareState.
/// This means each session has its own independent permission choices, shared across all branches.
/// There is NO global permission storage - all permissions are session-scoped.
/// </para>
///
/// <para><b>Storage Format:</b></para>
/// <para>
/// Dictionary&lt;string, PermissionChoice&gt; where key is function name.
/// Serialized as JSON and stored in Session.MiddlewareState (session-scoped, shared by all branches).
/// </para>
///
/// <para><b>Usage:</b></para>
/// <code>
/// // Read state
/// var permState = context.State.MiddlewareState.PermissionPersistent ?? new();
/// var choice = permState.GetPermission("functionName");
///
/// // Update state
/// var updated = permState.WithPermission("functionName", PermissionChoice.AlwaysAllow);
/// context.UpdateState(s => s with
/// {
///     MiddlewareState = s.MiddlewareState.WithPermissionPersistent(updated)
/// });
/// </code>
///
/// <para><b>Persistence:</b></para>
/// <para>
/// This state is automatically saved to Session.MiddlewareState
/// at the end of each agent run via SaveToSession() (session-scoped).
/// It is loaded back via LoadFromSession() at agent start.
/// </para>
/// </remarks>
[MiddlewareState(Persistent = true, Scope = StateScope.Session)]
public sealed record PermissionPersistentStateData
{
    /// <summary>
    /// Permission choices stored by function name.
    /// Key: Function name (e.g., "Bash", "Read", "Write")
    /// Value: Permission choice (AlwaysAllow, AlwaysDeny, Ask)
    /// </summary>
    public IReadOnlyDictionary<string, PermissionChoice> Permissions { get; init; }
        = new Dictionary<string, PermissionChoice>();

    /// <summary>
    /// Gets the stored permission choice for a function.
    /// Returns null if no preference is stored.
    /// </summary>
    /// <param name="functionName">Function name to look up</param>
    /// <returns>Stored permission choice, or null if not found</returns>
    public PermissionChoice? GetPermission(string functionName)
    {
        if (Permissions.TryGetValue(functionName, out var choice))
            return choice;
        return null;
    }

    /// <summary>
    /// Creates a new state with an updated permission choice.
    /// </summary>
    /// <param name="functionName">Function name</param>
    /// <param name="choice">Permission choice to store</param>
    /// <returns>New state with updated permission</returns>
    public PermissionPersistentStateData WithPermission(string functionName, PermissionChoice choice)
    {
        var updated = new Dictionary<string, PermissionChoice>(Permissions)
        {
            [functionName] = choice
        };

        return this with { Permissions = updated };
    }

    /// <summary>
    /// Creates a new state with a permission removed.
    /// </summary>
    /// <param name="functionName">Function name to remove</param>
    /// <returns>New state without the specified permission</returns>
    public PermissionPersistentStateData WithoutPermission(string functionName)
    {
        if (!Permissions.ContainsKey(functionName))
            return this; // No change needed

        var updated = new Dictionary<string, PermissionChoice>(Permissions);
        updated.Remove(functionName);

        return this with { Permissions = updated };
    }

    /// <summary>
    /// Creates a new state with all permissions cleared.
    /// </summary>
    /// <returns>New state with empty permission dictionary</returns>
    public PermissionPersistentStateData Clear()
    {
        return this with { Permissions = new Dictionary<string, PermissionChoice>() };
    }
}
