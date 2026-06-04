namespace HPD.Agent;

/// <summary>
/// Options for configuring session persistence behavior.
/// </summary>
public class SessionRepositoryOptions
{
    /// <summary>
    /// Whether to automatically save session metadata and the active branch
    /// after each completed turn.
    /// When false, callers are responsible for explicitly saving changed
    /// session and branch state.
    /// Default: false (manual save).
    /// </summary>
    public bool PersistAfterTurn { get; set; } = false;
}
