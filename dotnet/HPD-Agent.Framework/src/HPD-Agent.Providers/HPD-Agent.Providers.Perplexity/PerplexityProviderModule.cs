using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HPD.Agent.Providers.Perplexity;

public static class PerplexityProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderDiscovery.RegisterProviderFactory(() => new PerplexityProvider());

        ProviderDiscovery.RegisterProviderConfigType<PerplexityProviderConfig>(
            "perplexity",
            json => JsonSerializer.Deserialize(json, PerplexityJsonContext.Default.PerplexityProviderConfig),
            config => JsonSerializer.Serialize(config, PerplexityJsonContext.Default.PerplexityProviderConfig));

        SecretAliasRegistry.Register("perplexity:ApiKey", "PERPLEXITY_API_KEY");
        SecretAliasRegistry.Register("perplexity:Endpoint", "PERPLEXITY_ENDPOINT", "PERPLEXITY_BASE_URL");
    }
}
