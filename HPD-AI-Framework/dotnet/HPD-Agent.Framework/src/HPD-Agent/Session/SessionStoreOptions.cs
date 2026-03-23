namespace HPD.Agent;

/// <summary>
/// Options for configuring session persistence behavior.
/// </summary>
public class SessionStoreOptions
{
    /// <summary>
    /// Whether to automatically save session snapshot after each turn completes.
    /// When false, you must call SaveSessionAsync() manually.
    /// Default: false (manual save).
    /// </summary>
    public bool PersistAfterTurn { get; set; } = false;
}
