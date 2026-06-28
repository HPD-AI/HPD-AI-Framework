using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Groq;

/// <summary>
/// Auto-discovers and registers the Groq provider on assembly load.
/// </summary>
public static class GroqProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderContributionRegistry.RegisterProviderFactory(() => new GroqProvider());

        ProviderContributionRegistry.RegisterProviderConfigType<GroqProviderConfig>(
            "groq",
            json => JsonSerializer.Deserialize(json, GroqJsonContext.Default.GroqProviderConfig),
            config => JsonSerializer.Serialize(config, GroqJsonContext.Default.GroqProviderConfig));

        ProviderContributionRegistry.RegisterSecretAlias("groq:ApiKey", "GROQ_API_KEY");
        ProviderContributionRegistry.RegisterSecretAlias("groq:Endpoint", "GROQ_ENDPOINT");
    }
}
