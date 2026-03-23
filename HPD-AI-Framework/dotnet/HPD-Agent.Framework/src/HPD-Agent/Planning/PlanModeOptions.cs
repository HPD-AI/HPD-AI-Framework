namespace HPD.Agent.Planning;

/// <summary>
/// Configuration options for Plan Mode.
/// </summary>
/// <remarks>
/// <para><b>Session Persistence:</b></para>
/// <para>
/// Plans are automatically persisted to the session via MiddlewareState (PlanModePersistentStateData).
/// Plans will automatically survive across agent runs within the same session.
/// </para>
/// </remarks>
public class PlanModeOptions
{
    /// <summary>
    /// Whether plan mode is enabled.
    /// Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Custom instructions to add to system prompt explaining plan mode usage.
    /// If null, no additional instructions are added (plan injection handles it).
    /// </summary>
    public string? CustomInstructions { get; set; }

    /// <summary>
    /// Enables or disables plan mode.
    /// </summary>
    public PlanModeOptions WithEnabled(bool enabled)
    {
        Enabled = enabled;
        return this;
    }

    /// <summary>
    /// Sets custom instructions for plan mode usage.
    /// </summary>
    public PlanModeOptions WithCustomInstructions(string instructions)
    {
        CustomInstructions = instructions;
        return this;
    }
}
