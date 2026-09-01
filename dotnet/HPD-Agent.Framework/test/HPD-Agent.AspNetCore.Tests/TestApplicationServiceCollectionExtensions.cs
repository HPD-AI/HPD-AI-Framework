namespace Microsoft.Extensions.DependencyInjection;

internal static class TestApplicationServiceCollectionExtensions
{
    internal static IServiceCollection AddTestApplicationCompositions(this IServiceCollection services)
    {
        services.AddHpdGeneratedAgentEvents();
        services.AddSingleton(HPD.Agent.Providers.ProviderComposition.Create([]));
        return services;
    }
}
