using System.Runtime.CompilerServices;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;

namespace HPD.Agent.Providers.OpenRouter;

/// <summary>
/// Auto-discovers and registers the OpenRouter provider on assembly load.
/// </summary>
public static class OpenRouterProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        ProviderDiscovery.RegisterProviderFactory(() => new OpenRouterProvider());

        // Register environment variable alias for unified secret resolution
        SecretAliasRegistry.Register("openrouter:ApiKey", "OPENROUTER_API_KEY");
    }
}
