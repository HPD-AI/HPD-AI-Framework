using HPD.Agent.Providers;
using HPD.Agent.Secrets;
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
        ProviderDiscovery.RegisterProviderFactory(() => new MiniMaxProvider());

        ProviderDiscovery.RegisterProviderConfigType<MiniMaxProviderConfig>(
            "minimax",
            json => JsonSerializer.Deserialize(json, MiniMaxJsonContext.Default.MiniMaxProviderConfig),
            config => JsonSerializer.Serialize(config, MiniMaxJsonContext.Default.MiniMaxProviderConfig));

        SecretAliasRegistry.Register("minimax:ApiKey", "MINIMAX_API_KEY");
        SecretAliasRegistry.Register("minimax:Endpoint", "MINIMAX_ENDPOINT", "MINIMAX_BASE_URL", "MINIMAX_API_BASE");
    }
}
