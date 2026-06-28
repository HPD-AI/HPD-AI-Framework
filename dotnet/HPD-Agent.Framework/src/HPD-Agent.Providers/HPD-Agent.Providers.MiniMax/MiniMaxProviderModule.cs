using HPD.Agent.Providers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HPD.Agent.Providers.MiniMax;

public static class MiniMaxProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderContributionRegistry.RegisterProviderFactory(() => new MiniMaxProvider());

        ProviderContributionRegistry.RegisterProviderConfigType<MiniMaxProviderConfig>(
            "minimax",
            json => JsonSerializer.Deserialize(json, MiniMaxJsonContext.Default.MiniMaxProviderConfig),
            config => JsonSerializer.Serialize(config, MiniMaxJsonContext.Default.MiniMaxProviderConfig));

        ProviderContributionRegistry.RegisterSecretAlias("minimax:ApiKey", "MINIMAX_API_KEY");
        ProviderContributionRegistry.RegisterSecretAlias("minimax:Endpoint", "MINIMAX_ENDPOINT", "MINIMAX_BASE_URL", "MINIMAX_API_BASE");
    }
}
