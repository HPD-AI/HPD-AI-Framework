using System.Runtime.CompilerServices;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;

namespace HPD.Agent;

/// <summary>
/// Auto-discovers and loads HPD-Agent extension libraries and provider assemblies.
/// This ModuleInitializer runs automatically in both JIT and AOT scenarios.
/// Loads: HPD-Agent.Audio, HPD-Agent.MCP, and LLM providers (HPD-Agent.Providers.*).
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

        // 2. Initialize LLM providers
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
            // Get the directory containing HPD-Agent assembly
            var hpdAgentAssembly = typeof(AgentBuilder).Assembly;
#pragma warning disable IL3000 // Intentional fallback handling for single-file apps
            var assemblyPath = hpdAgentAssembly.Location;
#pragma warning restore IL3000

            if (string.IsNullOrEmpty(assemblyPath))
            {
                // In single-file publish, Location is empty. Use AppContext.BaseDirectory
                assemblyPath = AppContext.BaseDirectory;
            }

            if (string.IsNullOrEmpty(assemblyPath))
            {
                // Final fallback: try entry assembly
                var entryAssembly = Assembly.GetEntryAssembly();
                if (entryAssembly != null)
                {
#pragma warning disable IL3000 // Intentional fallback handling for single-file apps
                    assemblyPath = entryAssembly.Location;
#pragma warning restore IL3000
                }
            }

            if (string.IsNullOrEmpty(assemblyPath))
                return;

            var directory = Path.GetDirectoryName(assemblyPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return;

            // 1. Load extension libraries (which may auto-discover their own providers)
            TryLoadExtensionLibrary(directory, "HPD-Agent.Audio.dll");
            TryLoadExtensionLibrary(directory, "HPD-Agent.MCP.dll");
            TryLoadExtensionLibrary(directory, "HPD-Agent.OpenApi.dll");

            // 2. Scan for LLM provider assemblies
            var providerPattern = "HPD-Agent.Providers.*.dll";
            var providerFiles = Directory.GetFiles(directory, providerPattern);

            foreach (var providerFile in providerFiles)
            {
                try
                {
                    // Load the assembly (triggers its ModuleInitializer)
                    var assemblyName = AssemblyName.GetAssemblyName(providerFile);
                    var loadedAssembly = Assembly.Load(assemblyName);

                    // Explicitly trigger module constructor to ensure ModuleInitializers run
                    RuntimeHelpers.RunModuleConstructor(loadedAssembly.ManifestModule.ModuleHandle);
                }
                catch
                {
                    // Silently ignore failures - provider might not be needed
                    // or might have dependency issues
                }
            }
        }
        catch
        {
            // Silently ignore - extension/provider discovery is a best-effort feature
            // Extensions and providers can still be loaded manually if needed
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
                var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
                var loadedAssembly = Assembly.Load(assemblyName);

                // Trigger module constructor to ensure the extension's ModuleInitializer runs
                RuntimeHelpers.RunModuleConstructor(loadedAssembly.ManifestModule.ModuleHandle);
            }
        }
        catch
        {
            // Silently ignore - extension might not be needed
        }
    }
#endif
}
