namespace HPD.Agent;

/// <summary>
/// Options for configuring agent definition persistence behavior.
/// </summary>
public class AgentStoreOptions
{
    /// <summary>
    /// Whether <see cref="AgentBuilder.BuildAsync"/> should save the final
    /// <see cref="StoredAgent"/> definition back to the configured
    /// <see cref="IAgentStore"/>.
    /// </summary>
    public bool PersistOnBuild { get; set; } = false;
}
