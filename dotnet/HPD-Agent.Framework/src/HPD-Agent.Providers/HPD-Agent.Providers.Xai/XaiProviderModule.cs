using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;

namespace HPD.Agent.Providers.Xai;

/// <summary>
/// Auto-discovers and registers the xAI provider on assembly load.
/// </summary>
public static class XaiProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderDiscovery.RegisterProviderFactory(() => new XaiProvider());

        ProviderDiscovery.RegisterProviderConfigType<XaiProviderConfig>(
            "xai",
            json => JsonSerializer.Deserialize(json, XaiJsonContext.Default.XaiProviderConfig),
            config => JsonSerializer.Serialize(config, XaiJsonContext.Default.XaiProviderConfig));

        SecretAliasRegistry.Register("xai:ApiKey", "XAI_API_KEY");
        SecretAliasRegistry.Register("xai:Endpoint", "XAI_ENDPOINT", "XAI_BASE_URL");
    }
}
