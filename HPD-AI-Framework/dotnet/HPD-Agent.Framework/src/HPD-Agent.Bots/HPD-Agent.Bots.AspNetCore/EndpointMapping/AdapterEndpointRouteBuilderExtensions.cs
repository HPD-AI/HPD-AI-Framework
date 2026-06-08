using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Bots.AspNetCore.EndpointMapping;

/// <summary>
/// Convenience extension for mapping all registered adapters at once.
/// </summary>
public static class BotEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps every adapter registered in the assembly via <see cref="HpdBotAttribute"/>
    /// to its default webhook path.
    /// </summary>
    /// <remarks>
    /// Equivalent to calling <c>MapXxxWebhook()</c> for each adapter individually.
    /// Uses <see cref="IBotRegistryProvider"/> registrations, including the
    /// provider emitted by the source generator for generated adapters.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Map all adapters at their default paths:
    /// app.MapHPDBots();
    ///
    /// // Or map individually with custom paths:
    /// app.MapSlackWebhook("/webhooks/slack");
    /// app.MapTeamsWebhook("/webhooks/teams");
    /// </code>
    /// </example>
    public static IEndpointRouteBuilder MapHPDBots(this IEndpointRouteBuilder app)
    {
        // This method is a no-op if no adapters registered a registry provider.
        // Each adapter's MapEndpoint delegate calls the generated MapXxxWebhook() extension.
        foreach (var registration in GetRegistrations(app))
        {
            registration.MapEndpoint(app, registration.DefaultPath);
        }

        return app;
    }

    // Returns all adapter registrations contributed through DI.
    private static IEnumerable<BotRegistration> GetRegistrations(IEndpointRouteBuilder app)
    {
        return app.ServiceProvider
            .GetServices<IBotRegistryProvider>()
            .SelectMany(provider => provider.GetAll());
    }
}
