using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Cohere;

/// <summary>
/// Auto-discovers and registers the Cohere provider on assembly load.
/// Also registers the provider-specific config type for FFI/JSON serialization.
/// </summary>
public static class CohereProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        ProviderContributionRegistry.RegisterProviderFactory(() => new CohereProvider());

        ProviderContributionRegistry.RegisterProviderConfigType<CohereProviderConfig>(
            "cohere",
            json => JsonSerializer.Deserialize(json, CohereJsonContext.Default.CohereProviderConfig),
            config => JsonSerializer.Serialize(config, CohereJsonContext.Default.CohereProviderConfig));
        ProviderContributionRegistry.RegisterProviderConfigType<CohereProviderConfig>(
            "cohere",
            ProviderClientFamily.Embeddings,
            json => JsonSerializer.Deserialize(json, CohereJsonContext.Default.CohereProviderConfig),
            config => JsonSerializer.Serialize(config, CohereJsonContext.Default.CohereProviderConfig));

        ProviderContributionRegistry.RegisterSecretAlias("cohere:ApiKey", "COHERE_API_KEY");
    }
}
