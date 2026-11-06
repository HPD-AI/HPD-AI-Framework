using HPD.Agent;

namespace HPD.Agent.Tests;

internal static class ToolHarnessFactoryTestExtensions
{
    internal static object CreateInstance(this ToolHarnessFactory factory)
    {
        var activation = factory.Activate
            ?? throw new InvalidOperationException($"Harness '{factory.ActivationIdentity}' has no activation delegate.");
        return activation(new ToolHarnessConstructionContext(
            factory.ActivationIdentity,
            services: null,
            overrides: new Dictionary<Type, object>(),
            secrets: null,
            config: null,
            defaults: [],
            lifetime: new ToolHarnessOwnerLifetime()));
    }
}
