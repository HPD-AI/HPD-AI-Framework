using HPD.Agent.Providers;

namespace HPD.Agent.ActionInvocation.Tests;

internal static class SubAgentTestPolicies
{
    internal static SubAgentExecutionPolicy Default => SubAgentExecutionPolicy.Create(
        initialRunConfig: null,
        new AgentClientsConfig { Chat = new ChatClientConfig
        {
            Provider = new ProviderReference { Key = "test" },
            ModelName = "test-model"
        } },
        new Dictionary<ProviderClientFamily, SubAgentClientSelectionSource>
        {
            [ProviderClientFamily.Chat] = SubAgentClientSelectionSource.ControllerResolved
        },
        new AgentSecurityRunConfig(),
        new NoSubAgentClientPropagation());
}
