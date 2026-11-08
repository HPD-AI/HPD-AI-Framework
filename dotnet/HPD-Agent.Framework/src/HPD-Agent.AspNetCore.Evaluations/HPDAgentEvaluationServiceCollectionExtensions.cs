using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Agent.AspNetCore.Evaluations;

/// <summary>Registers optional evaluation API serialization support.</summary>
public static class HPDAgentEvaluationServiceCollectionExtensions
{
    /// <summary>Adds evaluation API response metadata to the host JSON resolver chain.</summary>
    public static IServiceCollection AddHPDAgentEvaluations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<JsonOptions>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<JsonOptions>, HPDAgentEvaluationJsonOptionsSetup>());
        return services;
    }

    private sealed class HPDAgentEvaluationJsonOptionsSetup : IConfigureOptions<JsonOptions>
    {
        public void Configure(JsonOptions options) =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                HPDAgentEvaluationJsonSerializerContext.Default);
    }
}
