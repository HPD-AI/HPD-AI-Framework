namespace HPD.Agent.Serialization;

/// <summary>
/// A portable agent configuration paired with its host loading context.
/// </summary>
public sealed record LoadedAgentConfig(
    AgentConfig Config,
    string SourceDirectory);
