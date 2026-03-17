// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;

namespace HPD.Agent.Audio;

/// <summary>
/// Auto-discovers and loads audio provider assemblies when HPD-Agent.Audio library is loaded.
/// This ModuleInitializer runs automatically in both JIT and AOT scenarios.
/// </summary>
internal static class AudioProviderAutoDiscovery
{
    private static bool _initialized = false;
    private static readonly object _lock = new();

    /// <summary>
    /// Module initializer that runs when HPD-Agent.Audio assembly is first loaded.
    /// Attempts to load audio provider assemblies to trigger their ModuleInitializers.
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
            // In non-AOT scenarios, try to auto-discover provider assemblies
            TryLoadAudioProviderAssemblies();
#else
            // In AOT scenarios, explicitly trigger provider ModuleInitializers
            // This ensures they're not trimmed away by the AOT compiler
            TryInitializeKnownAudioProviders();
#endif

            _initialized = true;
        }
    }

#if NATIVE_AOT
    /// <summary>
    /// Explicitly triggers ModuleInitializers for known audio provider modules in AOT scenarios.
    /// This prevents the AOT trimmer from removing providers that appear unused.
    /// Uses conditional compilation and weak references to only include providers
    /// that the app actually references.
    /// </summary>
    private static void TryInitializeKnownAudioProviders()
    {
        // Each provider is tried individually with weak references
        // If the app doesn't reference a provider, the weak reference will fail gracefully

        // Use reflection to dynamically check and load, which AOT will handle gracefully
        TryInitializeProviderByTypeName("HPD.Agent.AudioProviders.OpenAI.OpenAIAudioProviderModule, HPD-Agent.AudioProviders.OpenAI");
        TryInitializeProviderByTypeName("HPD.Agent.AudioProviders.ElevenLabs.ElevenLabsProviderModule, HPD-Agent.AudioProviders.ElevenLabs");
        // Add more audio providers here as they are created
    }

    /// <summary>
    /// Attempts to load and initialize an audio provider module by assembly-qualified type name.
    /// </summary>
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "HPD.Agent.AudioProviders.OpenAI.OpenAIAudioProviderModule", "HPD-Agent.AudioProviders.OpenAI")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "HPD.Agent.AudioProviders.ElevenLabs.ElevenLabsProviderModule", "HPD-Agent.AudioProviders.ElevenLabs")]
    private static void TryInitializeProviderByTypeName(string assemblyQualifiedTypeName)
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
    /// Attempts to scan and load audio provider assemblies in non-AOT scenarios.
    /// This provides automatic discovery without requiring user configuration.
    /// </summary>
    private static void TryLoadAudioProviderAssemblies()
    {
        try
        {
            // Get the directory containing HPD-Agent.Audio assembly
            var audioAssembly = typeof(AudioConfig).Assembly;
#pragma warning disable IL3000 // Intentional fallback handling for single-file apps
            var assemblyPath = audioAssembly.Location;
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

            // Scan for audio provider assemblies
            var providerPattern = "HPD-Agent.AudioProviders.*.dll";
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
            // Silently ignore - provider discovery is a best-effort feature
            // Providers can still be registered manually if needed
        }
    }
#endif
}
