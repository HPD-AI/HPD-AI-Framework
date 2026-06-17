namespace HPD.Agent;

/// <summary>
/// Options for configuring session persistence behavior.
/// </summary>
public class SessionStoreOptions
{
    /// <summary>
    /// Whether to automatically save session metadata and the active thread
    /// after each completed turn.
    /// When false, callers are responsible for explicitly saving changed
    /// session and thread state.
    /// Default: false (manual save).
    /// </summary>
    public bool PersistAfterTurn { get; set; } = false;
}
