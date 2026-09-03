using HPD.Agent.Providers;

namespace HPD.Agent.ActionInvocation.Tests;

internal static class SubAgentTestPolicies
{
    internal static SubAgentExecutionPolicy Default => SubAgentExecutionPolicy.Create(
        initialRunConfig: null,
        new ChatClientConfig
        {
            Provider = new ProviderReference { Key = "test" },
            ModelName = "test-model"
        },
        SubAgentClientSelectionSource.ControllerResolved,
        new NoSubAgentClientPropagation());
}
