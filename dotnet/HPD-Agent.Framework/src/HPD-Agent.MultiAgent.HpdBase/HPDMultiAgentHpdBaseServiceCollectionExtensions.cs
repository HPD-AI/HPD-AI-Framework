using HPD.Graph.Base;
using HPD.Agent.MultiAgent.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Agent.MultiAgent.HpdBase;

/// <summary>Registers HPD Base-backed durable multi-agent workflow activation.</summary>
public static class HPDMultiAgentHpdBaseServiceCollectionExtensions
{
    public static IServiceCollection AddHPDMultiAgentHpdBase(
        this IServiceCollection services,
        params BaseGraphActivationDefinition[] graphs)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(graphs);
        if (graphs.Length == 0 || graphs.Select(static graph => graph.GraphId).Distinct(StringComparer.Ordinal).Count() != graphs.Length)
            throw new ArgumentException("At least one uniquely identified installed graph is required.", nameof(graphs));

        services.AddHPDMultiAgentAspNetCore();
        foreach (BaseGraphActivationDefinition graph in graphs)
            services.AddSingleton(graph);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IHPDMultiAgentWorkflowBackend,
            HPDMultiAgentHpdBaseWorkflowBackend>());
        return services;
    }
}
