namespace Microsoft.Extensions.DependencyInjection;

internal static class TestApplicationServiceCollectionExtensions
{
    internal static IServiceCollection AddTestApplicationCompositions(this IServiceCollection services)
    {
        var generatedType = typeof(HPD.Agent.AspNetCore.Tests.TestEventApplication).Assembly
            .GetType("Microsoft.Extensions.DependencyInjection.GeneratedAgentEventServiceCollectionExtensions", throwOnError: true)!;
        var generatedMethod = generatedType.GetMethod(
            "AddHpdGeneratedAgentEvents",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?? throw new MissingMethodException(generatedType.FullName, "AddHpdGeneratedAgentEvents");
        generatedMethod.Invoke(null, [services]);
        services.AddSingleton(HPD.Agent.Providers.ProviderComposition.Create([]));
        return services;
    }
}
