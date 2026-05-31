using System.Runtime.CompilerServices;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;

namespace HPD.Agent;

/// <summary>
/// Auto-discovers and loads HPD-Agent extension libraries and provider assemblies.
/// This ModuleInitializer runs automatically in both JIT and AOT scenarios.
/// Loads: HPD-Agent.Audio, HPD-Agent.MCP, HPD-Agent.Harness.*, and provider packages
/// (HPD-Agent.Providers.* and HPD-Agent.AudioProviders.*).
/// </summary>
internal static class AutoDiscovery
{
    private static bool _initialized = false;
    private static readonly object _lock = new();

    /// <summary>
    /// Module initializer that runs when HPD-Agent assembly is first loaded.
    /// Attempts to load extension libraries and provider assemblies to trigger their ModuleInitializers.
    /// </summary>
#pragma warning disable CA2255 // ModuleInitializer is intentionally used in library for auto-discovery
    [ModuleInitializer]
    public static void Initialize()
#pragma warning restore CA2255
    {
        lock (_lock)
        {
            if (_initialized) return;

#if !NATIVE_AOT
            // In non-AOT scenarios, try to auto-discover extension libraries and providers
            TryLoadExtensionsAndProviders();
#else
            // In AOT scenarios, explicitly trigger ModuleInitializers
            // This ensures they're not trimmed away by the AOT compiler
            TryInitializeKnownExtensionsAndProviders();
#endif

            _initialized = true;
        }
    }

#if NATIVE_AOT
    /// <summary>
    /// Explicitly triggers ModuleInitializers for known extension libraries and provider modules in AOT scenarios.
    /// This prevents the AOT trimmer from removing extensions/providers that appear unused.
    /// Uses conditional compilation and weak references to only include libraries
    /// that the app actually references.
    /// </summary>
    private static void TryInitializeKnownExtensionsAndProviders()
    {
        // Each library is tried individually with weak references
        // If the app doesn't reference a library, the weak reference will fail gracefully

        // 1. Initialize extension libraries (which may auto-discover their own providers)
        TryInitializeByTypeName("HPD.Agent.Audio.AudioProviderAutoDiscovery, HPD-Agent.Audio");
        TryInitializeByTypeName("HPD.Agent.MCP.MCPAutoDiscovery, HPD-Agent.MCP");
        TryInitializeByTypeName("HPD.Agent.OpenApi.OpenApiAutoDiscovery, HPD-Agent.OpenApi");

        // 2. Initialize provider packages
        TryInitializeByTypeName("HPD.Agent.Providers.OpenAI.OpenAIProviderModule, HPD-Agent.Providers.OpenAI");
        TryInitializeByTypeName("HPD.Agent.Providers.Anthropic.AnthropicProviderModule, HPD-Agent.Providers.Anthropic");
        TryInitializeByTypeName("HPD.Agent.Providers.GoogleAI.GoogleAIProviderModule, HPD-Agent.Providers.GoogleAI");
        TryInitializeByTypeName("HPD.Agent.Providers.AzureAIInference.AzureAIInferenceProviderModule, HPD-Agent.Providers.AzureAIInference");
        TryInitializeByTypeName("HPD.Agent.Providers.Bedrock.BedrockProviderModule, HPD-Agent.Providers.Bedrock");
        TryInitializeByTypeName("HPD.Agent.Providers.Ollama.OllamaProviderModule, HPD-Agent.Providers.Ollama");
        TryInitializeByTypeName("HPD.Agent.Providers.Mistral.MistralProviderModule, HPD-Agent.Providers.Mistral");
        TryInitializeByTypeName("HPD.Agent.Providers.HuggingFace.HuggingFaceProviderModule, HPD-Agent.Providers.HuggingFace");
        TryInitializeByTypeName("HPD.Agent.Providers.OnnxRuntime.OnnxRuntimeProviderModule, HPD-Agent.Providers.OnnxRuntime");
        TryInitializeByTypeName("HPD.Agent.Providers.OpenRouter.OpenRouterProviderModule, HPD-Agent.Providers.OpenRouter");
        TryInitializeByTypeName("HPD.Agent.AudioProviders.OpenAI.OpenAIAudioProviderModule, HPD-Agent.AudioProviders.OpenAI");
        TryInitializeByTypeName("HPD.Agent.AudioProviders.ElevenLabs.ElevenLabsProviderModule, HPD-Agent.AudioProviders.ElevenLabs");
        TryInitializeByTypeName("HPD.Agent.AudioProviders.Silero.SileroVadProviderModule, HPD-Agent.AudioProviders.Silero");
    }

    /// <summary>
    /// Attempts to load and initialize an extension or provider module by assembly-qualified type name.
    /// </summary>
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "HPD.Agent.Audio.AudioProviderAutoDiscovery", "HPD-Agent.Audio")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "HPD.Agent.MCP.MCPAutoDiscovery", "HPD-Agent.MCP")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "HPD.Agent.OpenApi.OpenApiAutoDiscovery", "HPD-Agent.OpenApi")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "HPD.Agent.Providers.OpenAI.OpenAIProviderModule", "HPD-Agent.Providers.OpenAI")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "HPD.Agent.Providers.Anthropic.AnthropicProviderModule", "HPD-Agent.Providers.Anthropic")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "HPD.Agent.Providers.GoogleAI.GoogleAIProviderModule", "HPD-Agent.Providers.GoogleAI")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "HPD.Agent.Providers.AzureAIInference.AzureAIInferenceProviderModule", "HPD-Agent.Providers.AzureAIInference")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "HPD.Agent.Providers.Bedrock.BedrockProviderModule", "HPD-Agent.Providers.Bedrock")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "HPD.Agent.Providers.Ollama.OllamaProviderModule", "HPD-Agent.Providers.Ollama")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "HPD.Agent.Providers.Mistral.MistralProviderModule", "HPD-Agent.Providers.Mistral")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "HPD.Agent.Providers.HuggingFace.HuggingFaceProviderModule", "HPD-Agent.Providers.HuggingFace")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "HPD.Agent.Providers.OnnxRuntime.OnnxRuntimeProviderModule", "HPD-Agent.Providers.OnnxRuntime")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "HPD.Agent.Providers.OpenRouter.OpenRouterProviderModule", "HPD-Agent.Providers.OpenRouter")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "HPD.Agent.AudioProviders.OpenAI.OpenAIAudioProviderModule", "HPD-Agent.AudioProviders.OpenAI")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "HPD.Agent.AudioProviders.ElevenLabs.ElevenLabsProviderModule", "HPD-Agent.AudioProviders.ElevenLabs")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "HPD.Agent.AudioProviders.Silero.SileroVadProviderModule", "HPD-Agent.AudioProviders.Silero")]
    private static void TryInitializeByTypeName(string assemblyQualifiedTypeName)
    {
        try
        {
            var type = Type.GetType(assemblyQualifiedTypeName, throwOnError: false);
            if (type != null)
            {
                RuntimeHelpers.RunModuleConstructor(type.Module.ModuleHandle);
            }
        }
        catch
        {
            // Silently ignore - provider might not be referenced or available
        }
    }
#endif

#if !NATIVE_AOT
    /// <summary>
    /// Attempts to scan and load extension libraries and provider assemblies in non-AOT scenarios.
    /// This provides automatic discovery without requiring user configuration.
    /// </summary>
    private static void TryLoadExtensionsAndProviders()
    {
        try
        {
            var directory = AppContext.BaseDirectory;
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return;

            directory = Path.GetFullPath(directory);

            // 1. Load extension libraries (which may auto-discover their own providers)
            TryLoadExtensionLibrary(directory, "HPD-Agent.Audio.dll");
            TryLoadExtensionLibrary(directory, "HPD-Agent.MCP.dll");
            TryLoadExtensionLibrary(directory, "HPD-Agent.OpenApi.dll");

            // 2. Scan for harness assemblies so string-based AgentConfig harnesses can resolve.
            foreach (var harnessFile in Directory.GetFiles(directory, "HPD-Agent.Harness.*.dll"))
            {
                TryLoadAssemblyAndRunModuleInitializer(harnessFile);
            }

            // 3. Scan for provider assemblies.
            TryLoadProviderAssemblies(directory, "HPD-Agent.Providers.*.dll");
            TryLoadProviderAssemblies(directory, "HPD-Agent.AudioProviders.*.dll");
        }
        catch
        {
            // Silently ignore - extension/provider discovery is a best-effort feature
            // Extensions and providers can still be loaded manually if needed
        }
    }

    private static void TryLoadProviderAssemblies(string directory, string pattern)
    {
        foreach (var providerFile in Directory.GetFiles(directory, pattern))
        {
            TryLoadAssemblyAndRunModuleInitializer(providerFile);
        }
    }

    private static void TryLoadAssemblyAndRunModuleInitializer(string assemblyPath)
    {
        try
        {
            var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
            var loadedAssembly = Assembly.Load(assemblyName);

            // Explicitly trigger module constructor to ensure ModuleInitializers run
            RuntimeHelpers.RunModuleConstructor(loadedAssembly.ManifestModule.ModuleHandle);
        }
        catch
        {
            // Silently ignore failures - extension/provider might not be needed
            // or might have dependency issues.
        }
    }

    /// <summary>
    /// Attempts to load an HPD-Agent extension library by filename.
    /// Extension libraries may have their own ModuleInitializers that auto-discover providers.
    /// </summary>
    private static void TryLoadExtensionLibrary(string directory, string filename)
    {
        try
        {
            var assemblyPath = Path.Combine(directory, filename);
            if (File.Exists(assemblyPath))
            {
                TryLoadAssemblyAndRunModuleInitializer(assemblyPath);
            }
        }
        catch
        {
            // Silently ignore - extension might not be needed
        }
    }
#endif
}
