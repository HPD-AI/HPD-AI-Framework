namespace HPD.Agent;

/// <summary>
/// Options for configuring agent definition persistence behavior.
/// </summary>
public class AgentRepositoryOptions
{
    /// <summary>
    /// Whether <see cref="AgentBuilder.BuildAsync"/> should save the final
    /// <see cref="StoredAgent"/> definition back to the configured
    /// <see cref="IAgentRepository"/>.
    /// </summary>
    public bool PersistOnBuild { get; set; } = false;
}
