namespace HPD.Agent;

internal static class AgentBuilderDefaults
{
    internal static IAgentStore AgentStore { get; } = new InMemoryAgentStore();
}
