using HPD.Agent.Hosting.Lifecycle;

namespace HPD.Agent.AspNetCore.DependencyInjection;

internal sealed class HPDAgentHostingServicesProvider : IHPDAgentHostingServicesProvider
{
    private readonly HPDAgentRegistry _registry;

    public HPDAgentHostingServicesProvider(HPDAgentRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public HPDAgentHostingServices Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _registry.Get(name).HostingServices;
    }
}
