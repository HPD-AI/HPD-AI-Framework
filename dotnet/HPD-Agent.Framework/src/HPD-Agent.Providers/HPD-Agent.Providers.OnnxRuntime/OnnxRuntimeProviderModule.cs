using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.OnnxRuntime;

/// <summary>
/// Auto-discovers and registers the ONNX Runtime provider on assembly load.
/// Also registers the provider-specific config type for FFI/JSON serialization.
/// </summary>
public static class OnnxRuntimeProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        // Register provider factory
        ProviderContributionRegistry.RegisterProviderFactory(() => new OnnxRuntimeProvider());

        // Register config type for FFI/JSON serialization (AOT-compatible)
        ProviderContributionRegistry.RegisterProviderConfigType<OnnxRuntimeProviderConfig>(
            "onnx-runtime",
            json => JsonSerializer.Deserialize(json, OnnxRuntimeJsonContext.Default.OnnxRuntimeProviderConfig),
            config => JsonSerializer.Serialize(config, OnnxRuntimeJsonContext.Default.OnnxRuntimeProviderConfig));

        ProviderContributionRegistry.RegisterSecretAlias("onnx-runtime:ModelPath", "ONNX_MODEL_PATH");
        ProviderContributionRegistry.RegisterSecretAlias("onnx-runtime:ModelPath", "ONNX_RUNTIME_MODEL_PATH");
    }
}
