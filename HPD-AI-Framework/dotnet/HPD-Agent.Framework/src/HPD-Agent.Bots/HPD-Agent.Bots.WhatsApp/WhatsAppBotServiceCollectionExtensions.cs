using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Agent.Bots.WhatsApp;

public static partial class WhatsAppBotServiceCollectionExtensions
{
    /// <summary>
    /// Registers the WhatsApp bot plus its supporting Cloud API services. Calls the
    /// generated single-parameter overload for options, bot registration, streaming
    /// options, registry entries, and session mapping.
    /// </summary>
    public static IServiceCollection AddWhatsappBot(
        this IServiceCollection services,
        Action<WhatsAppBotConfig> configure,
        bool registerInfrastructure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddWhatsappBot(configure);

        if (registerInfrastructure)
        {
            services.AddHttpClient("whatsapp", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });
            services.TryAddSingleton<WhatsAppApiClient>();
            services.TryAddSingleton<WhatsAppFormatConverter>();
        }

        return services;
    }
}
