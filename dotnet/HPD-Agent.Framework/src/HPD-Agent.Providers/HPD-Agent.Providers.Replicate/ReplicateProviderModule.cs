using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Replicate;

/// <summary>
/// Auto-discovers and registers the Replicate provider on assembly load.
/// </summary>
public static class ReplicateProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderContributionRegistry.RegisterProviderFactory(() => new ReplicateProvider());

        ProviderContributionRegistry.RegisterProviderConfigType<ReplicateProviderConfig>(
            "replicate",
            ProviderClientFamily.ImageGeneration,
            json => JsonSerializer.Deserialize(json, ReplicateJsonContext.Default.ReplicateProviderConfig),
            config => JsonSerializer.Serialize(config, ReplicateJsonContext.Default.ReplicateProviderConfig));

        ProviderContributionRegistry.RegisterSecretAlias("replicate:ApiKey", "REPLICATE_API_KEY", "REPLICATE_API_TOKEN");
    }
}
